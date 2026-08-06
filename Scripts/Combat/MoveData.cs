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
// key off (斬る系/ツメ・こぶし系). All four groups are populated in
// moves.json, each by a name-based identification rule.
public enum WeaponTag
{
    None,
    Slash,    // 斬る系 - names containing 斬/ブレード (11 moves)

    // Was ClawFist (ツメ・こぶし系). Renamed to Fist when the move-family
    // rules landed: the tag now covers BOTH the フィスト family and the
    // ブロー family, which are capped separately in data (8 and 6) but
    // share this single tag.
    Fist,

    // The remaining move families, each identified by the suffix of the
    // move's name and each carrying its own rule (see moves.json and
    // Tools/apply_move_families.py):
    //   Strike  ストライク - cap 10, power <= 80
    //   Punch   パンチ     - cap 2 per element, power <= 70, always has a
    //                        secondary effect
    //   Thrust  スラスト   - non-contact, always TwoTile range
    //   Crush   クラッシュ - cap 5, knocks the target back (AttackAction)
    //   Rend    レンド     - cap 3, clears the field/trap under the user
    //   Flash   フラッシュ - cap 2, non-contact, never misses, always crits,
    //                        power fixed at 50
    Strike,
    Punch,
    Thrust,
    Crush,
    Rend,
    Flash,

    // ブレス/息系 - stage 9 §1.5's 発煙器官 keys off this: the 5 moves whose
    // names contain "ブレス" or "息" (Ice x2, Dragon x2, Fire x1).
    Breath,

    // 風系 - the weather system's きょうふう makes these unmissable.
    // WeaponTag is single-valued, so a move tagged Wind cannot ALSO be
    // Slash: エアーブレード and れっぷうざん were Slash and are now Wind,
    // which is the accepted cost of expressing wind on this field.
    Wind,
}

// Multi-hit moves. Two shapes were specified and both exist because they
// fail differently, which is the point of having both:
//
//   Variable2To5  - ONE accuracy roll for the whole move; if it lands, the
//                   hit count is rolled 2/3/4/5 at 1/3, 1/3, 1/6, 1/6. All
//                   or nothing on accuracy, variable on output.
//   RepeatPerHit  - accuracy is re-rolled for EVERY hit, so a "3-hit" move
//                   routinely lands 2. Fixed count, variable connection.
//
// MoveData.Power is the PER-HIT power in both shapes, not the total.
public enum MultiHitMode
{
    None,
    Variable2To5,
    RepeatPerHit,
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

// One rank change a move applies. A move used to be able to declare
// exactly one; the status-move expansion needs up to three on a single
// move ("2つのランクを2段階上げ、防御ランクを2段階下げる"), so the single
// slot became a list. MoveData keeps BOTH shapes: the legacy scalar
// properties are still the authored form for the 592 moves that only
// need one, and MoveDatabase normalises either form into RankEffects,
// which is what AttackAction actually reads.
public class RankEffect
{
    public RankStat Stat { get; set; } = RankStat.None;
    public int Delta { get; set; }
    public StatusTarget Target { get; set; } = StatusTarget.Self;
    public float Chance { get; set; } = 1.0f;

    public bool IsActive => Stat != RankStat.None && Delta != 0;
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

    // The normalised view every consumer reads: either the single legacy
    // slot above wrapped in a 1-element list, or the move's own
    // "rank_effects" array. Empty when the move changes no rank at all.
    // Built by MoveDatabase; never null.
    public System.Collections.Generic.IReadOnlyList<RankEffect> RankEffects { get; set; }
        = System.Array.Empty<RankEffect>();

    // True when nothing this move does needs a target: every rank change
    // is Self-directed and it inflicts no ailment. Such a move must still
    // resolve when no enemy is adjacent - otherwise a pure self-buff could
    // only ever be used while already standing next to something, which is
    // exactly backwards (see AttackAction.ExecuteSingle).
    public bool IsSelfContained
    {
        get
        {
            if (AilmentEffect != AilmentType.None) return false;
            foreach (var e in RankEffects)
                if (e.IsActive && e.Target != StatusTarget.Self) return false;
            return true;
        }
    }

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

    // The フラッシュ family's "必ず急所に当たる": forces the crit roll to
    // land instead of nudging its odds. Deliberately still loses to a
    // defender that is declared un-crittable by a trait or item
    // (たかねのはな), which is the stated carve-out.
    public bool GuaranteedCrit { get; set; } = false;

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

    // Self-destruct (メガトンじばく): the user faints after the move fully
    // resolves, whether or not it connected. Default false.
    public bool SelfGuaranteedDeath { get; set; } = false;

    // ---- trait_catalog_v2 stage 2-b ----

    // Which weapon-group いっせん/ツメのかりうど key off (§4). None on
    // every move today - see the enum's own comment.
    public WeaponTag WeaponTag { get; set; } = WeaponTag.None;

    // ---- Trap-move kit: persistent field overlays ----

    // Which field this move lays down (Dungeon.FieldType) and how it picks
    // its tiles. Default None on every pre-existing move, so nothing else
    // changes. FieldPlacement.ClearRadiusFour is the げきりゅう case - it
    // removes fields instead of placing them, and ignores FieldEffect.
    public Dungeon.FieldType FieldEffect { get; set; } = Dungeon.FieldType.None;
    public Dungeon.FieldPlacement FieldPlacement { get; set; } = Dungeon.FieldPlacement.None;

    // Weather kit (optional; only the weather-setting moves set these).
    // Resolved at USE time like FieldEffect - the move changes the floor,
    // it does not attack, so it neither needs nor consults a target.
    // WeatherTurns is how long the change lasts before the floor falls
    // back to the dungeon's own weather (see WeatherState).
    public Dungeon.WeatherType WeatherEffect { get; set; } = Dungeon.WeatherType.None;
    public int WeatherTurns { get; set; }

    // ---- Multi-hit kit ----

    // Which multi-hit shape this move uses (None on every pre-existing move).
    // Power above is PER HIT when this is set.
    public MultiHitMode MultiHit { get; set; } = MultiHitMode.None;

    // How many hits RepeatPerHit attempts. Unused by Variable2To5, which
    // rolls its own count.
    public int MultiHitCount { get; set; } = 0;
}
