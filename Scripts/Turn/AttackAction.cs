using System;
using System.Collections.Generic;
using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;
using MysteryDungeon.Dungeon;
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

    public void Execute(int turnNumber)
    {
        var move = _moveSlot.Data;

        // Out of PP = the move simply fails (turn still consumed).
        if (_moveSlot.CurrentPp <= 0)
        {
            MessageLogger.Log($"{_attacker.ActorName} tried to use {move.Name}, but it has no PP left!", MessageLogger.IneffectiveColor);
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

        // AoE needs the floor (actor enumeration) and the grid; without
        // them (shouldn't happen in-dungeon) fall back to the single path.
        bool canAoe = move.Range != MoveRange.Adjacent && _floorController != null && _attacker.Grid != null;
        if (canAoe)
            ExecuteAoe(move);
        else
            ExecuteSingle(move);

        // Self-destruct (メガトン自爆): the user faints once the move has
        // fully resolved, hit or miss (§ self_guaranteed_death). Applied
        // here - after both paths, after their recoil/drain - so the
        // damage the move dealt still lands first.
        ApplySelfDestruct(move);
    }

    // ---- Single-target / bump path (Adjacent) - Phase 6..21 behaviour ----
    private void ExecuteSingle(MoveData move)
    {
        // Menu-invoked moves may auto-aim to nothing - still costs the turn.
        if (_defender == null)
        {
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

        int damage = StrikeTarget(move, _defender);
        if (damage > 0)
        {
            bool alive = _defender.Stats.IsAlive;
            ApplyRankEffectIfAny(move, alive);
            ApplyAilmentEffectIfAny(move, alive);
            if (!alive) HandleFaint(_defender);
        }

        ApplyDrain(move, damage);
        ApplyRecoil(move, damage);
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
        foreach (var target in new List<Entity>(targets))
        {
            if (!GodotObject.IsInstanceValid(target) || !target.IsAlive) continue;

            if (move.Category == MoveCategory.Status)
            {
                ApplyAoeAilment(move, target);
                ApplyEnemyRankToTarget(move, target);
                continue;
            }

            int damage = StrikeTarget(move, target);
            if (damage <= 0) continue; // missed this target

            totalDamage += damage;
            if (target.Stats.IsAlive)
            {
                ApplyAoeAilment(move, target);
                ApplyEnemyRankToTarget(move, target);
            }
            else
            {
                HandleFaint(target);
            }
        }

        ApplyDrain(move, totalDamage);
        ApplyRecoil(move, totalDamage);
    }

    // Core per-target strike: hit roll -> crit -> Phase 16 damage -> burn
    // penalty -> apply. Returns damage dealt (0 = missed). Shared verbatim
    // by the single and AoE paths so both roll accuracy/crit/damage
    // identically. Does NOT apply secondary rank/ailment or death - the
    // caller sequences those (secondary effects run on a still-alive
    // target before death processing).
    private int StrikeTarget(MoveData move, Entity target)
    {
        // Darkness (暗闇): a direct x0.7 on the afflicted attacker's own
        // outgoing hit chance, separate from the AccuracyRank ladder
        // (status-redesign §4-4). Skipped, like the rank multipliers
        // already are, when IsGuaranteedHit short-circuits the roll.
        float darknessMul = _attacker.StatusEffects.IsInDarkness ? 0.7f : 1.0f;

        // クイックステップ (§3): always +1 evasion rank, folded into the
        // same table lookup the ordinary Evasion rank uses.
        int evasionBonus = HasTrait(target, "quick_step") ? 1 : 0;

        // IsGuaranteedHit bypasses the roll and the target's evasion rank.
        bool hits = move.IsGuaranteedHit
            || GD.Randf() * 100f < move.Accuracy * _attacker.StatusEffects.GetAccuracyMultiplier() * target.StatusEffects.GetEvasionMultiplierWithBonus(evasionBonus) * darknessMul;
        if (!hits)
        {
            MessageLogger.Log($"{_attacker.ActorName}'s {move.Name} missed {target.ActorName}!", MessageLogger.IneffectiveColor);
            return 0;
        }

        var defenderStats = target.Stats;

        // Soaked (ずぶ濡れ) overrides an entity's COMBAT-relevant Types to
        // single Water - scoped narrowly to type-effectiveness (here) and
        // STAB (below) only, per status-redesign §4-2; GetMovementProfile
        // and everything else keeps reading the real Type1/Type2 (out of
        // scope - see CombatTypes).
        var (defType1, defType2) = CombatTypes(target);
        float typeMultiplier = TypeChartManager.GetMultiplier(move.Type, defType1, defType2);

        // STAB (same-type attack bonus): x1.2 when the move's Type
        // matches either of the attacker's own (possibly Soaked-overridden)
        // Types. A move is always single-typed and an attacker has at most
        // 2 Types, so this is a strict either/or - "both Types match"
        // can't structurally occur (multitype_stab_proposal §7-1), no
        // double-counting to guard.
        var (atkType1, atkType2) = CombatTypes(_attacker);
        bool stabApplies = move.Type == atkType1 || (!string.IsNullOrEmpty(atkType2) && move.Type == atkType2);
        // 〇〇派 (§3): when STAB already applies, a matching stab-template
        // trait replaces the usual 1.2x with 1.5x ("差し替え" - it's not an
        // additional stack, STAB just becomes stronger for this holder).
        float stabMultiplier = stabApplies
            ? (HasMatchingTemplateTrait(_attacker, move.Type, TraitTemplateKind.Stab) ? 1.5f : 1.2f)
            : 1.0f;

        // 〇〇式 (§4, party census): when this hit is exactly a single
        // weakness (2.0x - the doc's literal "2.0→2.5", not a general
        // "+25% to any weakness" rule, so a double-weakness 4.0 is left
        // untouched), a party-wide weakness-template trait matching the
        // move's own element upgrades it to 2.5x. Self-inclusive (no "他"
        // qualifier in the source text, unlike ちから below).
        if (typeMultiplier == 2.0f && Enum.TryParse<Element>(move.Type, out var weaknessElement)
            && PartyElementCensus.AnyAllyHasTemplateTrait(_attacker, _floorController?.AllActors(), TraitTemplateKind.Weakness, weaknessElement, includeSelf: true))
            typeMultiplier = 2.5f;

        // MudCaked (泥まみれ) OR the target holding きょうじんなからだ (§3,
        // reuses MudCaked's block-list wholesale) neuters the move's OWN
        // CritRankBonus/DragonMultiplier for this strike - the attacker's
        // own CritRank and the base formula are untouched, only the
        // move-level kickers are blocked.
        bool blocked = BlocksSecondaryEffectsFor(target);
        int effectiveCritRankBonus = blocked ? 0 : move.CritRankBonus;
        float effectiveDragonMultiplier = blocked ? 1.0f : move.DragonMultiplier;

        // Crit rolled per target (§4-3), with the move's CritRankBonus.
        bool isCrit = GD.Randf() < _attacker.StatusEffects.GetCritChanceWithBonus(effectiveCritRankBonus);

        float atkMul = _attacker.StatusEffects.GetAtkMultiplier();
        float defMul = target.StatusEffects.GetDefMultiplier();
        float powerMul = _attacker.StatusEffects.GetElementPowerMultiplier(move.Type);

        // 〇〇のきずな (§4, party census): the attacker's OWN bond-template
        // trait (if any) scales their own Attack by 8% per matching-
        // element ally, capped at 3 bodies (24%) - not move.Type-dependent
        // at all, a standing buff active on every attack. Folds into the
        // same "individual passive / party skill" slot AtkMultiplier was
        // already documented for.
        var bondTrait = TraitDatabase.Get(_attacker.Stats.Trait);
        if (bondTrait != null && bondTrait.Category == TraitCategory.Template
            && bondTrait.TemplateKind == TraitTemplateKind.Bond && bondTrait.Element.HasValue)
        {
            int allyCount = Mathf.Min(3, PartyElementCensus.CountAlliesWithType(_attacker, _floorController?.AllActors(), bondTrait.Element.Value));
            atkMul *= 1.0f + allyCount * 0.08f;
        }

        // 〇〇のちから (§4, party census): if some OTHER party member holds
        // a power-template trait matching this move's own element, +10%
        // power. Existence-only check ("重複不可" - multiple holders still
        // only grant +10% once, no extra dedup needed).
        if (Enum.TryParse<Element>(move.Type, out var powerElement)
            && PartyElementCensus.AnyAllyHasTemplateTrait(_attacker, _floorController?.AllActors(), TraitTemplateKind.Power, powerElement, includeSelf: false))
            powerMul *= 1.1f;

        // 〇〇のまもり (§4, party census): if a guard-template trait matching
        // either of the DEFENDER's own Types exists anywhere in their
        // party (holder included - "全員" covers the holder too, unlike
        // ちから's "他パルの"), +10% defense.
        if (HasPartyGuard(target)) defMul *= 1.1f;

        if (isCrit)
        {
            atkMul = Mathf.Max(1f, atkMul);
            defMul = Mathf.Min(1f, defMul);
            powerMul = Mathf.Max(1f, powerMul);
        }

        var ctx = new DamageContext
        {
            BaseAtk = _attacker.Stats.Attack,
            BaseDef = defenderStats.Defense,
            BasePower = move.Power,
            AttackElement = move.Type,
            DefenderElement = defType1,
            TypeEffectiveness = typeMultiplier,
            StabMultiplier = stabMultiplier,
            AtkMultiplier = atkMul,
            DefMultiplier = defMul,
            PowerMultiplier = powerMul,
            CritMultiplier = isCrit ? 1.5f : 1.0f,
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
        if (HasMatchingTemplateTrait(target, move.Type, TraitTemplateKind.Resist))
            damage = Mathf.Max(1, Mathf.FloorToInt(damage * 0.15f));

        // きぬぬい (§3): one-time -10% on the next damage this entity takes,
        // armed by their own prior Ice-move use (see Execute()).
        if (target.StatusEffects.ConsumeDamageReductionIfArmed())
            damage = Mathf.Max(1, Mathf.FloorToInt(damage * 0.9f));

        defenderStats.TakeDamage(damage);
        _attacker.StatusEffects.ResetDamageTimer();
        target.StatusEffects.ResetDamageTimer();

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
    }

    // Self-destruct (メガトン自爆): the user always faints after the move
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
        if (move.RankEffectStat == RankStat.None || move.RankEffectDelta == 0) return;
        if (move.RankEffectTarget == StatusTarget.Enemy && !defenderAlive) return;
        if (GD.Randf() >= move.RankEffectChance) return;

        var target = move.RankEffectTarget == StatusTarget.Self ? _attacker : _defender;
        ApplyRankTo(move, target);
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

        var target = move.AilmentTarget == StatusTarget.Self ? _attacker : _defender;

        if (move.AilmentEffect == AilmentType.Stun)
        {
            if (GD.Randf() * 100f >= move.AilmentChance) return;
            ApplyAilmentTo(target, AilmentType.Stun);
            return;
        }

        target.StatusEffects.AccumulateOnHit(move.Type, move.AilmentEffect, move.AilmentChance);
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

        if (move.AilmentEffect == AilmentType.Stun)
        {
            if (GD.Randf() * 100f >= move.AilmentChance) return;
            ApplyAilmentTo(target, AilmentType.Stun);
            return;
        }

        target.StatusEffects.AccumulateOnHit(move.Type, move.AilmentEffect, move.AilmentChance);
    }

    // Enemy-targeted rank effect: only opposing-faction hit targets, per
    // target (§4-2 "Target=Enemy なら敵対勢力の被弾者のみ"). Blocked while
    // the user is MudCaked (§4-5) or that specific target holds きょうじん
    // なからだ (trait_catalog_v2 §3).
    private void ApplyEnemyRankToTarget(MoveData move, Entity target)
    {
        if (BlocksSecondaryEffectsFor(target)) return;
        if (move.RankEffectStat == RankStat.None || move.RankEffectDelta == 0) return;
        if (move.RankEffectTarget != StatusTarget.Enemy) return;
        if (target.Faction == _attacker.Faction) return; // opposing only
        if (GD.Randf() >= move.RankEffectChance) return;
        ApplyRankTo(move, target);
    }

    // Self-targeted rank effect: the user, once (§4-2 "Target=Self は使用者に1回").
    // Blocked while the user is MudCaked, or the (primary) target holds
    // きょうじんなからだ (§4-5 / trait_catalog_v2 §3's "全ブロック" reuse).
    private void ApplySelfRankOnce(MoveData move)
    {
        if (DefenderBlocksSecondaryEffects) return;
        if (move.RankEffectStat == RankStat.None || move.RankEffectDelta == 0) return;
        if (move.RankEffectTarget != StatusTarget.Self) return;
        if (GD.Randf() >= move.RankEffectChance) return;
        ApplyRankTo(move, _attacker);
    }

    private void ApplyRankTo(MoveData move, Entity target)
    {
        var moveElement = Enum.TryParse<Element>(move.Type, out var parsed) ? parsed : Element.Neutral;
        target.StatusEffects.ApplyRankDelta(move.RankEffectStat, move.RankEffectDelta, moveElement);
        string direction = move.RankEffectDelta > 0 ? "rose" : "fell";
        MessageLogger.Log($"{target.ActorName}'s {move.RankEffectStat} {direction}!", MessageLogger.NeutralColor);
    }

    private void ApplyAilmentTo(Entity target, AilmentType ailment)
    {
        if (target.StatusEffects.TryApplyAilment(ailment))
            MessageLogger.Log($"{target.ActorName} was afflicted with {ailment}!", MessageLogger.IneffectiveColor);
        else
            MessageLogger.Log($"{target.ActorName} is unaffected - already under a status condition.", MessageLogger.NeutralColor);
    }
}
