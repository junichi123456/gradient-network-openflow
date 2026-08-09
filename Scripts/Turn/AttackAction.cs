using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;
using MysteryDungeon.Dungeon;
using MysteryDungeon.Grid;
using MysteryDungeon.UI;

namespace MysteryDungeon.Turn;

// Damage is computed by DamageCalculator (Phase 16's DamageContext
// pipeline) - see that class for the formula itself. TypeEffectiveness
// is still the real value from TypeChartManager (a system multiplier,
// not a "buff"). AtkMultiplier/DefMultiplier/PowerMultiplier are fed by
// Phase 21's rank system; crit and the 300-move mechanics (recoil, self-
// stun, AoE ranges) layer on top.
//
// Range dispatch: Adjacent stays the single-target/bump path (unchanged
// - regression 0). Every other range routes through the AoE path, which
// resolves a target list (TargetResolver) and applies the SAME per-
// target strike to each, with friendly-fire (see the move-consumption
// proposal §4).
public class AttackAction : IAction
{
    public ITurnActor Actor { get; }

    private readonly Entity _attacker;
    private readonly Entity _defender;
    private readonly MoveSlot _moveSlot;
    private readonly FloorController _floorController;

    public AttackAction(Entity attacker, Entity defender, MoveSlot moveSlot, FloorController floorController = null)
    {
        Actor = attacker;
        _attacker = attacker;
        _defender = defender;
        _moveSlot = moveSlot;
        _floorController = floorController;
    }

    // Floor-wide weather, or None when this action has no FloorController
    // (bare test entities, and any construction site that omits it). Every
    // weather branch below is a strict no-op at None, which is what keeps
    // the Phase 16 damage benchmarks and the accuracy maths untouched.
    private WeatherType Weather => _floorController?.Weather?.Current ?? WeatherType.None;

    // True once this action actually delivered an ATTACK (a Physical or
    // Special move that got past the PP / ざんきょうのしゅごしゃ gates). Read
    // by TurnManager/TurnScheduler for ふわふわ/ゆきすべり, which grant a
    // follow-up step "after attacking" - a Status move (a weather or field
    // cast, a pure buff) is not an attack and must not grant one.
    public bool PerformedAttack { get; private set; }

    public void Execute(int turnNumber)
    {
        var move = ResolveMove(_moveSlot.Data);

        // Out of PP = the move simply fails (turn still consumed).
        if (_moveSlot.CurrentPp <= 0)
        {
            MessageLogger.Log($"{_attacker.ActorName} tried to use {move.Name}, but it has no PP left!", MessageLogger.IneffectiveColor);
            return;
        }

        // ざんきょうのしゅごしゃ (§4, stage 3): the holder is barred from
        // using ATTACK moves at all (Physical/Special) - the standing cost
        // of its ally-shielding half (see StrikeTarget). Status moves stay
        // available, so the holder isn't reduced to a pure Wait. Gated here
        // rather than at the 4 AttackAction construction sites (Player/
        // MenuUI/HostileEntity/AllyEntity) so a single choke point covers
        // every path, exactly like the PP check above. Placed BEFORE the
        // decrement: a forbidden move must not silently burn PP.
        if (move.Category != MoveCategory.Status && HasTrait(_attacker, "zankyou_no_shugosha"))
        {
            MessageLogger.Log($"{_attacker.ActorName} cannot attack - it stands guard instead!", MessageLogger.IneffectiveColor);
            return;
        }

        _moveSlot.CurrentPp--;

        // Self-stun (大技の隙): applied at USE time, regardless of hit or
        // miss (§9-4). Reuses Phase 21's Stun (consumed at the start of
        // the attacker's next action-cycle). Blocked entirely while the
        // user is MudCaked, or the (primary) target holds きょうじんなから
        // だ - trait_catalog_v2 §3 reuses MudCaked's whole block-list
        // wholesale ("泥まみれの実装を流用"), see DefenderBlocksSecondaryEffects.
        if (move.SelfStunNextTurn && !DefenderBlocksSecondaryEffects)
        {
            _attacker.StatusEffects.TryApplyAilment(AilmentType.Stun);
            MessageLogger.Log($"{_attacker.ActorName} must recharge after {move.Name}!", MessageLogger.IneffectiveColor);
        }

        // きぬぬい: arms on USE (hit or miss - "氷技使用後"), regardless of
        // MudCaked/toughness blocking (this is the USER's own trait firing
        // off their own move choice, not a secondary effect being done TO
        // anyone - trait_catalog_v2 §3).
        if (move.Type == "Ice" && HasTrait(_attacker, "kinuinui"))
            _attacker.StatusEffects.ArmDamageReduction();

        // Trap-move field placement (みずたまり/ブルームガーデン/もうどくのきり/
        // うすらひのうえ/クレバス/じわれ/げきりゅう). Resolved at USE time and
        // centred on the USER, so it neither needs nor consults a target -
        // these are Status moves that shape the ground, not attacks.
        if (ApplyFieldEffect(move)) return;

        // Weather-setting moves (はれごい/あまごい/…). Same shape as the field
        // moves above: resolved at use time, no target, fully consumed here.
        if (ApplyWeatherEffect(move)) return;

        // Past every gate above, so this really is an attack being made.
        PerformedAttack = move.Category != MoveCategory.Status;

        // AoE needs the floor (actor enumeration) and the grid; without
        // them (shouldn't happen in-dungeon) fall back to the single path.
        bool canAoe = move.Range != MoveRange.Adjacent && _floorController != null && _attacker.Grid != null;
        if (canAoe)
            ExecuteAoe(move);
        else
            ExecuteSingle(move);

        // チェイサー (§4, stage 3): fires off ANOTHER party member's attack,
        // so it hangs here (after the strike fully resolved) rather than
        // inside the per-target loop - it's one reaction per attack, not
        // one per target hit.
        TriggerChasers(move);

        // Self-destruct (メガトンじばく): the user faints once the move has
        // fully resolved, hit or miss (§ self_guaranteed_death). Applied
        // here - after both paths, after their recoil/drain - so the
        // damage the move dealt still lands first.
        ApplySelfDestruct(move);
    }

    // ---- Single-target / bump path (Adjacent) - Phase 6..21 behaviour ----
    private void ExecuteSingle(MoveData move)
    {
        // Menu-invoked moves may auto-aim to nothing - still costs the turn.
        // A Status move that only affects its USER is the exception: it has
        // nothing to aim at in the first place, so requiring an adjacent
        // enemy would mean a self-buff could only be used while already
        // standing next to something. Every rank move in the pool is
        // Adjacent-ranged, so without this a pure buff simply never worked
        // outside melee.
        if (_defender == null)
        {
            if (move.Category == MoveCategory.Status && move.IsSelfContained && move.RankEffects.Count > 0)
            {
                MessageLogger.Log($"{_attacker.ActorName} used {move.Name}!");
                ApplySelfRankOnce(move);
                return;
            }

            MessageLogger.Log($"{_attacker.ActorName} used {move.Name}, but there was no target! It hit nothing but air.", MessageLogger.IneffectiveColor);
            return;
        }

        _attacker.PlayBumpAttack(_defender.GridPosition);

        // Pure Status move: no damage, only rank/ailment effects.
        if (move.Category == MoveCategory.Status)
        {
            MessageLogger.Log($"{_attacker.ActorName} used {move.Name}!");
            ApplyRankEffectIfAny(move, defenderAlive: true);
            ApplyAilmentEffectIfAny(move, defenderAlive: true);
            return;
        }

        int damage = Strike(move, _defender);
        if (damage > 0)
        {
            bool alive = _defender.Stats.IsAlive;
            ApplyRankEffectIfAny(move, alive);
            ApplyAilmentEffectIfAny(move, alive);
            if (alive) ApplyKnockback(move, _defender);
            if (!alive) HandleFaint(_defender);
        }

        ApplyRendClear(move, damage);
        ApplyDrain(move, damage);
        ApplyRecoil(move, damage);
        ApplyFundoRecoil(damage);
    }

    // ---- Multi-target path (Line/TwoTile/Area/Room/FullFloor) ----
    private void ExecuteAoe(MoveData move)
    {
        // Area centres on the primary defender's tile; without one, on the
        // tile the user faces. Room's corridor fallback uses the same aim.
        var aim = _defender != null ? _defender.GridPosition : _attacker.GridPosition + _attacker.FacingDirection;
        _attacker.PlayBumpAttack(aim);

        var targets = TargetResolver.Resolve(move.Range, _attacker, aim, _attacker.Grid, _floorController);
        if (targets.Count == 0)
        {
            MessageLogger.Log($"{_attacker.ActorName} used {move.Name}, but nothing was in range!", MessageLogger.IneffectiveColor);
            return; // §6: no damage, no recoil (self-stun already applied)
        }

        MessageLogger.Log($"{_attacker.ActorName} used {move.Name}!");

        // Self-targeted rank effect fires once, not per target (§4-2).
        ApplySelfRankOnce(move);

        int totalDamage = 0;
        // Snapshot the list - HandleFaint QueueFree's dead targets, and a
        // Room/FullFloor list can include entities that die mid-loop.
        // エリアイージス(使い切り): 相手が範囲射程の攻撃技を使ったとき、
        // 所持者「以外」の味方はダメージを受けない。所持者自身は庇わない。
        // Room は元々味方に当たらないので、対象は Area と Surrounding。
        Entity aegisHolder = null;
        if (move.Category != MoveCategory.Status
            && (move.Range == MoveRange.Area || move.Range == MoveRange.Surrounding))
        {
            aegisHolder = targets.FirstOrDefault(
                t => GodotObject.IsInstanceValid(t) && t.IsAlive
                     && t.Faction != _attacker.Faction
                     && t.Holds(Combat.BattleItemEffect.CoverAllies));
            if (aegisHolder != null)
            {
                aegisHolder.ConsumeHeldItem();
                MessageLogger.Log($"{aegisHolder.ActorName}のエリアイージスが味方を庇った!",
                                  MessageLogger.EffectiveColor);
            }
        }

        foreach (var target in new List<Entity>(targets))
        {
            if (!GodotObject.IsInstanceValid(target) || !target.IsAlive) continue;

            // 庇われた側は完全に対象外（ダメージも追加効果も乗らない）。
            if (aegisHolder != null && target != aegisHolder
                && target.Faction == aegisHolder.Faction) continue;

            if (move.Category == MoveCategory.Status)
            {
                ApplyAoeAilment(move, target);
                ApplyEnemyRankToTarget(move, target);
                continue;
            }

            int damage = Strike(move, target);
            if (damage <= 0) continue; // missed this target

            totalDamage += damage;
            if (target.Stats.IsAlive)
            {
                ApplyAoeAilment(move, target);
                ApplyEnemyRankToTarget(move, target);
                ApplyKnockback(move, target);
            }
            else
            {
                HandleFaint(target);
            }
        }

        ApplyRendClear(move, totalDamage);
        ApplyDrain(move, totalDamage);
        ApplyRecoil(move, totalDamage);
        ApplyFundoRecoil(totalDamage);
    }

    // Core per-target strike: hit roll -> crit -> Phase 16 damage -> burn
    // penalty -> apply. Returns damage dealt (0 = missed). Shared verbatim
    // by the single and AoE paths so both roll accuracy/crit/damage
    // identically. Does NOT apply secondary rank/ailment or death - the
    // caller sequences those (secondary effects run on a still-alive
    // target before death processing).
    // forceHit skips the accuracy roll entirely. Only Variable2To5's
    // follow-up hits pass true: that shape rolls accuracy ONCE for the whole
    // move, so hits 2..N must not each get another chance to miss.
    // ガードトニック(HP50%未満で最大HPの25%回復) / ラストトニック(33%以下で50%回復)。
    // どちらも使い切り。被弾でHPが動いた直後に判定する。
    private static void TryHpThresholdItem(Entity e)
    {
        if (!e.IsAlive) return;
        var st = e.Stats;
        float ratio = st.MaxHp <= 0 ? 1f : (float)st.CurrentHp / st.MaxHp;

        float healRate = e.HeldEffect switch
        {
            Combat.BattleItemEffect.HealAt50 when ratio < 0.50f => 0.25f,
            Combat.BattleItemEffect.HealAt33 when ratio <= 0.33f => 0.50f,
            _ => 0f,
        };
        if (healRate <= 0f) return;

        e.ConsumeHeldItem();
        int heal = Mathf.Max(1, Mathf.FloorToInt(st.MaxHp * healRate));
        st.Heal(heal);
        MessageLogger.Log($"{e.ActorName}はHPを{heal}回復した!", MessageLogger.EffectiveColor);
    }

    private int StrikeTarget(MoveData move, Entity target, bool forceHit = false)
    {
        // Darkness (暗闇): a direct x0.7 on the afflicted attacker's own
        // outgoing hit chance, separate from the AccuracyRank ladder
        // (status-redesign §4-4). Skipped, like the rank multipliers
        // already are, when IsGuaranteedHit short-circuits the roll.
        float darknessMul = _attacker.StatusEffects.IsInDarkness ? 0.7f : 1.0f;

        // クイックステップ (§3): always +1 evasion rank, folded into the
        // same table lookup the ordinary Evasion rank uses.
        // かげろうボディ: the same +1, but only while the weather is はれ.
        // Additive with クイックステップ - the rank ladder clamps the total,
        // so a holder of both is not a special case.
        int evasionBonus = (HasTrait(target, "quick_step") ? 1 : 0)
            + (Weather == WeatherType.Sunny && HasTrait(target, "kagerou_body") ? 1 : 0);

        // IsGuaranteedHit bypasses the roll and the target's evasion rank.
        // じゆうのつばさ (stage 9 §1): +1 accuracy rank per OTHER Dragon-or-
        // Dark party member, capped at +2 ranks.
        int accuracyBonus = HasTrait(_attacker, "jiyuu_no_tsubasa")
            ? Mathf.Min(2, PartyElementCensus.CountAlliesWithEitherType(_attacker, _floorController?.AllActors(), Element.Dragon, Element.Dark))
            : 0;

        // ---- Weather, accuracy half ----
        // きょうふう: a Wind-tagged move can't miss. Treated exactly like the
        // move's own IsGuaranteedHit, so it also bypasses the target's
        // evasion rank and Darkness - which is where its real value lies,
        // since nearly every wind move is already at 100 accuracy.
        bool galeGuaranteed = Weather == WeatherType.Gale && move.WeaponTag == WeaponTag.Wind;

        // ゆき: "氷技の命中率+10%" - keyed on the MOVE's element (effective,
        // so おしえ composes), applied multiplicatively alongside the rank
        // ladder rather than as flat percentage points.
        // すなあらし: "地属性以外のパルの技の命中率-15%" - keyed on the
        // ATTACKER's own types instead. The two are deliberately read from
        // different subjects; that is what the spec text says.
        float weatherAccMul = 1.0f;
        if (Weather == WeatherType.Snow && EffectiveMoveType(_attacker, move) == "Ice")
            weatherAccMul = 1.10f;
        else if (Weather == WeatherType.Sandstorm && !HasRealType(_attacker, "Ground"))
            weatherAccMul = 0.85f;

        // ボディプレス: Crush/Rend を必中にする。技自身の IsGuaranteedHit と
        // 同じ扱いなので、相手の回避ランクと暗闇も同時に無視する。
        bool bodyPress = HasTrait(_attacker, "body_press")
            && (move.WeaponTag == WeaponTag.Crush || move.WeaponTag == WeaponTag.Rend);

        bool hits = forceHit || move.IsGuaranteedHit || galeGuaranteed || bodyPress
            || GD.Randf() * 100f < move.Accuracy * _attacker.StatusEffects.GetAccuracyMultiplierWithBonus(accuracyBonus) * target.StatusEffects.GetEvasionMultiplierWithBonus(evasionBonus) * darknessMul * weatherAccMul;
        if (!hits)
        {
            MessageLogger.Log($"{_attacker.ActorName}'s {move.Name} missed {target.ActorName}!", MessageLogger.IneffectiveColor);
            return 0;
        }

        var defenderStats = target.Stats;

        // ぜつえんたい / ディープダイブ (stage 9 §1.9): full nullification of
        // an incoming element. Returns BEFORE any damage or secondary-effect
        // work, which is what makes it "ダメージ・追加効果とも0" - the caller
        // reads 0 and therefore skips rank/ailment application and death
        // handling exactly as it does for a miss.
        //
        // Uses the move's RAW Type, not EffectiveMoveType: おしえ retargets
        // what a Neutral move counts AS, and a move converted into Electric
        // by the attacker's own trait is still an Electric move arriving at
        // the defender. Both readings coincide for a natively-typed move,
        // which is every case that exists today.
        // ワイドウォード(持続): Room/Area の攻撃を受けない。ぜつえんたいと
        // 同じ「ダメージも追加効果も0」の完全無効。持続効果なので消費しない。
        if (target.Holds(Combat.BattleItemEffect.ImmuneWideRange)
            && (move.Range == MoveRange.Room || move.Range == MoveRange.Area))
        {
            MessageLogger.Log($"{target.ActorName}はワイドウォードで{move.Name}を遮った!",
                              MessageLogger.IneffectiveColor);
            return 0;
        }

        if (HasTrait(target, "zetsuentai") && move.Type == "Electric")
        {
            MessageLogger.Log($"{target.ActorName} is insulated - {move.Name} has no effect!", MessageLogger.IneffectiveColor);
            return 0;
        }

        if (HasTrait(target, "deep_dive") && move.Type == "Water")
        {
            // Nullified AND banked: the next Water move this entity uses
            // gets +25 power (see the powerFlatBuff block below).
            target.StatusEffects.ArmDeepDiveCharge();
            MessageLogger.Log($"{target.ActorName} absorbed {move.Name} and is charged up!", MessageLogger.EffectiveColor);
            return 0;
        }

        // 〇〇のおしえ (§4, stage 2-b): a Neutral-type move's EFFECTIVE
        // element is overridden to the attacker's own oshie-template
        // element, if they hold one - affects type-effectiveness/STAB/
        // ElementPower/ちから/式/accumulation uniformly, anywhere the
        // move's Type would otherwise be consulted for element-resolution.
        // A non-Neutral move (the overwhelming majority) is untouched.
        string effectiveType = EffectiveMoveType(_attacker, move);

        // Soaked (ずぶ濡れ) overrides an entity's COMBAT-relevant Types to
        // single Water - scoped narrowly to type-effectiveness (here) and
        // STAB (below) only, per status-redesign §4-2; GetMovementProfile
        // and everything else keeps reading the real Type1/Type2 (out of
        // scope - see CombatTypes).
        var (defType1, defType2) = CombatTypes(target);
        float typeMultiplier = TypeChartManager.GetMultiplier(effectiveType, defType1, defType2);

        // 燃えるこぶし (stage 9 §1.5): the holder's CONTACT moves additionally
        // carry Fire, so the defender's Fire effectiveness is multiplied in
        // on top of the move's own. This is the same product TypeChartManager
        // already forms for a dual-typed DEFENDER, just applied from the
        // attacking side - hence the spec's "防御側複属性乗算の反転利用".
        //
        // A move that is ALREADY Fire would be multiplying its own element in
        // twice, so that case takes a flat +5 power instead (see powerFlatBuff
        // below). Placed here, before グロリアスミスト/式, so the composed
        // value is what those two subsequently inspect.
        if (HasTrait(_attacker, "moeru_kobushi") && move.IsContact && effectiveType != "Fire")
            typeMultiplier *= TypeChartManager.GetMultiplier("Fire", defType1, defType2);

        // STAB (same-type attack bonus): x1.2 when the move's (effective)
        // Type matches either of the attacker's own (possibly Soaked-
        // overridden) Types. A move is always single-typed and an attacker
        // has at most 2 Types, so this is a strict either/or - "both Types
        // match" can't structurally occur (multitype_stab_proposal §7-1),
        // no double-counting to guard.
        var (atkType1, atkType2) = CombatTypes(_attacker);
        bool stabApplies = effectiveType == atkType1 || (!string.IsNullOrEmpty(atkType2) && effectiveType == atkType2);
        // 〇〇派 (§3): when STAB already applies, a matching stab-template
        // trait replaces the usual 1.2x with 1.5x ("差し替え" - it's not an
        // additional stack, STAB just becomes stronger for this holder).
        float stabMultiplier = stabApplies
            ? (HasMatchingTemplateTrait(_attacker, effectiveType, TraitTemplateKind.Stab) ? 1.5f : 1.2f)
            : 1.0f;

        // グロリアスミスト (§4, stage 3): a Water move against a Soaked
        // target is FORCED to weakness (2.0x). Soaked already rewrote the
        // defender's combat Types to single Water above, and the real chart
        // has Water vs Water = 1.0 (verified) - which is exactly the
        // "通常は水vs水で中立になるところを上書き" this overrides.
        //
        // Deliberately placed BEFORE the 式 check below so the two compose:
        // 式 looks for "exactly 2.0", finds this forced 2.0, and upgrades it
        // to 2.5. A party running both traits is rewarded, per the confirmed
        // stacking decision.
        if (HasTrait(_attacker, "glorious_mist") && effectiveType == "Water" && target.StatusEffects.IsSoaked)
            typeMultiplier = 2.0f;

        // 〇〇式 (§4, party census): when this hit is exactly a single
        // weakness (2.0x - the doc's literal "2.0→2.5", not a general
        // "+25% to any weakness" rule, so a double-weakness 4.0 is left
        // untouched), a party-wide weakness-template trait matching the
        // move's own element upgrades it to 2.5x. Self-inclusive (no "他"
        // qualifier in the source text, unlike ちから below).
        //
        // The exact "== 2.0f" below relies on Data/type_chart.json's
        // value set being {0.5, 1.0, 2.0} ONLY - all exact powers of two,
        // so TypeChartManager.GetMultiplier's (at most 2-way) product is
        // always bit-exact, empirically verified against the real chart
        // (729 combos, 0 drift). If the chart ever gains a non-power-of-
        // two tier (e.g. 1.5x/0.75x), switch this to an epsilon compare
        // (Mathf.Abs(typeMultiplier - 2.0f) < 1e-6f) - it would no longer
        // be safe as a strict equality.
        if (typeMultiplier == 2.0f && Enum.TryParse<Element>(effectiveType, out var weaknessElement)
            && PartyElementCensus.AnyAllyHasTemplateTrait(_attacker, _floorController?.AllActors(), TraitTemplateKind.Weakness, weaknessElement, includeSelf: true))
            typeMultiplier = 2.5f;

        // MudCaked (泥まみれ) OR the target holding きょうじんなからだ (§3,
        // reuses MudCaked's block-list wholesale) neuters the move's OWN
        // CritRankBonus/DragonMultiplier for this strike - the attacker's
        // own CritRank and the base formula are untouched, only the
        // move-level kickers are blocked.
        bool blocked = BlocksSecondaryEffectsFor(target);
        // えいそう (stage 9 §1): +1 crit rank on the holder's CONTACT moves.
        // Added to the move's own bonus, then subject to the same MudCaked/
        // きょうじんなからだ neutering - it rides the move-level kicker slot.
        int eisouBonus = (HasTrait(_attacker, "eisou") && move.IsContact) ? 1 : 0;
        int effectiveCritRankBonus = blocked ? 0 : move.CritRankBonus + eisouBonus;
        float effectiveDragonMultiplier = blocked ? 1.0f : move.DragonMultiplier;

        // Crit rolled per target (§4-3), with the move's CritRankBonus.
        // たかねのはな (stage 9 §1.5): the DEFENDER holding it can never be
        // crit - a hard suppression on the roll's RESULT rather than a
        // chance reduction, so even a guaranteed-crit CritRank +5 attacker
        // is denied. Applied here so every downstream consumer (the damage
        // multiplier, the crit clamps, きょうしんぞう below, the log line)
        // uniformly sees "no crit happened".
        // GuaranteedCrit (フラッシュ系) forces the result rather than the
        // odds, so it also survives a 0% crit chance. たかねのはな still
        // wins below - "特性や道具で急所に当たらないと規定されていない限り".
        bool isCrit = move.GuaranteedCrit
            || GD.Randf() < _attacker.StatusEffects.GetCritChanceWithBonus(effectiveCritRankBonus);
        if (HasTrait(target, "takane_no_hana")) isCrit = false;

        // きょうしんぞう (stage 9 §1.5): taking a crit maxes the VICTIM's own
        // Atk rank. Fires on the crit landing, before damage resolves, so
        // the buff is already standing when they next swing (it does not
        // retroactively boost the hit they are currently taking - that is
        // the attacker's damage, not theirs).
        if (isCrit && HasTrait(target, "kyoushinzou"))
            target.StatusEffects.ApplyRankDelta(RankStat.Atk, AtkRankMaxDelta);

        float atkMul = _attacker.StatusEffects.GetAtkMultiplier();
        float defMul = target.StatusEffects.GetDefMultiplier();
        float powerMul = _attacker.StatusEffects.GetElementPowerMultiplier(effectiveType);

        // ---- Weather, damage half ----
        // はれ/あめ: the classic Fire/Water see-saw. Keyed on effectiveType
        // so おしえ composes the same way it does for ちから/式/ElementPower,
        // and folded into powerMul so it lands in the pipeline's Step 1
        // (effPower) rather than as another Step-4 multiplier.
        switch (Weather)
        {
            case WeatherType.Sunny when effectiveType == "Fire": powerMul *= 1.5f; break;
            case WeatherType.Sunny when effectiveType == "Water": powerMul *= 0.75f; break;
            case WeatherType.Rain when effectiveType == "Water": powerMul *= 1.5f; break;
            case WeatherType.Rain when effectiveType == "Fire": powerMul *= 0.75f; break;
        }

        // きり: "地属性のパルの防御力-10%". Confirmed as a multiplier on the
        // COMPUTED Defense (the ハードブロック/ハードアーマー x2 shape), not
        // the species-value flat form the trait catalogue uses - so it rides
        // defMul alongside those, not defFlatBuff.
        if (Weather == WeatherType.Fog && HasRealType(target, "Ground"))
            defMul *= 0.9f;

        // いっせん／ツメのかりうど (§4, stage 2-b): this move's own
        // WeaponTag matching the attacker's held trait grants +10% power.
        // Self-based (not party census) - a direct trait+move-data check.
        // いっせん は Slash と Thrust、ツメのかりうど は Fist と Punch を見る
        // （系統整理でタグが増えたのに合わせて対象を広げた）。
        if ((HasTrait(_attacker, "issen")
             && (move.WeaponTag == WeaponTag.Slash || move.WeaponTag == WeaponTag.Thrust))
            || (HasTrait(_attacker, "tsume_no_kariudo")
                && (move.WeaponTag == WeaponTag.Fist || move.WeaponTag == WeaponTag.Punch)))
            powerMul *= 1.1f;

        // ポーカーフェイス: Straight/Flash の威力1.5倍。いっせん／ツメのかりうど
        // と同じ「自分の技のタグを見る」形なので同じ powerMul に乗せる。
        // 平坦加算(+10)から差し替えたので、威力の高い技ほど伸びる。
        if (HasTrait(_attacker, "poker_face")
            && (move.WeaponTag == WeaponTag.Straight || move.WeaponTag == WeaponTag.Flash))
            powerMul *= 1.5f;

        // がんばりサポート (§4, stage 3): a party-census trait that buffs
        // OTHERS, not its holder - "他の味方が使う接触技の威力+10%", so this
        // is the ちから shape (includeSelf: false) keyed on a bare unique
        // trait id instead of an element-matched template. First real
        // consumer of AnyAllyHasUniqueTrait, the hook split out in stage 2-a.
        if (move.IsContact
            && PartyElementCensus.AnyAllyHasUniqueTrait(_attacker, _floorController?.AllActors(), "ganbari_support", includeSelf: false))
            powerMul *= 1.1f;

        // 〇〇のきずな (§4, party census): the attacker's OWN bond-template
        // trait (if any) scales their own Attack by 8% per matching-
        // element ally, capped at 3 bodies (24%) - not move.Type-dependent
        // at all, a standing buff active on every attack. Folds into the
        // same "individual passive / party skill" slot AtkMultiplier was
        // already documented for.
        // Reworked in stage 9 §1.7 from a MULTIPLIER on the attacker's
        // computed Attack to a FLAT addend derived from the SPECIES value
        // (Stats.BaseAtk), so the bonus no longer scales with the holder's
        // level - two members of the same species contribute the same
        // +N at Lv5 and Lv95. DamageContext.AtkFlatBuff/DefFlatBuff are
        // the pre-existing seam for exactly this (Step 1 of the pipeline
        // adds them before any multiplier runs).
        float atkFlatBuff = 0f;
        float defFlatBuff = 0f;

        var bondTrait = TraitDatabase.Get(_attacker.Stats.Trait);
        if (bondTrait != null && bondTrait.Category == TraitCategory.Template
            && bondTrait.TemplateKind == TraitTemplateKind.Bond && bondTrait.Element.HasValue)
        {
            int allyCount = Mathf.Min(3, PartyElementCensus.CountAlliesWithType(_attacker, _floorController?.AllActors(), bondTrait.Element.Value));
            // Floor(BaseAtk * 0.08 * allies) - one floor over the whole
            // product, not per ally, per the spec's literal formula.
            atkFlatBuff += Mathf.FloorToInt(_attacker.Stats.BaseAtk * 0.08f * allyCount);
        }

        // 〇〇のちから (§4, party census): if some OTHER party member holds
        // a power-template trait matching this move's own element, +10%
        // power. Existence-only check ("重複不可" - multiple holders still
        // only grant +10% once, no extra dedup needed).
        if (Enum.TryParse<Element>(effectiveType, out var powerElement)
            && PartyElementCensus.AnyAllyHasTemplateTrait(_attacker, _floorController?.AllActors(), TraitTemplateKind.Power, powerElement, includeSelf: false))
            powerMul *= 1.1f;

        // 〇〇のまもり (§4, party census): if a guard-template trait matching
        // either of the DEFENDER's own Types exists anywhere in their
        // party (holder included - "全員" covers the holder too, unlike
        // ちから's "他パルの"), +10% defense.
        // Also reworked in stage 9 §1.7: Floor(BaseDef * 0.10) as a flat
        // addend instead of the old x1.1 on computed Defense. Still
        // existence-only, so "重複不可" holds unchanged.
        if (HasPartyGuard(target)) defFlatBuff += Mathf.FloorToInt(target.Stats.BaseDef * 0.10f);

        // ---- stage 9 §1.5/§1.9: further DamageContext-input traits ----

        // けいかいかん: +10 Defense against a RANGED move. "遠距離" is read as
        // !IsContact (the spec's own stated interpretation) - no separate
        // melee/ranged flag is introduced. A plain +10, not a species-value
        // percentage, so it lands on the same flat seam as まもり and is
        // likewise level-invariant.
        if (HasTrait(target, "keikaikan") && !move.IsContact) defFlatBuff += 10;

        // ハードブロック / ハードアーマー: the defender doubles their own
        // Defense against one damage category. These stay MULTIPLIERS on the
        // computed stat (the x2 family is explicitly exempted from §1.7's
        // species-value rework), so they ride defMul, not defFlatBuff.
        if ((HasTrait(target, "hard_block") && move.Category == MoveCategory.Special)
            || (HasTrait(target, "hard_armor") && move.Category == MoveCategory.Physical))
            defMul *= 2.0f;

        // Power addends (威力+N) - these add to the move's Power BEFORE
        // PowerMultiplier, which is what DamageContext.PowerFlatBuff is for.
        // Additive with each other, unlike the x1.1 power traits above.
        float powerFlatBuff = 0f;

        // こうふん: +15 against an ADJACENT target. Chebyshev distance 1, the
        // same reach rule melee/CanAttackAdjacent already uses.
        if (HasTrait(_attacker, "koufun"))
        {
            var reach = (_attacker.GridPosition - target.GridPosition).Abs();
            if (Mathf.Max(reach.X, reach.Y) <= 1) powerFlatBuff += 15f;
        }

        // フリーフォール: +25 against a target whose ECOLOGY is グライド -
        // the first trait to key off the ecology slot rather than a trait.
        if (HasTrait(_attacker, "free_fall") && target.Stats.Ecology == "glide")
            powerFlatBuff += 25f;

        // パープルヘイズ: while the weather is きり, the holder's own Dark
        // moves of power 55 or less gain +15. Read off move.Power (the
        // authored base power), not the running effPower, so the "威力55以下"
        // gate can't be flipped by another buff resolving first. Uses
        // effectiveType so an おしえ-retyped Neutral move counts as Dark the
        // same way it does everywhere else.
        if (Weather == WeatherType.Fog && HasTrait(_attacker, "purple_haze")
            && effectiveType == "Dark" && move.Power <= PurpleHazePowerCap)
            powerFlatBuff += PurpleHazePowerBonus;

        // 発煙器官: +10 on ブレス/息系 moves (WeaponTag.Breath, populated in
        // moves.json for the 5 qualifying moves).
        if (HasTrait(_attacker, "hatsuen_kikan") && move.WeaponTag == WeaponTag.Breath)
            powerFlatBuff += 10f;

        // 燃えるこぶし's already-Fire branch: no second Fire multiplication
        // (that would square the holder's own element), a flat +5 instead.
        if (HasTrait(_attacker, "moeru_kobushi") && move.IsContact && effectiveType == "Fire")
            powerFlatBuff += 5f;

        // ディープダイブ: consumes the one-shot charge armed by nullifying a
        // Water move (see the nullification block earlier in this method).
        if (move.Type == "Water" && _attacker.StatusEffects.ConsumeDeepDiveChargeIfArmed())
            powerFlatBuff += 25f;

        // みずたまり (trap-move kit): a Pal standing in a puddle takes 1.25x
        // from Electric, and its OWN Fire moves lose 25% power. Two
        // independent halves - the defender's tile drives the first, the
        // attacker's tile the second - so a puddle can be helping and
        // hurting different actors in the same strike.
        if (_floorController != null)
        {
            if (effectiveType == "Electric"
                && _floorController.Fields.Get(target.GridPosition) == Dungeon.FieldType.Puddle)
                typeMultiplier *= 1.25f;

            if (effectiveType == "Fire"
                && _floorController.Fields.Get(_attacker.GridPosition) == Dungeon.FieldType.Puddle)
                powerMul *= 0.75f;
        }

        // ---- stage 9 §1: the 12 transcribed catalogue traits ----

        // ふんどのつばさ: +5 power per OTHER Dragon-or-Fire party member.
        // Uncapped (unlike じゆうのつばさ's +2 rank ceiling) - the spec gives
        // no limit, and the 33% recoil in ApplyRecoil is the counterweight.
        if (HasTrait(_attacker, "fundo_no_tsubasa"))
            powerFlatBuff += 5f * PartyElementCensus.CountAlliesWithEitherType(
                _attacker, _floorController?.AllActors(), Element.Dragon, Element.Fire);

        // おこりんぼ: +10% damage on contact moves - the same 与ダメージ+10%
        // shape as いっせん, so it rides powerMul for consistency.
        if (HasTrait(_attacker, "okorinbo") && move.IsContact) powerMul *= 1.1f;

        // ガーディアンモード: below half HP, Atk AND Def double. Explicitly
        // exempt from §1.7's species-value rework (it is a x2 multiplier),
        // so it stays on the computed stat. Checked per side: the holder
        // gets the Atk half when attacking, the Def half when defending.
        if (HasTrait(_attacker, "guardian_mode") && IsBelowHalfHp(_attacker)) atkMul *= 2.0f;
        if (HasTrait(target, "guardian_mode") && IsBelowHalfHp(target)) defMul *= 2.0f;

        // こんとんのしゅくふく: a party-wide x1.2 Atk aura. A multiplier, not
        // a constant, so §1.7's species-value rule does not apply. Existence
        // check including self - the holder benefits from their own aura,
        // and pays for it with the 15% HP drain in ResolveStatusTick.
        if (PartyElementCensus.AnyAllyHasUniqueTrait(_attacker, _floorController?.AllActors(), "konton_no_shukufuku", includeSelf: true))
            atkMul *= 1.2f;

        // リーダー系4種: scale with how many むれのいちいん holders are in the
        // party. The stat halves are species-value flat addends per §1.7
        // ("％の能力値部分のみ種族値基準"); the move-power halves stay as
        // multipliers, since those are not 能力値 at all.
        int attackerFollowers = CountFollowers(_attacker);
        if (attackerFollowers > 0)
        {
            // もふもふ: Atk +10%/follower (cap 30%). とうそつ: Atk +15% (cap 45%).
            if (HasTrait(_attacker, "mofumofu_leader"))
                atkFlatBuff += SpeciesPercent(_attacker.Stats.BaseAtk, 0.10f, attackerFollowers, 0.30f);
            if (HasTrait(_attacker, "tousotsu_leader"))
                atkFlatBuff += SpeciesPercent(_attacker.Stats.BaseAtk, 0.15f, attackerFollowers, 0.45f);

            // 冷却: Ice moves +5%/follower (cap 15%). 力持ち: Neutral moves ditto.
            if (HasTrait(_attacker, "reikyaku_leader") && effectiveType == "Ice")
                powerMul *= 1f + Mathf.Min(0.15f, 0.05f * attackerFollowers);
            if (HasTrait(_attacker, "chikaramochi_leader") && effectiveType == "Neutral")
                powerMul *= 1f + Mathf.Min(0.15f, 0.05f * attackerFollowers);
        }

        int targetFollowers = CountFollowers(target);
        if (targetFollowers > 0)
        {
            // 冷却/力持ち/もふもふ all give Def +10%/follower (cap 30%).
            if (HasTrait(target, "reikyaku_leader") || HasTrait(target, "chikaramochi_leader") || HasTrait(target, "mofumofu_leader"))
                defFlatBuff += SpeciesPercent(target.Stats.BaseDef, 0.10f, targetFollowers, 0.30f);
        }

        if (isCrit)
        {
            atkMul = Mathf.Max(1f, atkMul);
            defMul = Mathf.Min(1f, defMul);
            powerMul = Mathf.Max(1f, powerMul);

            // Same "a crit ignores corrections that disadvantage the
            // attacker" rule the three clamps above apply, extended to the
            // new flat buffs so the rework doesn't quietly change crit
            // behaviour: まもり used to ride defMul and was therefore
            // already cancelled by the Min(1f) clamp, so it must still be
            // cancelled now that it rides defFlatBuff instead. きずな's
            // clamp is a no-op (it only ever adds), kept for symmetry.
            atkFlatBuff = Mathf.Max(0f, atkFlatBuff);
            defFlatBuff = Mathf.Min(0f, defFlatBuff);

            // Power addends are the ATTACKER's own advantage, so they
            // survive a crit for the same reason powerMul's Max(1f) does.
            powerFlatBuff = Mathf.Max(0f, powerFlatBuff);
        }

        // 持ち物4種は実数値ではなく実効威力を動かす。ダメージは実効威力に
        // 対して線形（rawDamage = baseCalc * effPower/100）なので、表記の
        // ±25/30/40% がそのままダメージの増減になる。実数値(Atk/Def)側に
        // 掛けると Atk^2/(Atk+Def) の非線形を通るため、攻撃側は表記より
        // 強く、防御側は表記より弱く出てしまう。
        //
        // 急所のクランプ(powerMul の Max(1f))より後に掛けているので、
        // プレートの軽減は急所でも打ち消されない。〇〇流など特性による
        // 軽減がダメージ確定後に効くのと同じ扱い。
        if (_attacker.Holds(Combat.BattleItemEffect.PhysAtkUp25) && move.Category == MoveCategory.Physical)
            powerMul *= 1.25f;
        if (_attacker.Holds(Combat.BattleItemEffect.SpecAtkUp25) && move.Category == MoveCategory.Special)
            powerMul *= 1.25f;
        if (target.Holds(Combat.BattleItemEffect.PhysDefUp30) && move.Category == MoveCategory.Physical)
            powerMul *= 0.70f;
        if (target.Holds(Combat.BattleItemEffect.SpecDefUp40) && move.Category == MoveCategory.Special)
            powerMul *= 0.60f;

        var ctx = new DamageContext
        {
            BaseAtk = _attacker.Stats.Attack,
            BaseDef = defenderStats.Defense,
            BasePower = move.Power,
            AtkFlatBuff = atkFlatBuff,
            DefFlatBuff = defFlatBuff,
            PowerFlatBuff = powerFlatBuff,
            AttackElement = effectiveType,
            DefenderElement = defType1,
            TypeEffectiveness = typeMultiplier,
            StabMultiplier = stabMultiplier,
            AtkMultiplier = atkMul,
            DefMultiplier = defMul,
            PowerMultiplier = powerMul,
            // 急所は最後段で掛ける。処理順は
            // 通常ダメージ → 特性 → 持ち物 → 急所 と決めたので、
            // 計算器の内部では等倍に固定しておく。
            CritMultiplier = 1.0f,
            DragonMultiplier = effectiveDragonMultiplier,
        };

        int damage = DamageCalculator.Calculate(ctx);

        // Burn's contact-damage penalty (x0.5 output halving), outside
        // DamageCalculator - a damage-output penalty, not a stat modifier.
        if (_attacker.StatusEffects.Ailment == AilmentType.Burn && move.IsContact)
            damage = Mathf.Max(1, Mathf.FloorToInt(damage * 0.5f));

        // 〇〇流 (§3): the DEFENDER holding a resist-template trait matching
        // the incoming move's own element takes only 15% damage (85%
        // reduction) - keyed on the trait's declared element, independent
        // of the holder's real Type1/Type2.
        if (HasMatchingTemplateTrait(target, effectiveType, TraitTemplateKind.Resist))
            damage = Mathf.Max(1, Mathf.FloorToInt(damage * 0.15f));

        // きぬぬい (§3): one-time -10% on the next damage this entity takes,
        // armed by their own prior Ice-move use (see Execute()).
        if (target.StatusEffects.ConsumeDamageReductionIfArmed())
            damage = Mathf.Max(1, Mathf.FloorToInt(damage * 0.9f));

        // わたほうし (§4, stage 2-c): the DEFENDER holding this trait
        // checks the ATTACKER's own ailment (VineBound) - the reverse
        // reference direction from every other defensive trait, which
        // normally checks only its own holder's state ("通常と逆方向の参照").
        // 50% reduction, per the confirmed magnitude.
        if (HasTrait(target, "watahoushi") && _attacker.StatusEffects.IsVineBound)
            damage = Mathf.Max(1, Mathf.FloorToInt(damage * 0.5f));

        // むれのいちいん (stage 9 §1): -15% while a リーダー holder is in the
        // party AND this entity is at FULL HP - a "first hit only" shield in
        // practice, since the first hit that lands breaks the full-HP
        // condition for every hit after it.
        if (HasTrait(target, "mure_no_ichiin")
            && defenderStats.CurrentHp >= defenderStats.MaxHp
            && HasAnyLeaderAlly(target))
            damage = Mathf.Max(1, Mathf.FloorToInt(damage * 0.85f));

        // ビルドアップ (stage 9 §1): -25% for the duration of the recipient's
        // one action. Recoil is explicitly NOT reduced, which is automatic
        // here: ApplyRecoil computes from the damage DEALT and never routes
        // through this defender-side path.
        if (target.StatusEffects.HasBuildUpShield)
            damage = Mathf.Max(1, Mathf.FloorToInt(damage * 0.75f));

        // ばくせん (§4, stage 3): a Fire hit on an already-Burning target
        // adds a FLAT target-Level*0.8, in "相性倍率の影響を受けない別枠" -
        // hence its position here, after every multiplicative modifier
        // (type/STAB/crit/流/きぬぬい/わたほうし) has already resolved, so
        // nothing scales it. Additive, so no second-stage floor concern:
        // the addend is floored once on its own.
        //
        // Not gated by MudCaked/きょうじんなからだ - like いっせん/ツメのかり
        // うど, this is the attacker's own damage-output trait, not a
        // secondary effect being done TO the target (that block-list covers
        // the move's own kickers and status riders, see BlocksSecondaryEffectsFor).
        if (HasTrait(_attacker, "bakusen") && effectiveType == "Fire" && target.StatusEffects.Ailment == AilmentType.Burn)
            damage += Mathf.FloorToInt(target.Stats.Level * 0.8f);

        // あくむのひとみ (§4, stage 2-c): drains 50% of the damage dealt,
        // but ONLY when the exact named move (ナイトメアボール/パルス) is
        // used - a direct move-id reference, independent of the move's
        // own (currently unset) DrainHpPercent field. MudCaked/きょうじん
        // なからだ still block it (same reasoning as DrainHalf).
        if (HasTrait(_attacker, "akumu_no_hitomi") && (move.Id == "nightmare_ball" || move.Id == "MV_131")
            && !BlocksSecondaryEffectsFor(target) && _attacker.Stats.IsAlive)
        {
            int akumuHeal = Mathf.Max(1, Mathf.FloorToInt(damage * 0.5f));
            _attacker.Stats.Heal(akumuHeal);
            MessageLogger.Log($"{_attacker.ActorName} drained {akumuHeal} HP with {move.Name}!", MessageLogger.ProgressionColor);
        }

        // 攻守一体 (§4, stage 2-c): triggers on the DEFENDER being hit -
        // arms a 2-turn Atk+1/Def+1 buff on THEMSELVES, independent of
        // the normal 10-turn-decay rank system.
        if (HasTrait(target, "koushu_ittai"))
            target.StatusEffects.ArmTemporaryRankBuff(atkBonus: 1, defBonus: 1, turns: 2);

        // 瞬間冷凍 (§4, stage 2-c): the ATTACKER's own trait, landing a hit
        // on a Soaked target bypasses the accumulation system entirely and
        // inflicts Freeze immediately. Same MudCaked/きょうじんなからだ
        // block as every other secondary effect. TryApplyAilment alone
        // would silently no-op here - the Ailment slot is already occupied
        // by Soaked (both share the same 9-way mutually-exclusive slot),
        // so Soaked must be cleared first to free the slot for Freeze.
        if (HasTrait(_attacker, "shunkan_reitou") && target.StatusEffects.IsSoaked && !BlocksSecondaryEffectsFor(target))
        {
            target.StatusEffects.ClearAilmentIfType(AilmentType.Soaked);
            target.StatusEffects.TryApplyAilment(AilmentType.Freeze);
        }

        // ざんきょうのしゅごしゃ (§4, stage 3): a same-faction guardian within
        // 2 tiles of the victim shoulders 15% of this hit - the victim's
        // damage drops by that amount and the guardian takes it as REAL
        // damage (the confirmed "肩代わり" reading, not a free negation).
        // Resolved last, immediately before the victim is dealt damage, so
        // it splits the FINAL number - everything above (multipliers, ばく
        // せん's flat add) is already baked in, and the attacker's own
        // drain/recoil still key off the full damage they dealt.
        damage = ApplyGuardianShoulder(target, damage);

        // ---- 持ち物の段 (特性の後、急所の前) ----
        // クリットシェル(使い切り): 急所を受けた時、そのダメージを90%減。
        // 急所倍率が乗る前の値に掛かるが、どちらも乗算なので結果は変わらない
        // （切り捨ての位置だけが変わる）。
        if (isCrit && target.Holds(Combat.BattleItemEffect.CritCut90))
        {
            damage = Mathf.Max(1, Mathf.FloorToInt(damage * 0.1f));
            target.ConsumeHeldItem();
        }

        // ウィークシェル(使い切り): 弱点属性を受けた時、そのダメージを75%減。
        // 「弱点」は解決後の相性倍率が等倍超であることで判定する。
        if (typeMultiplier > 1.0f && target.Holds(Combat.BattleItemEffect.WeaknessCut75))
        {
            damage = Mathf.Max(1, Mathf.FloorToInt(damage * 0.25f));
            target.ConsumeHeldItem();
        }

        // ---- 急所の段 (最後) ----
        // 通常ダメージ → 特性 → 持ち物 → 急所 の順で処理する取り決めにより、
        // 急所はすべての軽減・増幅を通したあとの値に掛かる。
        if (isCrit)
            damage = Mathf.Max(1, Mathf.FloorToInt(damage * 1.5f));

        // エンデュアチャーム(使い切り): HP満タンから即死する一撃をHP1で耐える。
        // 「即死に至る威力」の判定なので、急所まで乗せきった最終値で見る。
        if (damage >= defenderStats.CurrentHp
            && defenderStats.CurrentHp == defenderStats.MaxHp
            && target.Holds(Combat.BattleItemEffect.SurviveFromFull))
        {
            damage = defenderStats.CurrentHp - 1;
            target.ConsumeHeldItem();
        }

        defenderStats.TakeDamage(damage);

        // ガードトニック/ラストトニック(使い切り): 被弾でHPが閾値を割ったら回復。
        // 1匹1つなので両方は持てず、競合しない。
        TryHpThresholdItem(target);

        // ルームミラー(使い切り): 相手のRoom射程を受けた時、受けたダメージを
        // 使用者へそのまま返す。自陣のRoom技は元々味方に当たらないので、
        // 「相手が使用した技」という条件は射程の性質から自動的に満たされる。
        if (damage > 0 && move.Range == MoveRange.Room
            && target.Faction != _attacker.Faction
            && target.Holds(Combat.BattleItemEffect.CounterRoom))
        {
            target.ConsumeHeldItem();
            _attacker.Stats.TakeDamage(damage);
            MessageLogger.Log($"{target.ActorName}のルームミラー! {_attacker.ActorName}に{damage}ダメージを返した!",
                              MessageLogger.EffectiveColor);
            if (_attacker.Stats.CurrentHp <= 0) _attacker.Die();
        }
        _attacker.StatusEffects.ResetDamageTimer();
        target.StatusEffects.ResetDamageTimer();

        // ---- stage 9 §1.5/§1.9: post-damage reactions on the DEFENDER ----

        // あくい: a Dark move still connects in full, then gives back half
        // of what it dealt ("本来受けるダメージの数値の半分を回復") - a net
        // halving expressed as damage-then-heal, not as a reduction, so the
        // attacker's own drain/recoil still see the undiminished number.
        if (HasTrait(target, "akui") && move.Type == "Dark" && damage > 0)
        {
            int akuiHeal = Mathf.FloorToInt(damage * 0.5f);
            if (akuiHeal > 0)
            {
                defenderStats.Heal(akuiHeal);
                MessageLogger.Log($"{target.ActorName} feeds on the darkness and recovers {akuiHeal} HP!", MessageLogger.ProgressionColor);
            }
        }

        // ニードルアーマー: a CONTACT move fires spikes back at the attacker
        // for 5% of the ATTACKER's own MaxHP, "ダメージ処理後" - i.e. after
        // TakeDamage above, so a defender who dies to the hit still retaliates.
        if (HasTrait(target, "needle_armor") && move.IsContact && !BlocksSecondaryEffectsFor(target))
        {
            int spikes = Mathf.Max(1, Mathf.FloorToInt(_attacker.Stats.MaxHp * 0.05f));
            _attacker.Stats.TakeDamage(spikes);
            _attacker.PlayHitFlash();
            _attacker.ShowDamagePopup(spikes);
            MessageLogger.Log($"{_attacker.ActorName} is hurt by {target.ActorName}'s spikes! ({spikes} damage)", MessageLogger.IneffectiveColor);
            if (!_attacker.Stats.IsAlive) HandleFaint(_attacker);
        }

        // サンドバッグ: being hit by an OPPONENT's contact move turns the
        // weather to すなあらし. Same slot as ニードルアーマー above (a landed
        // contact hit, after damage), and likewise fires even if the holder
        // died to it - "受けると" is about taking the hit, not surviving it.
        //
        // Faction-gated because the effect says 相手から: a stray friendly-fire
        // contact hit from a party member must not set it off. Skipped when
        // the weather is already すなあらし ("すでにその天候になっている場合、
        // 天候を変化する部分に関しては発動しない").
        if (HasTrait(target, "sandbag") && move.IsContact
            && _attacker.Faction != target.Faction
            && _floorController != null
            && _floorController.Weather.Current != WeatherType.Sandstorm)
        {
            _floorController.Weather.SetBaseline(WeatherType.Sandstorm);
            MessageLogger.Log($"{target.ActorName} kicks up a sandstorm!", MessageLogger.ProgressionColor);
        }

        // どくせんボディ: poison accumulation in BOTH directions, entirely
        // trait-driven (AccumulateFlat bypasses the move's own element/
        // AilmentChance matching, same as レッツハギング/ひょうてんま).
        // - holder is hit by a Physical move  -> attacker gains +250
        // - holder LANDS a Physical move      -> target gains +75
        if (move.Category == MoveCategory.Physical)
        {
            if (HasTrait(target, "dokusen_body") && !_attacker.StatusEffects.IsMudCaked)
                _attacker.StatusEffects.AccumulateFlat(AilmentType.Poison, 250);

            if (HasTrait(_attacker, "dokusen_body") && !BlocksSecondaryEffectsFor(target))
                target.StatusEffects.AccumulateFlat(AilmentType.Poison, 75);
        }

        // Darkness clears on a landed Special-category hit, from any
        // source (status-redesign §4-6 "発生源は問わない").
        if (move.Category == MoveCategory.Special)
            target.StatusEffects.ClearAilmentIfType(AilmentType.Darkness);

        target.PlayHitFlash();
        target.ShowDamagePopup(damage);
        MessageLogger.Log($"{_attacker.ActorName} used {move.Name} on {target.ActorName}! It hit for {damage} damage.");

        if (isCrit)
            MessageLogger.Log("A critical hit!", MessageLogger.EffectiveColor);
        if (typeMultiplier > 1f)
            MessageLogger.Log("It's super effective!", MessageLogger.EffectiveColor);
        else if (typeMultiplier < 1f)
            MessageLogger.Log("It's not very effective...", MessageLogger.IneffectiveColor);

        return damage;
    }

    // Soaked (ずぶ濡れ) override, scoped to combat-type resolution only
    // (TypeEffectiveness + STAB) - see StrikeTarget. GetMovementProfile
    // and every other Type1/Type2 reader is untouched (status-redesign
    // §4-2's explicit (a)/(b) scope; (c) - ally elemental buffs - has no
    // system to hook into yet and is a declared no-op for now).
    private static (string Type1, string Type2) CombatTypes(Entity entity) =>
        entity.StatusEffects.IsSoaked ? ("Water", "") : (entity.Stats.Type1, entity.Stats.Type2);

    // Weather keys off the entity's REAL typing, deliberately NOT
    // CombatTypes: ずぶ濡れ's Water override is scoped to type-effectiveness
    // and STAB only (status-redesign §4-2), so a Soaked Ground-type Pal is
    // still a Ground-type as far as すなあらし and きり are concerned.
    private static bool HasRealType(Entity entity, string element) =>
        entity.Stats.Type1 == element || entity.Stats.Type2 == element;

    // 〇〇のおしえ (§4, stage 2-b): a Neutral-type move's effective element
    // becomes `attacker`'s own oshie-template element, if they hold one -
    // everything else keeps its real Type unchanged. Called once per
    // strike (StrikeTarget) and independently wherever the ailment-
    // accumulation dispatch also needs it (ApplyAilmentEffectIfAny/
    // ApplyAoeAilment), since those are separate methods with no shared
    // local to thread it through.
    private static string EffectiveMoveType(Entity attacker, MoveData move)
    {
        if (move.Type != "Neutral") return move.Type;

        var trait = TraitDatabase.Get(attacker.Stats.Trait);
        return trait != null && trait.Category == TraitCategory.Template
            && trait.TemplateKind == TraitTemplateKind.Oshie && trait.Element.HasValue
            ? trait.Element.Value.ToString()
            : move.Type;
    }

    // ---- trait_catalog_v2 helpers ----

    private static bool HasTrait(Entity entity, string traitId) =>
        entity != null && entity.Stats.Trait == traitId;

    // Does `entity` hold a Template-category trait of `kind` whose own
    // Element matches `moveType` (e.g. an entity holding "fire_stab" and
    // moveType=="Fire")? Used by 派/流 (§3) - templates are keyed by their
    // OWN declared element, independent of the holder's real Type1/Type2.
    private static bool HasMatchingTemplateTrait(Entity entity, string moveType, TraitTemplateKind kind)
    {
        var trait = TraitDatabase.Get(entity.Stats.Trait);
        return trait != null && trait.Category == TraitCategory.Template
            && trait.TemplateKind == kind && trait.Element?.ToString() == moveType;
    }

    // きょうじんなからだ (§3): reuses MudCaked's ENTIRE block-list wholesale
    // ("泥まみれの実装を流用") whenever the (primary) defender holds it -
    // the move behaves, for every one of MudCaked's gates, as if the
    // ATTACKER were MudCaked for this one strike. _defender is this
    // class's existing "primary target/aim reference" (already used for
    // AoE's aim tile) - reused here as the one well-defined reference for
    // the attacker-self-effect gates (Recoil/Drain/SelfStun/SelfRankOnce),
    // which have no natural "which of several AoE targets" answer.
    private bool DefenderBlocksSecondaryEffects =>
        _attacker.StatusEffects.IsMudCaked || HasTrait(_defender, "kyoujin_na_karada");

    // Per-target version for gates that already have a specific target in
    // hand (StrikeTarget's crit/dragon neutering, AoE's per-target
    // ailment/enemy-rank) - strictly more precise than the _defender-based
    // check above when a single AttackAction hits several AoE targets.
    private bool BlocksSecondaryEffectsFor(Entity target) =>
        _attacker.StatusEffects.IsMudCaked || HasTrait(target, "kyoujin_na_karada");

    // きょうしんぞう raises the victim's Atk rank to its MAXIMUM in one go.
    // ApplyRankDelta clamps into [-6, +6] internally, so passing the full
    // span (+12) lands on +6 from any starting rank, including -6 - which
    // is what "最大まで上がる" means, rather than a mere +N step.
    private const int AtkRankMaxDelta = 12;

    // Resolves a multi-hit move against one target and returns the TOTAL
    // damage, so every caller (recoil, drain, faint handling) keeps working
    // off one combined number exactly as it does for a single strike.
    //
    // Accumulation: the spec is "1発ごとに状態異常値の基礎蓄積値を加算".
    // The caller already runs ApplyAilmentEffectIfAny once for the whole
    // move, which contributes the FIRST hit's baseline - so this adds one
    // baseline per EXTRA landed hit rather than one per hit, or the first
    // would be counted twice. AilmentType.None/0 is passed deliberately:
    // that path adds the flat baseline only, never the move's declared
    // chance bonus, which stays a once-per-move effect.
    private int StrikeMultiHit(MoveData move, Entity target)
    {
        int total = 0, landed = 0;

        if (move.MultiHit == MultiHitMode.Variable2To5)
        {
            // One accuracy roll for the move as a whole: if the opening hit
            // misses, the move misses outright and no count is rolled.
            int first = StrikeTarget(move, target);
            if (first <= 0) return 0;
            total = first; landed = 1;

            float r = GD.Randf();
            int hits = r < 1f / 3f ? 2 : r < 2f / 3f ? 3 : r < 5f / 6f ? 4 : 5;
            for (int i = 1; i < hits; i++)
            {
                if (!GodotObject.IsInstanceValid(target) || !target.Stats.IsAlive) break;
                int d = StrikeTarget(move, target, forceHit: true);
                if (d > 0) { total += d; landed++; }
            }
        }
        else // RepeatPerHit: accuracy is rolled again for every hit
        {
            int hits = Mathf.Max(1, move.MultiHitCount);
            for (int i = 0; i < hits; i++)
            {
                if (!GodotObject.IsInstanceValid(target) || !target.Stats.IsAlive) break;
                int d = StrikeTarget(move, target);
                if (d > 0) { total += d; landed++; }
            }
        }

        if (landed > 0)
            MessageLogger.Log($"{move.Name} hit {landed} time(s) for {total} total damage!");

        if (GodotObject.IsInstanceValid(target) && target.Stats.IsAlive)
        {
            string elem = EffectiveMoveType(_attacker, move);
            for (int i = 1; i < landed; i++)
                target.StatusEffects.AccumulateOnHit(elem, AilmentType.None, 0);
        }

        return total;
    }

    // One entry point for "hit this target with this move", so the single
    // and AoE paths both pick up multi-hit without duplicating the branch.
    private int Strike(MoveData move, Entity target) =>
        move.MultiHit == MultiHitMode.None ? StrikeTarget(move, target) : StrikeMultiHit(move, target);


    // 5x5 centred on the user = 25 tiles; "半分(端数切り捨て)" = 12.
    private const int HalfAreaTiles = 12;
    // パープルヘイズ: the power gate and the addend it grants.
    private const int PurpleHazePowerCap = 55;
    private const float PurpleHazePowerBonus = 15f;

    private const int FourEmptyTiles = 4;
    private const int GekiryuuRadius = 4;

    // Lays down (or, for げきりゅう, wipes) this move's field. Returns true
    // when the move was a field move and has now fully resolved - the caller
    // returns immediately, since these do no damage and have no target.
    private bool ApplyFieldEffect(MoveData move)
    {
        if (move.FieldPlacement == Dungeon.FieldPlacement.None) return false;
        if (_floorController == null) return false; // no floor (tests/Hub): inert

        var origin = _attacker.GridPosition;

        if (move.FieldPlacement == Dungeon.FieldPlacement.ClearRadiusFour)
        {
            int cleared = _floorController.ClearFieldsAround(origin, GekiryuuRadius);
            MessageLogger.Log($"{_attacker.ActorName} used {move.Name}! {cleared} field(s) were swept away.");
            return true;
        }

        int want = move.FieldPlacement == Dungeon.FieldPlacement.FourEmptyTiles ? FourEmptyTiles : HalfAreaTiles;
        int placed = _floorController.PlaceField(origin, move.FieldEffect, want);
        MessageLogger.Log($"{_attacker.ActorName} used {move.Name}! ({placed} tile(s) affected)");
        return true;
    }

    // Sets the floor's weather for WeatherTurns turns. Returns true when the
    // move was a weather move and has now fully resolved. Without a floor
    // (tests/Hub) it is inert but still consumes the move, matching
    // ApplyFieldEffect - a weather move must not silently fall through to
    // the damage path, since it has no power to deal.
    private bool ApplyWeatherEffect(MoveData move)
    {
        if (move.WeatherEffect == Dungeon.WeatherType.None) return false;

        if (_floorController == null)
        {
            MessageLogger.Log($"{_attacker.ActorName} used {move.Name}, but nothing happened.", MessageLogger.IneffectiveColor);
            return true;
        }

        _floorController.Weather.Set(move.WeatherEffect, move.WeatherTurns);
        MessageLogger.Log(
            $"{_attacker.ActorName} used {move.Name}! The weather is now {Dungeon.WeatherTypeNames.Japanese(move.WeatherEffect)}.",
            MessageLogger.ProgressionColor);
        return true;
    }

    private static bool IsBelowHalfHp(Entity e) => e.Stats.CurrentHp * 2 < e.Stats.MaxHp;

    // A リーダー trait's scaling term: `rate` per follower, capped at `cap`,
    // taken against the SPECIES value and floored once - §1.7's rule for the
    // "％の能力値部分". Level-invariant by construction, same as きずな/まもり.
    private static int SpeciesPercent(int speciesStat, float rate, int followers, float cap) =>
        Mathf.FloorToInt(speciesStat * Mathf.Min(cap, rate * followers));

    // How many OTHER party members hold むれのいちいん - the term every
    // リーダー trait scales on.
    private int CountFollowers(Entity leader) =>
        PartyElementCensus.CountAlliesWithUniqueTrait(leader, _floorController?.AllActors(), "mure_no_ichiin");

    // Does any party member hold one of the four リーダー traits? The
    // condition むれのいちいん keys off (the mirror of CountFollowers).
    private bool HasAnyLeaderAlly(Entity follower)
    {
        var actors = _floorController?.AllActors();
        return PartyElementCensus.AnyAllyHasUniqueTrait(follower, actors, "reikyaku_leader", includeSelf: false)
            || PartyElementCensus.AnyAllyHasUniqueTrait(follower, actors, "chikaramochi_leader", includeSelf: false)
            || PartyElementCensus.AnyAllyHasUniqueTrait(follower, actors, "mofumofu_leader", includeSelf: false)
            || PartyElementCensus.AnyAllyHasUniqueTrait(follower, actors, "tousotsu_leader", includeSelf: false);
    }

    // ばくげき (stage 9 §1.9): the holder's every DRAGON move is swapped for
    // the fixed ばくげき move at use time. Resolved once at the top of
    // Execute so the whole pipeline below - PP (still spent from the slot
    // the player actually chose), range, damage, logging - sees only the
    // replacement, with no per-site special-casing.
    //
    // A missing moves.json entry falls back to the original rather than
    // crashing; the swap is a buff, not a correctness requirement.
    private MoveData ResolveMove(MoveData move)
    {
        if (move == null || move.Type != "Dragon") return move;
        if (!HasTrait(_attacker, "bakugeki")) return move;

        return MoveDatabase.Get("bakugeki") ?? move;
    }

    // The 8 surrounding tiles, in the same order FloorController's own
    // auto-aim scans them - reused by チェイサー's landing-tile search.
    private static readonly Vector2I[] EightDirections =
    {
        new(0, -1), new(0, 1), new(-1, 0), new(1, 0),
        new(-1, -1), new(1, -1), new(-1, 1), new(1, 1),
    };

    // ざんきょうのしゅごしゃ (§4, stage 3): returns the damage `victim`
    // should actually take, after any qualifying guardian has shouldered
    // its 15% share (and been dealt that share themselves).
    //
    // Chebyshev distance, not Manhattan: this project is 8-directional
    // everywhere (movement, melee reach, Area blasts), so "2マス以内" is the
    // 5x5 box around the victim, consistent with every other range rule.
    // Only the FIRST qualifying guardian fires - the 15% is a flat share,
    // not a per-holder stack, matching the "重複不可" posture the other
    // party-wide traits already take.
    private int ApplyGuardianShoulder(Entity victim, int damage)
    {
        if (damage <= 0) return damage;

        var guardian = FindGuardian(victim, _floorController?.AllActors());
        if (guardian == null) return damage;

        // 15% of the hit, but never a silent no-op: floor(damage*0.15) is 0
        // for damage <= 6, which would disable the guardian in exactly the
        // low-power early-game fights where a shield matters most. Hence the
        // Max(1). The Min(damage-1) is the other half of that guarantee -
        // the victim must still take at least 1 (a landed hit always deals
        // >= 1, per DamageCalculator's own floor), so the guardian can never
        // absorb a hit outright. Behaviour for damage >= 7 is unchanged,
        // where floor(damage*0.15) is already >= 1.
        int shouldered = Mathf.Min(damage - 1, Mathf.Max(1, Mathf.FloorToInt(damage * 0.15f)));
        if (shouldered <= 0) return damage; // damage == 1: nothing left to split

        guardian.Stats.TakeDamage(shouldered);
        guardian.PlayHitFlash();
        guardian.ShowDamagePopup(shouldered);
        MessageLogger.Log($"{guardian.ActorName} shielded {victim.ActorName}, taking {shouldered} damage!", MessageLogger.EffectiveColor);

        // The guardian can shoulder itself to death - the confirmed reading
        // takes REAL damage with no death guard, so route it through the
        // same faint handling any other lethal hit gets.
        if (!guardian.Stats.IsAlive) HandleFaint(guardian);

        return damage - shouldered;
    }

    // The guardian-selection half of ざんきょうのしゅごしゃ, split out as a
    // pure function over a plain actor enumerable (no FloorController, no
    // grid) for the same reason PartyElementCensus takes one - the search
    // rules are the interesting part and this keeps them unit-testable
    // against a hand-built roster. Returns null when nobody qualifies.
    public static Entity FindGuardian(Entity victim, IEnumerable<Entity> actors)
    {
        if (actors == null) return null;

        foreach (var actor in actors)
        {
            if (actor == victim || actor.Faction != victim.Faction) continue;
            if (!HasTrait(actor, "zankyou_no_shugosha")) continue;
            if (!actor.Stats.IsAlive) continue;

            var delta = (actor.GridPosition - victim.GridPosition).Abs();
            if (Mathf.Max(delta.X, delta.Y) > 2) continue; // Chebyshev "2マス以内" = the 5x5 box

            return actor;
        }

        return null;
    }

    // チェイサー (§4, stage 3): every same-faction holder standing in the
    // SAME ROOM as the attacker warps to a free tile adjacent to the enemy
    // that was just attacked, and self-stuns as the cost.
    //
    // Skipped entirely when the holder is already adjacent to that enemy
    // (the confirmed reading) - warping nowhere while still eating a stun
    // would just be a self-inflicted lockout every time an ally swings.
    private void TriggerChasers(MoveData move)
    {
        // "攻撃した際" - a pure Status move isn't an attack. A friendly-fire
        // hit has no "敵" to chase either, so the target must be hostile.
        if (move.Category == MoveCategory.Status) return;
        if (_floorController == null || _defender == null || _attacker.Grid == null) return;
        if (_defender.Faction == _attacker.Faction) return;

        var grid = _attacker.Grid;
        int attackerRoom = grid.GetRoomId(_attacker.GridPosition);
        if (attackerRoom < 0) return; // attacker is in a corridor: "同室" is undefined

        // Snapshotted - PlaceAt mutates positions while we walk the roster.
        foreach (var actor in new List<Entity>(_floorController.AllActors()))
        {
            if (!IsChaseCandidate(actor, _attacker, _defender, grid.GetRoomId(actor.GridPosition), attackerRoom)) continue;

            var landing = FindFreeTileAdjacentTo(_defender, actor);
            if (landing == null) continue; // fully boxed in: no warp, and no stun either

            actor.PlaceAt(landing.Value);
            actor.StatusEffects.TryApplyAilment(AilmentType.Stun);
            MessageLogger.Log($"{actor.ActorName} chased in on {_defender.ActorName} and must recover!", MessageLogger.ProgressionColor);
        }
    }

    // Whether `actor` reacts to `attacker`'s strike on `enemy`. Room ids are
    // passed in rather than a GridManager so this stays a pure predicate
    // (same testability split as FindGuardian above): the caller does the
    // one grid lookup, this owns all the rules.
    public static bool IsChaseCandidate(Entity actor, Entity attacker, Entity enemy, int actorRoom, int attackerRoom)
    {
        if (actor == attacker || actor.Faction != attacker.Faction) return false;
        if (!HasTrait(actor, "chaser")) return false;
        if (actorRoom < 0 || actorRoom != attackerRoom) return false;

        // Already in melee reach of the enemy: no warp AND no stun (the
        // confirmed reading - a stun for a zero-distance move is pure loss).
        var gap = (actor.GridPosition - enemy.GridPosition).Abs();
        return Mathf.Max(gap.X, gap.Y) > 1;
    }

    // First free, terrain-legal tile adjacent to `enemy` that `mover` could
    // stand on. Respects the mover's own hazard immunities (Stats.CanTraverse,
    // same rule its ordinary movement uses) and never lands on an occupied
    // tile, so a warp can't stack two actors.
    private Vector2I? FindFreeTileAdjacentTo(Entity enemy, Entity mover)
    {
        var grid = mover.Grid;
        if (grid == null) return null;

        foreach (var dir in EightDirections)
        {
            var pos = enemy.GridPosition + dir;
            if (!grid.InBounds(pos)) continue;
            if (!mover.Stats.CanTraverse(grid.GetTile(pos).Terrain)) continue;
            if (_floorController.GetEntityAt(pos) != null) continue;
            return pos;
        }

        return null;
    }

    // 〇〇のまもり (§4): does a guard-template trait matching EITHER of
    // `defender`'s own Types exist anywhere in their party (self
    // included)? Checked against both Types since a dual-typed defender
    // benefits from a guard trait matching either one.
    private bool HasPartyGuard(Entity defender)
    {
        if (Enum.TryParse<Element>(defender.Stats.Type1, out var type1)
            && PartyElementCensus.AnyAllyHasTemplateTrait(defender, _floorController?.AllActors(), TraitTemplateKind.Guard, type1, includeSelf: true))
            return true;

        return !string.IsNullOrEmpty(defender.Stats.Type2)
            && Enum.TryParse<Element>(defender.Stats.Type2, out var type2)
            && PartyElementCensus.AnyAllyHasTemplateTrait(defender, _floorController?.AllActors(), TraitTemplateKind.Guard, type2, includeSelf: true);
    }

    // Death processing shared by both paths: EXP notification (before
    // Die() so the victim is still readable), then faction-gated kill
    // tracking + drops, then Die().
    private void HandleFaint(Entity victim)
    {
        MessageLogger.Log($"{victim.ActorName} fainted!", MessageLogger.FaintColor);

        _floorController?.Experience?.NotifyDefeated(victim, _attacker);

        if (_attacker.Faction == Faction.Player && victim.Faction == Faction.Enemy)
        {
            _floorController?.RunTracker.RecordKill(victim.SpeciesId);
            MaterialDropTable.TryDrop(_floorController, victim.GridPosition, victim.ActorName);
        }

        victim.Die();
    }

    // Self-inflicted recoil, shared by both paths. totalDamageDealt is the
    // sum across every target hit (§2/§4-3: recoil fires once, on the
    // combined damage). Self-KO -> normal death, no EXP (no attacker).
    // Blocked while the user is MudCaked, or the (primary) target holds
    // きょうじんなからだ (status-redesign §4-5 / trait_catalog_v2 §3).
    // ふんどのつばさ (stage 9 §1): an EXTRA 33% of damage dealt as recoil,
    // on top of whatever the move's own RecoilHpPercent already inflicts.
    // Read as a flat 33%, NOT 33% per qualifying ally - the per-ally term in
    // the source text scales the +5 power; compounding the recoil the same
    // way would reach ~99% self-damage at three allies, which no kit could
    // sustain. Flagged as an interpretation in the stage-9 report.
    private void ApplyFundoRecoil(int totalDamageDealt)
    {
        if (!HasTrait(_attacker, "fundo_no_tsubasa")) return;
        if (totalDamageDealt <= 0 || !_attacker.Stats.IsAlive) return;

        int recoil = Mathf.Max(1, Mathf.FloorToInt(totalDamageDealt * 0.33f));
        _attacker.Stats.TakeDamage(recoil);
        _attacker.PlayHitFlash();
        _attacker.ShowDamagePopup(recoil);
        MessageLogger.Log($"{_attacker.ActorName} is battered by its own fury! ({recoil} damage)", MessageLogger.IneffectiveColor);
        if (!_attacker.Stats.IsAlive) HandleFaint(_attacker);
    }

    // ---- クラッシュ系: knockback -------------------------------------
    // The target is shoved one tile directly away from the attacker ("攻撃
    // された方向から対角方向に1マス"): the step is the sign of
    // target - attacker on each axis, so a diagonal hit shoves diagonally.
    //
    // If the destination is blocked - a wall, or another actor standing
    // there - nobody moves and the shoved Pal takes 5% of its max HP. When
    // the blocker was itself a Pal, it takes the same 5% of ITS OWN max HP,
    // which is what "そのパルも同様のダメージを受ける" asks for (the same
    // rule, not the same number).
    //
    // Water and lava do NOT block: a knockback puts the target in regardless
    // of its element or ecology, which is the one case that overrides
    // Stats.CanTraverse.
    private const float KnockbackCollisionHpPercent = 0.05f;

    private void ApplyKnockback(MoveData move, Entity target)
    {
        if (move.WeaponTag != WeaponTag.Crush) return;
        if (target?.Grid == null || !GodotObject.IsInstanceValid(target)) return;

        var delta = target.GridPosition - _attacker.GridPosition;
        var step = new Vector2I(System.Math.Sign(delta.X), System.Math.Sign(delta.Y));
        if (step == Vector2I.Zero) return;

        var dest = target.GridPosition + step;
        var grid = target.Grid;

        bool offMap = !grid.InBounds(dest);
        bool wall = !offMap && grid.GetTile(dest).Terrain == TerrainType.Wall;
        var occupant = offMap ? null : _floorController?.GetEntityAt(dest);

        if (offMap || wall || occupant != null)
        {
            int selfDamage = System.Math.Max(1, Mathf.RoundToInt(target.Stats.MaxHp * KnockbackCollisionHpPercent));
            target.Stats.TakeDamage(selfDamage);
            MessageLogger.Log($"{target.ActorName} was knocked into something and took {selfDamage} damage!",
                              MessageLogger.IneffectiveColor);
            if (occupant != null && occupant.Stats.IsAlive)
            {
                int otherDamage = System.Math.Max(1, Mathf.RoundToInt(occupant.Stats.MaxHp * KnockbackCollisionHpPercent));
                occupant.Stats.TakeDamage(otherDamage);
                MessageLogger.Log($"{occupant.ActorName} was crushed against and took {otherDamage} damage!",
                                  MessageLogger.IneffectiveColor);
                if (!occupant.Stats.IsAlive) HandleFaint(occupant);
            }
            if (!target.Stats.IsAlive) HandleFaint(target);
            return;
        }

        // Chasm is left to the floor's own handling; water and lava are
        // entered even by a Pal that could never walk in on its own.
        target.MoveTo(dest);
        MessageLogger.Log($"{target.ActorName} was knocked back!");
    }

    // ---- レンド系: wipe the tile the user is standing on ----------------
    // "現在いるマスのトラップまたはフィールドを消滅させる" - the user's own
    // tile, so a Rend move doubles as a way out of a field you are stuck in.
    // Runs whether or not the move connected: the sweep is the move, not a
    // rider on the damage.
    private void ApplyRendClear(MoveData move, int damageDealt)
    {
        if (move.WeaponTag != WeaponTag.Rend || _floorController == null) return;

        var pos = _attacker.GridPosition;
        bool clearedField = _floorController.Fields.RemoveAt(pos);
        bool clearedTrap = _floorController.Objects.Get(pos) == MapObjectType.Trap;
        if (clearedTrap) _floorController.Objects.RemoveAt(pos);

        if (clearedField || clearedTrap)
            MessageLogger.Log($"{_attacker.ActorName}'s {move.Name} tore away what was underfoot!");
    }

    private void ApplyRecoil(MoveData move, int totalDamageDealt)
    {
        if (DefenderBlocksSecondaryEffects) return;
        if (move.RecoilHpPercent <= 0 || totalDamageDealt <= 0) return;

        int recoil = Mathf.FloorToInt(totalDamageDealt * move.RecoilHpPercent / 100f);
        if (recoil <= 0) return;

        _attacker.Stats.TakeDamage(recoil);
        MessageLogger.Log($"{_attacker.ActorName} is hit by recoil! ({recoil} damage)", MessageLogger.IneffectiveColor);

        if (!_attacker.Stats.IsAlive)
        {
            MessageLogger.Log($"{_attacker.ActorName} fainted from the recoil!", MessageLogger.FaintColor);
            _attacker.Die();
        }
    }

    // HP drain (DrainHalf kit): the user recovers DrainHpPercent of the
    // combined damage dealt, once - the healing sibling of ApplyRecoil.
    // Clamped to MaxHp by Stats.Heal; a dead attacker (self-KO'd by a
    // simultaneous mechanic) never heals. Blocked while the user is
    // MudCaked (§4-5, DrainHalf is in the blocked-effects list), the
    // (primary) target holds きょうじんなからだ (trait_catalog_v2 §3), OR
    // the user is VineBound (§4-3, "あらゆる回復を無効化" - drain is a
    // recovery path).
    private void ApplyDrain(MoveData move, int totalDamageDealt)
    {
        if (DefenderBlocksSecondaryEffects || _attacker.StatusEffects.IsVineBound) return;
        if (move.DrainHpPercent <= 0 || totalDamageDealt <= 0) return;
        if (!_attacker.Stats.IsAlive) return;

        int heal = Mathf.FloorToInt(totalDamageDealt * move.DrainHpPercent / 100f);
        if (heal <= 0) return;

        _attacker.Stats.Heal(heal);
        MessageLogger.Log($"{_attacker.ActorName} drained {heal} HP!", MessageLogger.ProgressionColor);

        // オーバーヒール (stage 9 §1.9): "技によってHPを回復すると" - this
        // drain is the only move-driven heal an over_heal holder can ever
        // receive. The other two Heal() sites in this file are あくむのひとみ
        // and あくい, both trait-driven, and a species holds exactly ONE
        // trait, so an over_heal holder is structurally excluded from both.
        // Item healing (UseItemAction) is not "技による" and stays untouched.
        int bonus = _attacker.StatusEffects.GetHealBonus(_attacker.Stats.MaxHp);
        if (bonus > 0)
        {
            _attacker.Stats.Heal(bonus);
            MessageLogger.Log($"{_attacker.ActorName} overheals for {bonus} more HP!", MessageLogger.ProgressionColor);
        }
    }

    // Self-destruct (メガトンじばく): the user always faints after the move
    // resolves. Routed through the same Die() path recoil self-KO uses,
    // so NPC removal / Player game-over both behave correctly.
    private void ApplySelfDestruct(MoveData move)
    {
        if (!move.SelfGuaranteedDeath) return;
        if (!_attacker.IsAlive) return; // already down (recoil/other) - don't double-mark

        _attacker.Stats.TakeDamage(_attacker.Stats.CurrentHp);
        MessageLogger.Log($"{_attacker.ActorName} self-destructed!", MessageLogger.FaintColor);
        _attacker.Die();
    }

    // ---- Single-target secondary-effect helpers ----
    // RankEffect is entirely blocked while the user is MudCaked, OR the
    // (primary) defender holds きょうじんなからだ (trait_catalog_v2 §3's
    // "全ブロック（泥まみれの実装を流用）" - an unconditional top-of-method
    // guard, exactly mirroring how the attacker's OWN IsMudCaked already
    // blocks Self-targeted effects too, not just Enemy-targeted ones).
    private void ApplyRankEffectIfAny(MoveData move, bool defenderAlive)
    {
        if (DefenderBlocksSecondaryEffects) return;

        // Each declared rank change rolls its own chance and lands
        // independently - a move that raises two ranks and drops a third
        // is three separate applications, in authored order.
        foreach (var effect in move.RankEffects)
        {
            if (effect.Target == StatusTarget.Enemy && !defenderAlive) continue;
            if (GD.Randf() >= effect.Chance) continue;

            var target = effect.Target == StatusTarget.Self ? _attacker : _defender;
            if (target == null) continue;
            ApplyRankTo(move, effect, target);
        }
    }

    // Status-redesign: AilmentEffect no longer applies via a per-hit
    // probability roll (except Stun, unchanged/out of scope per §3) -
    // instead it feeds the target's accumulation trackers (§2), which
    // fire the real ailment once a tracker crosses 1000. Entirely blocked
    // while the user is MudCaked (§4-5) OR the (primary) defender holds
    // きょうじんなからだ (trait_catalog_v2 §3 - same unconditional-guard
    // reasoning as ApplyRankEffectIfAny above).
    private void ApplyAilmentEffectIfAny(MoveData move, bool defenderAlive)
    {
        if (DefenderBlocksSecondaryEffects) return;
        if (move.AilmentTarget == StatusTarget.Enemy && !defenderAlive) return;

        // レッツハギング／ひょうてんま (§4): independent of the move's own
        // AilmentTarget - always lands on the actual defender being hit.
        ApplyTraitDrivenAccumulation(move, _defender);

        var target = move.AilmentTarget == StatusTarget.Self ? _attacker : _defender;

        if (move.AilmentEffect == AilmentType.Stun)
        {
            if (GD.Randf() * 100f >= move.AilmentChance) return;
            ApplyAilmentTo(target, AilmentType.Stun);
            return;
        }

        target.StatusEffects.AccumulateOnHit(EffectiveMoveType(_attacker, move), move.AilmentEffect, move.AilmentChance);
    }

    // ---- AoE secondary-effect helpers ----
    // Ailment/accumulation lands on every hit target (§4-2 "状態異常は全対象
    // へ通常判定"), per target - ignores AilmentTarget's Self/Enemy split,
    // since in AoE every hit actor IS a target (no current AoE move
    // self-ailments). Same Stun-stays-probabilistic / MudCaked-blocks-all
    // split as the single-target path above.
    private void ApplyAoeAilment(MoveData move, Entity target)
    {
        if (BlocksSecondaryEffectsFor(target)) return;

        // レッツハギング／ひょうてんま (§4): per hit target, same as the
        // single-target path above.
        ApplyTraitDrivenAccumulation(move, target);

        if (move.AilmentEffect == AilmentType.Stun)
        {
            if (GD.Randf() * 100f >= move.AilmentChance) return;
            ApplyAilmentTo(target, AilmentType.Stun);
            return;
        }

        target.StatusEffects.AccumulateOnHit(EffectiveMoveType(_attacker, move), move.AilmentEffect, move.AilmentChance);
    }

    // レッツハギング／ひょうてんま (§4, stage 2-b): a trait-driven
    // accumulation path entirely separate from the move's own
    // AilmentChance - fires whenever the attacker holds the matching
    // trait and the move used is Physical-category, regardless of what
    // (if anything) the move itself declares. 500 = the same chance*10
    // conversion (50%) used everywhere else in the accumulation system.
    private void ApplyTraitDrivenAccumulation(MoveData move, Entity target)
    {
        if (target == null || !target.IsAlive) return;
        if (move.Category != MoveCategory.Physical) return;

        if (HasTrait(_attacker, "let_us_hug")) target.StatusEffects.AccumulateFlat(AilmentType.VineBound, 500);
        if (HasTrait(_attacker, "hyouten_ma")) target.StatusEffects.AccumulateFlat(AilmentType.Freeze, 500);
    }

    // Enemy-targeted rank effect: only opposing-faction hit targets, per
    // target (§4-2 "Target=Enemy なら敵対勢力の被弾者のみ"). Blocked while
    // the user is MudCaked (§4-5) or that specific target holds きょうじん
    // なからだ (trait_catalog_v2 §3).
    private void ApplyEnemyRankToTarget(MoveData move, Entity target)
    {
        if (BlocksSecondaryEffectsFor(target)) return;
        if (target.Faction == _attacker.Faction) return; // opposing only

        foreach (var effect in move.RankEffects)
        {
            if (effect.Target != StatusTarget.Enemy) continue;
            if (GD.Randf() >= effect.Chance) continue;
            ApplyRankTo(move, effect, target);
        }
    }

    // Self-targeted rank effect: the user, once (§4-2 "Target=Self は使用者に1回").
    // Blocked while the user is MudCaked, or the (primary) target holds
    // きょうじんなからだ (§4-5 / trait_catalog_v2 §3's "全ブロック" reuse).
    private void ApplySelfRankOnce(MoveData move)
    {
        if (DefenderBlocksSecondaryEffects) return;

        foreach (var effect in move.RankEffects)
        {
            if (effect.Target != StatusTarget.Self) continue;
            if (GD.Randf() >= effect.Chance) continue;
            ApplyRankTo(move, effect, _attacker);
        }
    }

    private void ApplyRankTo(MoveData move, RankEffect effect, Entity target)
    {
        var moveElement = Enum.TryParse<Element>(move.Type, out var parsed) ? parsed : Element.Neutral;
        target.StatusEffects.ApplyRankDelta(effect.Stat, effect.Delta, moveElement);
        string direction = effect.Delta > 0 ? "rose" : "fell";
        MessageLogger.Log($"{target.ActorName}'s {effect.Stat} {direction}!", MessageLogger.NeutralColor);

        // ランクアンカー(使い切り): 自分のランクが下がったら、下がった分だけ
        // 戻す。全ランクを0に均すのではなく、この変化だけを打ち消す。
        if (effect.Delta < 0 && target.Holds(Combat.BattleItemEffect.RestoreRank))
        {
            target.ConsumeHeldItem();
            target.StatusEffects.ApplyRankDelta(effect.Stat, -effect.Delta, moveElement);
            MessageLogger.Log($"{target.ActorName}のランクアンカーで{effect.Stat}が元に戻った!",
                              MessageLogger.EffectiveColor);
        }
    }

    private void ApplyAilmentTo(Entity target, AilmentType ailment)
    {
        if (target.StatusEffects.TryApplyAilment(ailment))
        {
            MessageLogger.Log($"{target.ActorName} was afflicted with {ailment}!", MessageLogger.IneffectiveColor);

            // キュアベル(使い切り): 状態異常になった瞬間に回復する。
            if (target.Holds(Combat.BattleItemEffect.CureAilment))
            {
                target.ConsumeHeldItem();
                target.StatusEffects.ClearAilmentIfType(ailment);
                MessageLogger.Log($"{target.ActorName}のキュアベルで{ailment}が治った!",
                                  MessageLogger.EffectiveColor);
            }
        }
        else
            MessageLogger.Log($"{target.ActorName} is unaffected - already under a status condition.", MessageLogger.NeutralColor);
    }
}
