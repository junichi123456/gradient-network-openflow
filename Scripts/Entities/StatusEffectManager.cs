using Godot;
using System.Collections.Generic;
using MysteryDungeon.Combat;

namespace MysteryDungeon.Entities;

// Phase 21: per-entity battle-effect state (Atk/Def/Accuracy/Evasion/
// ElementPower ranks, and the 5-way-exclusive primary ailment + the
// independent Stun flag). Auto-attached by Entity._Ready() (see
// Entity.StatusEffects), same pattern as Stats/Moves. Pure state +
// calculation - HP mutation, logging, and visuals stay at the call site
// (Entity.ResolveStatusTick / AttackAction), matching how EntityStats
// itself never touches PlayHitFlash/ShowDamagePopup either.
//
// Not persisted across floors (Phase 19's PartyState hydrate/dehydrate
// doesn't touch this) - battle effects are floor-local, matching this
// project's existing roguelike convention that a fresh floor is a fresh
// tactical slate.
public partial class StatusEffectManager : Node
{
    // ---- Atk/Def ranks: the classic +/-6 stage formula ----
    // multiplier = (2+rank)/2 for rank>=0, 2/(2-rank) for rank<0.
    // rank=6 -> 4.0x (the "3/2..8/2" ladder), rank=-6 -> 0.25x (the
    // "2/3..2/8" ladder) - confirmed to match the spec's fractions
    // exactly for every one of the 12 non-zero ranks.
    public int AtkRank { get; private set; }
    public int DefRank { get; private set; }
    private const int AtkDefRankMin = -6;
    private const int AtkDefRankMax = 6;

    // 期間限定ランクバフ (攻守一体, trait_catalog_v2 §4 stage 2-c): a
    // SEPARATE bonus from AtkRank/DefRank, on its own fixed-length
    // countdown rather than the shared 10-turn step-toward-zero decay
    // below - it reverts to exactly 0 the instant the countdown reaches
    // 0 (a hard cutoff), not a gradual step. Folded transparently into
    // GetAtkMultiplier/GetDefMultiplier (clamped into the same -6..+6
    // ladder as the permanent rank) so no call site needs to know it
    // exists separately.
    private int _tempAtkRankBonus;
    private int _tempDefRankBonus;
    private int _tempRankTurnsRemaining;

    public void ArmTemporaryRankBuff(int atkBonus, int defBonus, int turns)
    {
        if (BlocksRankUp && (atkBonus > 0 || defBonus > 0)) return;   // ドラゴンアンカー
        _tempAtkRankBonus = atkBonus;
        _tempDefRankBonus = defBonus;
        _tempRankTurnsRemaining = turns;
    }

    public float GetAtkMultiplier() => RankMultiplier(Mathf.Clamp(AtkRank + _tempAtkRankBonus, AtkDefRankMin, AtkDefRankMax));
    public float GetDefMultiplier() => RankMultiplier(Mathf.Clamp(DefRank + _tempDefRankBonus, AtkDefRankMin, AtkDefRankMax));

    private static float RankMultiplier(int rank) => rank >= 0 ? (2f + rank) / 2f : 2f / (2f - rank);

    // ---- Accuracy rank: 5 states (-2..+2), confirmed reading (a) ----
    // rank -2=1/3, -1=2/3, 0=1.0(neutral), +1=4/3, +2=5/3. "必中"
    // (IsGuaranteedHit) is a move-level flag, not a rank value - it
    // bypasses this table and GetEvasionMultiplier entirely.
    public int AccuracyRank { get; private set; }
    private const int AccuracyRankMin = -2;
    private const int AccuracyRankMax = 2;
    private static readonly float[] AccuracyTable = { 1f / 3f, 2f / 3f, 1f, 4f / 3f, 5f / 3f }; // index = rank + 2

    public float GetAccuracyMultiplier() => AccuracyTable[AccuracyRank - AccuracyRankMin];

    // Accuracy with a flat rank bonus folded in (じゆうのつばさ: +1 per
    // qualifying ally, capped at +2) - the same shape as
    // GetEvasionMultiplierWithBonus/GetCritChanceWithBonus.
    public float GetAccuracyMultiplierWithBonus(int bonus) =>
        AccuracyTable[Mathf.Clamp(AccuracyRank + bonus, AccuracyRankMin, AccuracyRankMax) - AccuracyRankMin];

    // ---- Evasion rank: 3 states (0..+3), defense-only (no "evasion
    // down" was specified - only 7/8, 6/8, 5/8 exist, all <1). Applied
    // as an extra multiplier against the ATTACKER's hit chance.
    public int EvasionRank { get; private set; }
    private const int EvasionRankMin = 0;
    private const int EvasionRankMax = 3;
    private static readonly float[] EvasionTable = { 1f, 7f / 8f, 6f / 8f, 5f / 8f }; // index = rank

    public float GetEvasionMultiplier() => EvasionTable[EvasionRank];

    // Evasion with a flat rank bonus folded in (クイックステップ: always
    // +1) - same pattern as GetCritChanceWithBonus's move-level bonus.
    public float GetEvasionMultiplierWithBonus(int bonus) =>
        EvasionTable[Mathf.Clamp(EvasionRank + bonus, EvasionRankMin, EvasionRankMax)];

    // ---- Element power rank: 4 states (-2..-1, +1..+2), single slot ----
    // Only one element's correction can be active at a time; applying a
    // correction for a DIFFERENT element fully overwrites the slot (see
    // ApplyRankDelta). The affected element is always the move's OWN
    // Type - there's no separate "which element" field on MoveData.
    public Element? ElementPowerElement { get; private set; }
    public int ElementPowerRank { get; private set; }
    private const int ElementPowerRankMin = -2;
    private const int ElementPowerRankMax = 2;
    private static readonly float[] ElementPowerTable = { 1f / 3f, 2f / 3f, 1f, 1.5f, 2f }; // index = rank + 2

    public float GetElementPowerMultiplier(string moveType)
    {
        if (ElementPowerElement == null) return 1f;
        if (!System.Enum.TryParse<Element>(moveType, out var element) || element != ElementPowerElement.Value) return 1f;
        return ElementPowerTable[ElementPowerRank - ElementPowerRankMin];
    }

    // ---- Crit rank: 6 states (0..+5), POSITIVE-ONLY (crit rate can be
    // raised, never lowered - confirmed). rank 0 = base 1/30, then
    // 1/15, 1/8, 1/4, 1/2, and 1/1 (guaranteed crit) at rank +5. The
    // per-hit crit roll and the "ignore disadvantageous rank
    // corrections" damage rule live in AttackAction; this component only
    // stores the rate.
    public int CritRank { get; private set; }
    private const int CritRankMin = 0;
    private const int CritRankMax = 5;
    private static readonly float[] CritChanceTable = { 1f / 30f, 1f / 15f, 1f / 8f, 1f / 4f, 1f / 2f, 1f }; // index = rank

    public float GetCritChance() => CritChanceTable[CritRank];

    // Crit chance with a move-specific CritRankBonus folded in (see
    // MoveData.CritRankBonus) - the bonus is added to this entity's own
    // crit rank, clamped into the same [0,5] band, before the lookup.
    public float GetCritChanceWithBonus(int moveCritRankBonus) =>
        CritChanceTable[Mathf.Clamp(CritRank + moveCritRankBonus, CritRankMin, CritRankMax)];

    // moveElement is only consulted for RankStat.ElementPower; callers
    // pass default(Element) (Neutral) for the other four stats, where
    // it's simply unused.
    public void ApplyRankDelta(RankStat stat, int delta, Element moveElement = default)
    {
        // ドラゴンアンカー(RankUpBlock): 上昇だけを止める。下降は通る。
        if (delta > 0 && BlocksRankUp) return;

        switch (stat)
        {
            case RankStat.Atk:
                AtkRank = Mathf.Clamp(AtkRank + delta, AtkDefRankMin, AtkDefRankMax);
                break;
            case RankStat.Def:
                DefRank = Mathf.Clamp(DefRank + delta, AtkDefRankMin, AtkDefRankMax);
                break;
            case RankStat.Accuracy:
                AccuracyRank = Mathf.Clamp(AccuracyRank + delta, AccuracyRankMin, AccuracyRankMax);
                break;
            case RankStat.Evasion:
                // ブリッツビート (stage 9 §1.5): "回避率が下がらない" - the
                // holder's Evasion rank is floored against any DECREASE.
                // The [0,3] clamp alone isn't enough: a debuff from rank 3
                // would still drop them to 2 without this guard. Increases
                // are untouched.
                if (delta < 0 && _blocksEvasionDrop) break;
                EvasionRank = Mathf.Clamp(EvasionRank + delta, EvasionRankMin, EvasionRankMax);
                break;
            case RankStat.ElementPower:
                if (ElementPowerElement != moveElement) ElementPowerRank = 0; // different element: start the slot fresh
                ElementPowerElement = moveElement;
                ElementPowerRank = Mathf.Clamp(ElementPowerRank + delta, ElementPowerRankMin, ElementPowerRankMax);
                break;
            case RankStat.Crit:
                CritRank = Mathf.Clamp(CritRank + delta, CritRankMin, CritRankMax); // positive-only, clamps at 0
                break;
        }
    }

    // ---- Rank decay: every rank (Atk/Def/Accuracy/Evasion/ElementPower,
    // confirmed uniform treatment) steps 1 toward zero every 10 turns
    // since this entity's last REAL damage exchange (dealt or received -
    // AttackAction calls ResetDamageTimer on both sides when damage
    // actually lands; DoT ticks deliberately do NOT reset it, confirmed).
    private int _turnsSinceLastDamageEvent;
    private const int DecayIntervalTurns = 10;

    public void ResetDamageTimer() => _turnsSinceLastDamageEvent = 0;

    private void AdvanceRankDecay()
    {
        // 期間限定ランクバフ's own independent 2-turn hard-cutoff countdown
        // - ticks every action-cycle unconditionally, unlike the shared
        // 10-turn decay below (which early-returns on 9 out of 10 calls).
        if (_tempRankTurnsRemaining > 0)
        {
            _tempRankTurnsRemaining--;
            if (_tempRankTurnsRemaining <= 0)
            {
                _tempAtkRankBonus = 0;
                _tempDefRankBonus = 0;
            }
        }

        _turnsSinceLastDamageEvent++;
        if (_turnsSinceLastDamageEvent % DecayIntervalTurns != 0) return;

        AtkRank = StepTowardZero(AtkRank);
        DefRank = StepTowardZero(DefRank);
        AccuracyRank = StepTowardZero(AccuracyRank);
        EvasionRank = StepTowardZero(EvasionRank);
        CritRank = StepTowardZero(CritRank); // positive-only, so this only ever steps down toward 0

        if (ElementPowerElement != null)
        {
            ElementPowerRank = StepTowardZero(ElementPowerRank);
            if (ElementPowerRank == 0) ElementPowerElement = null; // fully decayed - free the slot
        }
    }

    private static int StepTowardZero(int value) => value > 0 ? value - 1 : (value < 0 ? value + 1 : 0);

    // ---- Accumulation-status system (状態異常の再設計 proposal): every
    // primary ailment except Stun now triggers via a per-entity 0-1000
    // accumulator per tracked ailment, rather than a per-hit probability
    // roll. AilmentChance (still the same 400-move data field, values
    // unchanged) is reinterpreted as an accumulation multiplier instead
    // of a hit-chance: chancePercent*10 per declared hit (10%->+100).
    private readonly Dictionary<AilmentType, int> _accumulation = new();
    private const int AccumulationThreshold = 1000;
    private const int BaselineAccumulation = 25;

    // Which ailment a move's own element feeds (§2). Neutral/Dragon
    // (null) and anything outside the 7-element accumulation set never
    // accumulate at all - those moves' hits are simply inert here.
    private static AilmentType? ElementalAilment(string moveType) => moveType switch
    {
        "Fire" => AilmentType.Burn,
        "Water" => AilmentType.Soaked,
        "Electric" => AilmentType.Paralyze,
        "Ground" => AilmentType.MudCaked,
        "Grass" => AilmentType.VineBound,
        "Ice" => AilmentType.Freeze,
        "Dark" => AilmentType.Darkness,
        "Void" => AilmentType.Hollow,
        _ => null,
    };

    // Called on THIS entity (the one hit) once per successful strike of a
    // move whose element/ailment could accumulate. declaredAilment/
    // declaredChancePercent come straight from the move's own
    // AilmentEffect/AilmentChance fields (Stun is handled separately by
    // the caller - see AttackAction - and never reaches here).
    //
    // Baseline: any hit of a tracked-element move adds a flat +25 to
    // that element's own tracker, unconditionally. Bonus: if the move's
    // OWN declared ailment matches that same elemental tracker (e.g. a
    // Fire move explicitly declaring Burn), or the declared ailment is
    // Poison/Toxic (which never get a baseline of their own, and accrue
    // ONLY via a declaring move, on their own independent tracker,
    // regardless of the move's element), the declared chance is added
    // on top (chancePercent*10 - a 100%-chance declared move therefore
    // always maxes a fresh tracker in exactly one hit, same as its old
    // "always applies" behaviour).
    //
    // Accumulation is fully paused while ANY primary ailment is already
    // active on this entity (§2 "発現中は停止") - the mutual-exclusion
    // Ailment slot already guarantees at most one can be active, so a
    // single top-of-method guard covers every tracker at once.
    //
    // multiplier: しみこみ(2つめの特性)で技が与える蓄積量を倍にする。基礎・宣言一致・
    // 毒/猛毒の量に掛ける。AccumulateFlat(特性の固定加算)と発症のしきい値は倍にしない。
    public void AccumulateOnHit(string moveType, AilmentType declaredAilment, int declaredChancePercent, int multiplier = 1)
    {
        if (Ailment != AilmentType.None) return;

        var elemental = ElementalAilment(moveType);
        bool declaredMatchesElemental = declaredAilment != AilmentType.None && elemental != null && declaredAilment == elemental.Value;

        if (elemental != null)
        {
            int amount = (BaselineAccumulation + (declaredMatchesElemental ? declaredChancePercent * 10 : 0)) * multiplier;
            Add(elemental.Value, amount);
        }

        bool declaredIsPoisonOrToxic = declaredAilment == AilmentType.Poison || declaredAilment == AilmentType.Toxic;
        if (declaredIsPoisonOrToxic && Ailment == AilmentType.None) // re-check: the elemental Add above may have just triggered
            Add(declaredAilment, declaredChancePercent * 10 * multiplier);
    }

    private void Add(AilmentType type, int amount)
    {
        if (amount <= 0) return;

        int newValue = _accumulation.GetValueOrDefault(type) + amount;
        if (newValue >= AccumulationThreshold)
        {
            TryApplyAilment(type); // Ailment is guaranteed None here (caller-checked), so this always succeeds
            _accumulation.Clear(); // §2: ALL trackers reset the instant any one fires
        }
        else
        {
            _accumulation[type] = newValue;
        }
    }

    // 潜航 (trait_catalog_v2 §6 stage 4): while the holder stands in Water,
    // every accumulation tracker drains by `amount` per action-cycle. Drains
    // ALL trackers, not just the water one - the source text is "状態異常の
    // 蓄積値が毎ターン-100" with no per-ailment qualifier, and the trackers
    // are a single pool conceptually (any one firing already clears them all).
    //
    // Clamped at 0 and the entry dropped entirely, so a drained tracker is
    // indistinguishable from one that never accumulated - GetValueOrDefault
    // on the Add path treats a missing key as 0 either way.
    // Current tracker value for `type` (0 when absent). Read-only window
    // onto the accumulator - HUD/debug/verification read it, nothing
    // mutates through it.
    public int GetAccumulation(AilmentType type) => _accumulation.GetValueOrDefault(type);

    public void DecayAccumulation(int amount)
    {
        if (amount <= 0 || _accumulation.Count == 0) return;

        // Materialised first: the loop writes to/removes from the same
        // dictionary it reads.
        foreach (var type in new List<AilmentType>(_accumulation.Keys))
        {
            int reduced = _accumulation[type] - amount;
            if (reduced <= 0) _accumulation.Remove(type);
            else _accumulation[type] = reduced;
        }
    }

    // Darkness's clear condition ("受けると確定解除、発生源は問わない") is
    // driven externally by AttackAction on a landed Special-category hit,
    // not by this component's own turn-based checks - this is the public
    // seam for that. A no-op if the entity isn't currently under `type`.
    public void ClearAilmentIfType(AilmentType type)
    {
        if (Ailment == type) ClearAilment();
    }

    // Trait-driven accumulation (レッツハギング/ひょうてんま, trait_catalog_v2
    // §4 stage 2-b): a flat bonus toward a SPECIFIC ailment, entirely
    // independent of the move's own element/AilmentEffect matching logic
    // that AccumulateOnHit uses - the trait dictates the target ailment
    // directly ("技のAilmentChanceとは別に"). Same "paused while any
    // ailment already active" guard as AccumulateOnHit, so it composes
    // safely alongside the normal move-driven accumulation on the same hit.
    public void AccumulateFlat(AilmentType type, int amount)
    {
        if (Ailment != AilmentType.None) return;
        Add(type, amount);
    }

    // ---- Ailments: Poison/Toxic/Burn/Paralyze/Freeze/Soaked/MudCaked/
    // VineBound/Darkness are mutually exclusive (one slot); Stun is
    // independent and can coexist with any of them (e.g. a Poisoned
    // entity can also be Stunned).
    public AilmentType Ailment { get; private set; } = AilmentType.None;
    private int _ailmentTurnsElapsed;
    private int _toxicStacks; // "n" in the Toxic formula, only meaningful while Ailment == Toxic

    public bool IsStunned { get; private set; }

    // Paralyze blocks movement only - AttackAction/bump-attacks still
    // work (see TurnScheduler/Player's use of this). バッテリー holders
    // occupy this same Ailment value permanently but are exempt from the
    // movement lock itself (trait_catalog_v2 §3 - see MarkAsBattery).
    public bool IsMovementLocked => Ailment == AilmentType.Paralyze && !_isBatteryHolder;

    // Convenience checks for the 4 new ailments' engine hooks (see
    // AttackAction/UseItemAction) - same pattern as IsMovementLocked.
    public bool IsSoaked => Ailment == AilmentType.Soaked;
    public bool IsMudCaked => Ailment == AilmentType.MudCaked;
    public bool IsVineBound => Ailment == AilmentType.VineBound;
    public bool IsInDarkness => Ailment == AilmentType.Darkness;

    // 虚脱: 使う技の一致補正(STAB)が消える。解除は「自分の2回目の行動終了時」
    // で、サイクル終了(AdvanceTurn)では刻まない。行動の開始時点で虚脱だった
    // 行動だけを数える（自分の行動中に虚脱になった場合、その行動は数えない）。
    public bool IsHollow => Ailment == AilmentType.Hollow;
    private int _hollowActionsElapsed;
    private bool _hollowAtActionStart;

    // 虚脱になった瞬間に呼ばれる（蓄積の発症・確定付与のどちらも）。
    // ログは呼び出し側(Entity)が持つので、通知だけ出す。
    public event System.Action HollowApplied;

    public void NotifyOwnActionStarting()
    {
        _hollowAtActionStart = IsHollow;

        // 行動の開始時点でかかっていた制限だけを、この行動の終了で数える
        // （自分の行動中に付いた制限は、その行動の終了を数えない）。
        _restrictionsAtActionStart.Clear();
        foreach (var kind in _restrictions.Keys)
            if (IsActionCounted(kind)) _restrictionsAtActionStart.Add(kind);
    }

    // 行動が実行されて終わるたびに呼ぶ。2回目で虚脱を解除して true を返す。
    public bool NotifyOwnActionEnded()
    {
        TickActionRestrictions();

        bool counted = _hollowAtActionStart && IsHollow;
        _hollowAtActionStart = false;
        if (!counted) return false;

        _hollowActionsElapsed++;
        if (_hollowActionsElapsed < 2) return false;

        ClearAilment();
        return true;
    }


    // ---- 制限 (Phase 6: 制限技12種) ----
    // 状態異常スロットとは独立。複数の種類が同時にかかり、同じ種類を重ねると
    // 残り回数を上書きする（延長しない）。値は残り回数。
    //  - 行動で減る: SealLastMove / NoStatusMoves / Encore / RankUpBlock / AdjacentOnly / NoRepeat
    //    （対象自身の行動が実行されて終わるたびに -1。NotifyOwnActionEnded）
    //  - サイクルで減る: ItemJam / HealBlock（AdvanceTurn。対戦はサイクル終了時）
    //  - 次の優先度計算で使い切り: PriorityDown（BattleScheduler.ResolveTurn が消費）
    //  - 即時効果の Pull / PpDrain は状態を持たない（AttackAction が直接処理する）
    private readonly Dictionary<RestrictionKind, int> _restrictions = new();
    private readonly HashSet<RestrictionKind> _restrictionsAtActionStart = new();
    private int _sealedSlot = -1;
    private int _encoreSlot = -1;

    public static string RestrictionLabel(RestrictionKind kind) => kind switch
    {
        RestrictionKind.SealLastMove => "技封じ",
        RestrictionKind.NoStatusMoves => "挑発",
        RestrictionKind.Encore => "アンコール",
        RestrictionKind.ItemJam => "持ち物無効",
        RestrictionKind.PriorityDown => "優先度低下",
        RestrictionKind.HealBlock => "回復封じ",
        RestrictionKind.RankUpBlock => "ランク上昇封じ",
        RestrictionKind.AdjacentOnly => "射程制限",
        RestrictionKind.NoRepeat => "連続使用禁止",
        _ => kind.ToString(),
    };

    // 制限が解けたとき（ログは呼び出し側 Entity が持つ）。
    public event System.Action<RestrictionKind> RestrictionExpired;

    private static bool IsActionCounted(RestrictionKind kind) => kind is
        RestrictionKind.SealLastMove or RestrictionKind.NoStatusMoves or RestrictionKind.Encore
        or RestrictionKind.RankUpBlock or RestrictionKind.AdjacentOnly or RestrictionKind.NoRepeat;

    private static bool IsCycleCounted(RestrictionKind kind) =>
        kind is RestrictionKind.ItemJam or RestrictionKind.HealBlock;

    public bool HasRestriction(RestrictionKind kind) => _restrictions.ContainsKey(kind);
    public int RestrictionRemaining(RestrictionKind kind) => _restrictions.GetValueOrDefault(kind);

    public bool IsItemJammed => HasRestriction(RestrictionKind.ItemJam);
    public bool IsHealBlocked => HasRestriction(RestrictionKind.HealBlock);
    public bool BlocksRankUp => HasRestriction(RestrictionKind.RankUpBlock);

    // 次の行動の実効優先度から引く値（フロストラグ）。
    public int PriorityPenalty => HasRestriction(RestrictionKind.PriorityDown) ? 1 : 0;

    // アンコールで強制される技枠。かかっていなければ -1。
    public int EncoreSlot => HasRestriction(RestrictionKind.Encore) ? _encoreSlot : -1;

    // 制限を付ける。付いたら true。SealLastMove / Encore は「最後に使った技」が
    // 前提なので、未使用（lastUsedSlot < 0）なら効果なし。Pull / PpDrain は即時効果
    // なので状態を持たず常に false。
    public bool TryApplyRestriction(RestrictionKind kind, int turns, int lastUsedSlot)
    {
        switch (kind)
        {
            case RestrictionKind.None:
            case RestrictionKind.Pull:
            case RestrictionKind.PpDrain:
                return false;
            case RestrictionKind.SealLastMove:
                if (lastUsedSlot < 0) return false;
                _sealedSlot = lastUsedSlot;
                break;
            case RestrictionKind.Encore:
                if (lastUsedSlot < 0) return false;
                _encoreSlot = lastUsedSlot;
                break;
        }
        _restrictions[kind] = System.Math.Max(1, turns);   // 重ねると上書き（延長しない）
        return true;
    }

    // この技枠・この技をいま出せない理由。出せるなら null。
    // エンジン(AttackAction)・NPC・対戦HUDの3か所が同じこの1箇所を見る。
    public string RestrictionBlocks(Entity self, int slot, MoveData move)
    {
        if (move == null) return null;

        if (HasRestriction(RestrictionKind.SealLastMove) && slot >= 0 && slot == _sealedSlot)
            return "技封じ";
        if (HasRestriction(RestrictionKind.NoStatusMoves) && move.Category == MoveCategory.Status)
            return "挑発";
        if (HasRestriction(RestrictionKind.AdjacentOnly) && move.Range != MoveRange.Adjacent)
            return "射程制限";
        if (HasRestriction(RestrictionKind.NoRepeat) && slot >= 0 && self != null && slot == self.LastUsedMoveSlot)
            return "連続使用禁止";
        return null;
    }

    // 次の行動の優先度に使われた分を消費する（BattleScheduler.ResolveTurn、
    // 行動順を決めた直後に呼ぶ）。
    public bool ConsumePriorityDown()
    {
        if (!_restrictions.Remove(RestrictionKind.PriorityDown)) return false;
        RestrictionExpired?.Invoke(RestrictionKind.PriorityDown);
        return true;
    }

    private void TickActionRestrictions()
    {
        if (_restrictionsAtActionStart.Count == 0) return;
        foreach (var kind in new List<RestrictionKind>(_restrictionsAtActionStart))
        {
            if (!_restrictions.TryGetValue(kind, out int left)) continue;
            left--;
            if (left > 0) { _restrictions[kind] = left; continue; }
            _restrictions.Remove(kind);
            RestrictionExpired?.Invoke(kind);
        }
        _restrictionsAtActionStart.Clear();
    }

    private void TickCycleRestrictions()
    {
        if (_restrictions.Count == 0) return;
        foreach (var kind in new List<RestrictionKind>(_restrictions.Keys))
        {
            if (!IsCycleCounted(kind)) continue;
            int left = _restrictions[kind] - 1;
            if (left > 0) { _restrictions[kind] = left; continue; }
            _restrictions.Remove(kind);
            RestrictionExpired?.Invoke(kind);
        }
    }

    // ---- trait_catalog_v2 stage 1 traits ----

    // バッテリー: permanently occupies the Ailment slot with Paralyze (帯電)
    // via the ordinary mutual-exclusion rule, so no other primary ailment
    // can ever be applied - but Paralyze's own effect (movement lock, see
    // IsMovementLocked above) is suppressed for this holder, and its
    // AdvanceTurn clear-countdown never fires (see the Paralyze case
    // below), so the occupation never lapses. Called once at spawn
    // (Entity._Ready) and re-asserted by Reset() (floor transitions), since
    // Reset() would otherwise wipe the occupying Ailment.
    private bool _isBatteryHolder;

    public void MarkAsBattery()
    {
        _isBatteryHolder = true;
        TryApplyAilment(AilmentType.Paralyze);
    }

    // きぬぬい: armed after this entity uses an Ice-type move (see
    // AttackAction.Execute), consumed on the next hit THIS entity takes
    // (see AttackAction.StrikeTarget) for a one-time 10% damage reduction.
    private bool _damageReductionArmed;

    public void ArmDamageReduction() => _damageReductionArmed = true;

    public bool ConsumeDamageReductionIfArmed()
    {
        if (!_damageReductionArmed) return false;
        _damageReductionArmed = false;
        return true;
    }

    // ディープダイブ (stage 9 §1.9): armed when this entity nullifies an
    // incoming Water move, consumed by the NEXT Water move they use for
    // +25 power. Same one-shot arm/consume shape as きぬぬい above - a
    // second nullification before spending it simply re-arms the same
    // single charge rather than stacking.
    private bool _deepDiveCharged;

    // ブリッツビート's evasion-drop immunity, and オーバーヒール's heal
    // top-up rate. Both are set once from the species trait at spawn
    // (Entity._Ready) rather than looked up per event - the same posture
    // MarkAsBattery already takes for a permanent trait-driven flag.
    private bool _blocksEvasionDrop;
    private float _healBonusRate;

    public void MarkBlocksEvasionDrop() => _blocksEvasionDrop = true;
    public void MarkHealBonusRate(float rate) => _healBonusRate = rate;

    // オーバーヒール (stage 9 §1.9): extra HP on top of any move-driven
    // heal, as a fraction of MaxHp. Returns the ADDITIONAL amount only -
    // the caller has already applied the base heal.
    public int GetHealBonus(int maxHp) =>
        _healBonusRate <= 0f ? 0 : Mathf.FloorToInt(maxHp * _healBonusRate);

    public void ArmDeepDiveCharge() => _deepDiveCharged = true;

    // ビルドアップ (stage 9 §1): -25% damage taken, scoped to ONE action of
    // the recipient rather than to a turn count. Deliberately NOT reusing
    // the 期間限定ランクバフ countdown (攻守一体) - that expires on
    // action-CYCLES elapsed, whereas this expires on the recipient's single
    // action completing, so the two have genuinely different lifetimes and
    // sharing one mechanism would misrepresent both. Set and cleared by the
    // turn loop around the Execute call (see BuildUpRelay).
    public bool HasBuildUpShield { get; private set; }

    public void SetBuildUpShield(bool active) => HasBuildUpShield = active;

    // もうどくのきり (trap-move kit): the mist applies Toxic while stood in
    // and clears it on leaving. The flag records that THIS Toxic came from
    // the mist - without it, walking off a mist tile would also cure a
    // Toxic inflicted by a move, which the field has no business undoing.
    private bool _toxicFromMist;

    public void ApplyMistToxic()
    {
        if (Ailment == AilmentType.Toxic) return; // already toxic: nothing to re-apply
        if (TryApplyAilment(AilmentType.Toxic)) _toxicFromMist = true;
    }

    public void ClearMistToxicIfAny()
    {
        if (!_toxicFromMist) return;
        _toxicFromMist = false;
        ClearAilmentIfType(AilmentType.Toxic);
    }

    public bool ConsumeDeepDiveChargeIfArmed()
    {
        if (!_deepDiveCharged) return false;
        _deepDiveCharged = false;
        _toxicFromMist = false;
        return true;
    }

    // Re-applying while already afflicted with one of the 5 is a no-op
    // (confirmed: re-poisoning a Poisoned target does NOT escalate it to
    // Toxic) - Stun always succeeds regardless, since it doesn't touch
    // the Ailment slot at all.
    public bool TryApplyAilment(AilmentType type)
    {
        if (type == AilmentType.None) return false;

        if (type == AilmentType.Stun)
        {
            IsStunned = true;
            return true;
        }

        if (Ailment != AilmentType.None) return false;

        Ailment = type;
        _ailmentTurnsElapsed = 0;
        _toxicStacks = type == AilmentType.Toxic ? 1 : 0;
        if (type == AilmentType.Hollow)
        {
            _hollowActionsElapsed = 0;
            HollowApplied?.Invoke();
        }
        return true;
    }

    private void ClearAilment()
    {
        Ailment = AilmentType.None;
        _ailmentTurnsElapsed = 0;
        _hollowActionsElapsed = 0;
        _toxicStacks = 0;
    }

    // Floor-transition reset. Allies/enemies get this "for free" every
    // floor (FloorController.CleanupCurrentFloor QueueFrees their nodes
    // and SpawnPartyMembers/SpawnEnemyAt build fresh StatusEffectManagers
    // via Entity._Ready) - the Player's node is the one exception (it
    // survives every floor transition, unlike everything else), so
    // FloorController calls this explicitly on the player to keep battle
    // effects floor-local for everyone uniformly.
    public void Reset()
    {
        AtkRank = 0;
        DefRank = 0;
        AccuracyRank = 0;
        EvasionRank = 0;
        ElementPowerElement = null;
        ElementPowerRank = 0;
        CritRank = 0;
        _turnsSinceLastDamageEvent = 0;
        _tempAtkRankBonus = 0;
        _tempDefRankBonus = 0;
        _tempRankTurnsRemaining = 0;
        Ailment = AilmentType.None;
        _ailmentTurnsElapsed = 0;
        _hollowActionsElapsed = 0;
        _hollowAtActionStart = false;
        _restrictions.Clear();
        _restrictionsAtActionStart.Clear();
        _sealedSlot = -1;
        _encoreSlot = -1;
        _toxicStacks = 0;
        IsStunned = false;
        _accumulation.Clear();
        _damageReductionArmed = false;
        _deepDiveCharged = false;

        // バッテリー's permanent occupation must survive a floor-transition
        // Reset() (the player is the one entity that persists across
        // floors and calls this explicitly) - re-assert it right after
        // clearing, same as the initial MarkAsBattery() call.
        if (_isBatteryHolder) TryApplyAilment(AilmentType.Paralyze);
    }

    // Before-action hook (Freeze/Stun only - Paralyze never reaches this,
    // it only gates movement, see IsMovementLocked). Returns true = this
    // action-cycle is skipped entirely (no DecideAction/Execute call).
    //
    // Freeze: 25% break chance each cycle, PLUS a guaranteed break once
    // 3 cycles have been checked (so at most 3 skipped cycles ever) -
    // both checks happen "before acting", so a cycle that breaks free
    // (via either the roll or the turn-3 guarantee) acts normally that
    // same cycle.
    // sunny: はれ (weather) melts ice on the very first check, so a freeze
    // applied under the sun costs at most nothing - the entity acts that
    // same cycle. Passed in by the caller rather than read here because
    // this class deliberately owns no grid/floor reference (same reason
    // 潜航's terrain check lives in Entity.ResolveStatusTick).
    public bool TryConsumeActionLock(bool sunny = false)
    {
        if (IsStunned)
        {
            IsStunned = false; // single-use: consumed by skipping this one action
            return true;
        }

        if (Ailment == AilmentType.Freeze)
        {
            if (sunny)
            {
                ClearAilment();
                return false;
            }

            _ailmentTurnsElapsed++;
            bool breaksFree = _ailmentTurnsElapsed >= 3 || GD.Randf() < 0.25f;
            if (breaksFree)
            {
                ClearAilment();
                return false;
            }
            return true;
        }

        return false;
    }

    // After-action hook: called once per action-cycle for EVERY entity,
    // whether or not TryConsumeActionLock skipped that cycle (a Stunned-
    // and-Poisoned entity still takes poison damage on the cycle it
    // couldn't act - only Freeze/Stun themselves have no DoT of their
    // own, so this is never actually double-counted). Advances rank
    // decay unconditionally, then applies whichever ailment is active:
    // Poison/Toxic/Burn return raw DoT damage (the caller applies it,
    // clamped to never drop CurrentHp below 1 - see Entity.
    // ResolveStatusTick); Paralyze has no damage but advances its own
    // 20%-or-5-turn clear check here (it never goes through
    // TryConsumeActionLock, since movement being blocked doesn't stop
    // the entity from acting at all).
    public int AdvanceTurn(int maxHp)
    {
        int damage = 0;

        switch (Ailment)
        {
            case AilmentType.Poison:
                damage = Mathf.FloorToInt(maxHp / 18f) + 1;
                break;

            case AilmentType.Toxic:
                damage = Mathf.Max(0, Mathf.FloorToInt(_toxicStacks * maxHp / 18f) - 1);
                _toxicStacks++;
                break;

            case AilmentType.Burn:
                damage = Mathf.FloorToInt(maxHp / 20f) + 1;
                break;

            case AilmentType.Paralyze:
                // バッテリー holders never roll for the clear - their
                // occupation of this slot is permanent (trait_catalog_v2 §3).
                if (_isBatteryHolder) break;
                _ailmentTurnsElapsed++;
                if (_ailmentTurnsElapsed >= 5 || GD.Randf() < 0.20f) ClearAilment();
                break;

            // Soaked: no DoT, no action lock - just a turn-gated probabilistic
            // clear. Turns 1-2 never roll; from turn 3 onward, 66% per
            // action-end (§4-6).
            case AilmentType.Soaked:
                _ailmentTurnsElapsed++;
                if (_ailmentTurnsElapsed >= 3 && GD.Randf() < 0.66f) ClearAilment();
                break;

            // MudCaked: no DoT, no action lock - deterministic clear at the
            // end of the 2nd action-cycle under it (§4-6).
            case AilmentType.MudCaked:
                _ailmentTurnsElapsed++;
                if (_ailmentTurnsElapsed >= 2) ClearAilment();
                break;

            // VineBound: no DoT, no action lock - deterministic clear at the
            // end of the 4th action-cycle under it (§4-6).
            case AilmentType.VineBound:
                _ailmentTurnsElapsed++;
                if (_ailmentTurnsElapsed >= 4) ClearAilment();
                break;

            // Darkness has no turn-based clear at all - only AttackAction's
            // ClearAilmentIfType on a landed Special-category hit (§4-6).
        }

        TickCycleRestrictions();
        AdvanceRankDecay();
        return damage;
    }
}
