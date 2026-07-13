namespace MysteryDungeon.Combat;

public enum MoveCategory
{
    Physical,
    Special,
    Status,
}

// Not yet consumed by AttackAction's simplified damage formula - reserved
// for the real targeting/AoE logic a future phase will add.
public enum MoveRange
{
    Adjacent,
    Line,
    Room,
}

// Phase 21: which rank track a move's RankEffect touches. ElementPower
// targets whichever Element the move itself is (move.Type) - no separate
// "which element" field, so a Fire-typed move that boosts elemental power
// always boosts Fire.
public enum RankStat
{
    None,
    Atk,
    Def,
    Accuracy,
    Evasion,
    ElementPower,
}

// Phase 21: who a RankEffect/AilmentEffect lands on.
public enum StatusTarget
{
    Self,
    Enemy,
}

// Phase 21: the 5 mutually-exclusive primary ailments (Poison/Toxic/
// Burn/Paralyze/Freeze - see StatusEffectManager) plus Stun, which is
// independent and does NOT compete with the other 5.
public enum AilmentType
{
    None,
    Poison,
    Toxic,
    Burn,
    Paralyze,
    Freeze,
    Stun,
}

// Immutable move definition, loaded once by MoveDatabase from
// Data/moves.json. Runtime state (current PP) lives in MoveSlot instead.
public class MoveData
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }
    public MoveCategory Category { get; set; }
    public int Power { get; set; }
    public int Accuracy { get; set; }
    public int MaxPp { get; set; }
    public MoveRange Range { get; set; }

    // Phase 21: rank-change effect (buff/debuff of Atk/Def/Accuracy/
    // Evasion/ElementPower). None of the existing 97 moves use this yet
    // (RankEffectStat defaults to None = no effect) - this is the data
    // seam future move-database growth (the "300 moves" plan) writes
    // buff/debuff moves into.
    public RankStat RankEffectStat { get; set; } = RankStat.None;
    public int RankEffectDelta { get; set; }
    public StatusTarget RankEffectTarget { get; set; } = StatusTarget.Self;

    // Phase 21: ailment-inflicting effect. poison_fog is the only
    // existing move that sets this (Poison/100%/Enemy); everything else
    // defaults to None. A damaging move can ALSO carry an AilmentEffect
    // (a "10% chance to poison" secondary effect) - AttackAction applies
    // it after damage resolves in that case.
    public AilmentType AilmentEffect { get; set; } = AilmentType.None;
    public int AilmentChance { get; set; } = 100;
    public StatusTarget AilmentTarget { get; set; } = StatusTarget.Enemy;

    // Phase 21: Burn's "接触技の与ダメージ*0.5" check reads this -
    // defaults false, so none of the existing 97 (all ranged) moves are
    // affected.
    public bool IsContact { get; set; } = false;

    // Phase 21: bypasses both the attacker's accuracy roll and the
    // defender's evasion rank entirely ("必中"). Defaults false.
    public bool IsGuaranteedHit { get; set; } = false;
}
