using Godot;
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

    public float GetAtkMultiplier() => RankMultiplier(AtkRank);
    public float GetDefMultiplier() => RankMultiplier(DefRank);

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

    // ---- Evasion rank: 3 states (0..+3), defense-only (no "evasion
    // down" was specified - only 7/8, 6/8, 5/8 exist, all <1). Applied
    // as an extra multiplier against the ATTACKER's hit chance.
    public int EvasionRank { get; private set; }
    private const int EvasionRankMin = 0;
    private const int EvasionRankMax = 3;
    private static readonly float[] EvasionTable = { 1f, 7f / 8f, 6f / 8f, 5f / 8f }; // index = rank

    public float GetEvasionMultiplier() => EvasionTable[EvasionRank];

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

    // ---- Ailments: Poison/Toxic/Burn/Paralyze/Freeze are mutually
    // exclusive (one slot); Stun is independent and can coexist with any
    // of them (e.g. a Poisoned entity can also be Stunned).
    public AilmentType Ailment { get; private set; } = AilmentType.None;
    private int _ailmentTurnsElapsed;
    private int _toxicStacks; // "n" in the Toxic formula, only meaningful while Ailment == Toxic

    public bool IsStunned { get; private set; }

    // Paralyze blocks movement only - AttackAction/bump-attacks still
    // work (see TurnScheduler/Player's use of this).
    public bool IsMovementLocked => Ailment == AilmentType.Paralyze;

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
        return true;
    }

    private void ClearAilment()
    {
        Ailment = AilmentType.None;
        _ailmentTurnsElapsed = 0;
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
        Ailment = AilmentType.None;
        _ailmentTurnsElapsed = 0;
        _toxicStacks = 0;
        IsStunned = false;
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
    public bool TryConsumeActionLock()
    {
        if (IsStunned)
        {
            IsStunned = false; // single-use: consumed by skipping this one action
            return true;
        }

        if (Ailment == AilmentType.Freeze)
        {
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
                _ailmentTurnsElapsed++;
                if (_ailmentTurnsElapsed >= 5 || GD.Randf() < 0.20f) ClearAilment();
                break;
        }

        AdvanceRankDecay();
        return damage;
    }
}
