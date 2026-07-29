namespace MysteryDungeon.Combat;

public enum MoveCategory
{
    Physical,
    Special,
    Status,
}

// Not yet consumed by AttackAction's simplified damage formula - reserved
// for the real targeting/AoE logic a future phase will add. TwoTile and
// FullFloor were added with the 300-move import (data declaration only -
// an unrecognised range in JSON still falls back to Adjacent via
// MoveDatabase, matching "未実装の射程は単体扱い").
public enum MoveRange
{
    Adjacent,   // 単体: the bumped/auto-aimed single target
    Line,       // 直線: straight in the user's facing, stops at a wall
    TwoTile,    // 2マス: two tiles ahead in the facing, stops at a wall
    Area,       // 範囲: 3x3 centred on the impact tile
    Room,       // 部屋: every tile of the user's room (corridor -> single)
    FullFloor,  // フロア全体: every actor on the floor
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
    Crit,
}

// Phase 21: who a RankEffect/AilmentEffect lands on.
public enum StatusTarget
{
    Self,
    Enemy,
}

// trait_catalog_v2 §4 stage 2-b: move-group tags いっせん/ツメのかりうど
// key off (斬る系/ツメ・こぶし系). Unset (None) on every move today - no
// per-move assignment has been authored yet, same "schema now, data
// later" posture as Trait/Ecology's stage-9 species assignment.
public enum WeaponTag
{
    None,
    Slash,    // 斬る系
    ClawFist, // ツメ・こぶし系

    // ブレス/息系 - stage 9 §1.5's 発煙器官 keys off this. Unlike Slash/
    // ClawFist (still unassigned), this one IS populated in moves.json:
    // the 5 moves whose names contain "ブレス" or "息" (Ice x2, Dragon x2,
    // Fire x1), which is the identification rule the spec gives.
    Breath,
}

// Phase 21 + the accumulation-status proposal: 9 mutually-exclusive
// primary ailments sharing one slot (Poison/Toxic/Burn/Paralyze/Freeze,
// plus Soaked/MudCaked/VineBound/Darkness added for the 蓄積値1000
// system - see StatusEffectManager), plus Stun, which is independent and
// does NOT compete with the other 9. Paralyze=帯電(雷) and Freeze=凍結
// (氷) are the pre-existing elemental ailments for Electric/Ice; Soaked=
// ずぶ濡れ(水), MudCaked=泥まみれ(地), VineBound=ツタまみれ(草),
// Darkness=暗闇(闇) are new. Neutral and Dragon have no elemental ailment
// (excluded from the accumulation system entirely).
public enum AilmentType
{
    None,
    Poison,
    Toxic,
    Burn,
    Paralyze,
    Freeze,
    Stun,
    Soaked,
    MudCaked,
    VineBound,
    Darkness,
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

    // ---- 300-move import: additional per-move fields ----

    // Move-specific crit-rank bonus, ADDED to the attacker's own crit
    // rank before the crit chance is looked up (see StatusEffectManager.
    // GetCritChanceWithBonus). Consumed - a "high crit ratio" move.
    public int CritRankBonus { get; set; } = 0;

    // Probability (0.0-1.0) that this move's RankEffect fires. Phase 21
    // rank effects were always-on; this gates them (e.g. 0.2 = 20%).
    // Default 1.0 keeps every existing rank-effect move deterministic.
    public float RankEffectChance { get; set; } = 1.0f;

    // Recoil damage as a percent of the damage dealt (consumed by
    // AttackAction.ApplyRecoil), and whether the user is stunned on its
    // next turn after using this move.
    public int RecoilHpPercent { get; set; } = 0;
    public bool SelfStunNextTurn { get; set; } = false;

    // ---- 400-move import (new_moves_100) additional mechanics ----

    // Unconditional move-level damage multiplier (a power/recoil
    // tradeoff on the 竜 recoil kit - 1.25/1.5). Slots into
    // DamageCalculator's Step-4 multiplier chain as a default-1.0 factor,
    // so a move without it (the overwhelming majority) is unaffected.
    public float DragonMultiplier { get; set; } = 1.0f;

    // HP drain: the user recovers this percent of the damage dealt (50 =
    // "half of damage dealt", the DrainHalf kit). 0 = no drain. Fires
    // once on the combined damage, same contract as RecoilHpPercent.
    public int DrainHpPercent { get; set; } = 0;

    // Self-destruct (メガトン自爆): the user faints after the move fully
    // resolves, whether or not it connected. Default false.
    public bool SelfGuaranteedDeath { get; set; } = false;

    // ---- trait_catalog_v2 stage 2-b ----

    // Which weapon-group いっせん/ツメのかりうど key off (§4). None on
    // every move today - see the enum's own comment.
    public WeaponTag WeaponTag { get; set; } = WeaponTag.None;
}
