using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Grid;
using MysteryDungeon.Progression;

namespace MysteryDungeon.Entities;

// Shared combat/survival stats component, attached as a child node of
// every Entity (see Entity._Ready()). Attack/Defense feed AttackAction's
// damage formula (via DamageCalculator - see Phase 16); physical moves
// use Attack/Defense, special moves use SpAttack/SpDefense (HABCDS).
//
// Phase 17: Attack/Defense/MaxHp are no longer flat designer-set values.
// Each is derived on demand from a formula (species BaseXxx scaled by
// Level, then the nature, then the individual BreakthroughXxx added on
// top as a flat real-stat bonus) - see MaxHp/Attack/Defense below. A concrete Entity subclass (Player/BossEntity/etc.)
// configures BaseMaxHp/BaseAtk/BaseDef/Level in its own _Ready() instead
// of assigning the real stats directly.
public partial class EntityStats : Node
{
    private int _level = 1;
    private int _baseMaxHp = 70;
    private int _breakthroughHp = 0;

    // Level and the two HP-affecting base/breakthrough fields all feed
    // MaxHp, so each setter re-syncs CurrentHp by the same delta the
    // change just applied to MaxHp (see ApplyMaxHpAffectingChange) -
    // this is what keeps CurrentHp valid across Godot's object-creation
    // order (an Entity's _Ready() only sets these AFTER EntityStats'
    // own _Ready() already computed an initial CurrentHp from the
    // pre-override defaults) and across post-spawn rescaling (e.g.
    // FloorController's per-floor Level bump on an already-spawned
    // enemy).
    [Export]
    public int Level
    {
        get => _level;
        set => ApplyMaxHpAffectingChange(ref _level, value);
    }

    [Export]
    public int BaseMaxHp
    {
        get => _baseMaxHp;
        set => ApplyMaxHpAffectingChange(ref _baseMaxHp, value);
    }

    [Export]
    public int BreakthroughHP
    {
        get => _breakthroughHp;
        set => ApplyMaxHpAffectingChange(ref _breakthroughHp, value);
    }

    // Phase 20: which species these constants came from (filled by
    // FillFromSpecies). The individual-progression counterpart to
    // PartyMemberRecord.SpeciesId - together with Level/CurrentExp/
    // CurrentHp it's enough to fully reconstruct a member.
    public string SpeciesId { get; set; } = "";

    // trait_catalog_v2: which Data/traits.json entry this species has
    // (single slot, filled by FillFromSpecies). "" = unassigned (stage 9
    // hasn't run the 287-species assignment yet - see SpeciesData.Trait).
    public string Trait { get; set; } = "";

    // 2つめの特性(secondary の特性だけが入る。空 = 持たない)。
    public string Trait2 { get; set; } = "";

    public bool HasTrait(string id) => !string.IsNullOrEmpty(id) && (Trait == id || Trait2 == id);

    // trait_catalog_v2: the INDEPENDENT ecology slot (Data/ecology.json).
    // Separate from Trait on purpose - a species can hold both at once.
    // "" = unassigned (same stage-9 caveat as Trait above).
    public string Ecology { get; set; } = "";

    [Export] public int BaseAtk { get; set; } = 70;
    [Export] public int BaseDef { get; set; } = 70;

    // HABCDS の残り3能力の種族値（魔力 C / 精神 D / 俊敏 S）。
    // C/D は特殊技のダメージ計算（AttackAction）、S は対戦の行動順
    // （BattleScheduler.Order）に使う。
    [Export] public int BaseSpAtk { get; set; } = 70;
    [Export] public int BaseSpDef { get; set; } = 70;
    [Export] public int BaseSpeed { get; set; } = 70;

    // 6能力の種族値合計（BST6）。合計種族値の唯一の定義。
    public int BaseTotal => BaseMaxHp + BaseAtk + BaseDef + BaseSpAtk + BaseSpDef + BaseSpeed;
    // 突破値（Breakthrough）。構築で振る「実数値への直接加算」で、気質補正のあとに足す。
    // 各0〜25・6能力合計55以下は構築側（StatSpread.Validate）で検証する。
    // 迷宮側は常に 0・無補正なので、実数値は従来と一致する。
    [Export] public int BreakthroughAtk { get; set; } = 0;
    [Export] public int BreakthroughDef { get; set; } = 0;
    [Export] public int BreakthroughSpAtk { get; set; } = 0;
    [Export] public int BreakthroughSpDef { get; set; } = 0;
    [Export] public int BreakthroughSpeed { get; set; } = 0;

    // 気質。HP 以外の5能力のうち上昇側 ×1.1・下降側 ×0.9。None/None か同じ能力は無補正。
    public NatureStat NatureUp { get; set; } = NatureStat.None;
    public NatureStat NatureDown { get; set; } = NatureStat.None;

    private int NatureMul10(NatureStat stat)
    {
        if (NatureUp == NatureDown || stat == NatureStat.None) return 10;
        if (NatureUp == stat) return 11;
        if (NatureDown == stat) return 9;
        return 10;
    }

    // Real (calculated) stats. 素の値(Base*2*Lv/100+5) -> 気質(*11/10, *9/10, 整数切り捨て)
    // -> 突破値を加算。HP は気質なし。Read-only: set BaseXxx/BreakthroughXxx/Level
    // instead, never these directly.
    public int MaxHp => BaseMaxHp * 2 * Level / 100 + Level + 10 + BreakthroughHP;
    public int Attack => StatSpread.RealStat(BaseAtk, Level, BreakthroughAtk, NatureMul10(NatureStat.Atk));
    public int Defense => StatSpread.RealStat(BaseDef, Level, BreakthroughDef, NatureMul10(NatureStat.Def));
    public int SpAttack => StatSpread.RealStat(BaseSpAtk, Level, BreakthroughSpAtk, NatureMul10(NatureStat.SpAtk));
    public int SpDefense => StatSpread.RealStat(BaseSpDef, Level, BreakthroughSpDef, NatureMul10(NatureStat.SpDef));
    public int Speed => StatSpread.RealStat(BaseSpeed, Level, BreakthroughSpeed, NatureMul10(NatureStat.Speed));

    // 構築の突破値・気質を写す。HP は ApplyMaxHpAffectingChange を通すプロパティ経由で
    // 代入するので、CurrentHp は満タンのまま。
    public void ApplySpread(StatSpread s)
    {
        s ??= StatSpread.Zero();
        BreakthroughHP = s.Hp;
        BreakthroughAtk = s.Atk;
        BreakthroughDef = s.Def;
        BreakthroughSpAtk = s.SpAtk;
        BreakthroughSpDef = s.SpDef;
        BreakthroughSpeed = s.Speed;
        NatureUp = s.NatureUp;
        NatureDown = s.NatureDown;
    }

    public int CurrentHp { get; set; }

    // ---- Phase 18-A: EXP/level-up data ----
    // Consumed by ExperienceSystem (defeat -> distribution -> threshold
    // loop); this component only stores the numbers. The old
    // Exp/ExpToNextLevel/AddExp/LevelUp machinery is fully retired.
    public const int LevelCap = 100;

    // Lifetime cumulative EXP on the cubic curve (see ExpCurve). long
    // per spec: Lv100 alone needs 1,000,000 and future yield multipliers
    // must never overflow.
    //
    // Lazily seeded to TotalExpForLevel(Level) on first read - the
    // spec's "initialize to the spawn level's cumulative EXP" rule.
    // Eager init can't work here: an Entity subclass assigns its spawn
    // Level in its own _Ready() AFTER this component's _Ready() already
    // ran, and FloorController bumps enemy Level even later (per-floor
    // scaling, post-AddChild). Nothing reads CurrentExp until combat is
    // underway, so first-read is always after the spawn level settled.
    private long? _currentExp;
    public long CurrentExp
    {
        get => _currentExp ??= ExpCurve.TotalExpForLevel(Level);
        set => _currentExp = value;
    }

    // Species value: how much EXP defeating THIS entity awards, before
    // victim-level scaling (Gained = floor(BaseExpYield * Level / K),
    // K = 10). 55 is the confirmed "standard enemy" baseline (r = 5.5:
    // at Lv10, ~6 same-level standard kills = 1 level).
    [Export] public int BaseExpYield { get; set; } = 55;

    // Up to two types; Type2 empty means single-typed.
    [Export] public string Type1 { get; set; } = "Neutral";
    [Export] public string Type2 { get; set; } = "";

    // Terrain-traversal trait ("Pal skill"): Hover/Glide lets this
    // entity stand on any hazard tile, on top of whatever its Type1/
    // Type2 already grants (see GetMovementProfile/CanTraverse).
    [Export] public PartnerSkill PartnerSkill { get; set; } = PartnerSkill.None;

    // Hunger - meaningful for Player only. FloorController calls
    // TickBelly() once per turn on the player's Stats; NPCs simply never
    // have this called, so their Belly just sits unused at MaxBelly.
    [Export] public int MaxBelly { get; set; } = 100;
    public int Belly { get; set; }

    public bool IsAlive => CurrentHp > 0;

    // Which terrain hazards this entity can stand on. Hover/Glide (from
    // PartnerSkill) takes priority since it covers everything at once;
    // otherwise Type1/Type2 grants the matching single-hazard immunity.
    // Resolved as a UNION of every source (PartnerSkill, Ecology, Type)
    // rather than a first-match-wins ladder - trait_catalog_v2 stage 4
    // added Ecology as a third source, and the old ladder would have let
    // whichever source came first mask the others (a 放熱器官 Water-type
    // would have kept only one of its two immunities).
    //
    // Routed through the profile - NOT bolted onto CanTraverse below -
    // because AStarPathfinder keys its per-profile grid cache off this
    // exact value. Granting traversal anywhere else would let manual
    // movement onto a tile that pathfinding still refuses to route
    // through, which is precisely the drift TerrainTraversalRules exists
    // to prevent.
    public MovementProfile GetMovementProfile()
    {
        // Chasm is all-or-nothing and implies the other two, so any source
        // granting it settles the answer outright.
        if (PartnerSkill is PartnerSkill.Hover or PartnerSkill.Glide) return MovementProfile.Hover;

        var ecology = EcologyDatabase.Get(Ecology);
        bool ecologyChasm = ecology != null && ecology.Hooks.Contains("chasm_walk");
        if (ecologyChasm) return MovementProfile.Hover;

        bool lava = Type1 == "Fire" || Type2 == "Fire"
            || (ecology != null && ecology.Hooks.Contains("lava_walk"));
        bool water = Type1 == "Water" || Type2 == "Water" || Type1 == "Ice" || Type2 == "Ice"
            || (ecology != null && ecology.Hooks.Contains("water_walk"));

        if (lava && water) return MovementProfile.FireWaterImmune;
        if (lava) return MovementProfile.FireImmune;
        if (water) return MovementProfile.WaterIceImmune;
        return MovementProfile.Normal;
    }

    // Does this entity's ecology declare `hook`? The single read point for
    // the non-movement ecology hooks (see Entity.ResolveStatusTick and
    // FloorController's stairs sense) - movement hooks go through
    // GetMovementProfile above instead.
    public bool HasEcologyHook(string hook)
    {
        var ecology = EcologyDatabase.Get(Ecology);
        return ecology != null && ecology.Hooks.Contains(hook);
    }

    public bool CanTraverse(TerrainType terrain) => TerrainTraversalRules.IsWalkable(terrain, GetMovementProfile());

    public override void _Ready()
    {
        CurrentHp = MaxHp;
        Belly = MaxBelly;
    }

    public void TakeDamage(int amount)
    {
        CurrentHp = Mathf.Max(0, CurrentHp - amount);
    }

    // スコーチスモッグ(HealBlock): かかっている間は HP が回復しない。ドレイン・リジェネ・
    // 天候・はなばたけ・トニック・闇のちから・道具の回復は全て Heal() を通るので、
    // ここ1箇所で止まる。HealToFull（全回復）は対象外。Entity が StatusEffects を結ぶ。
    public System.Func<bool> HealBlockedCheck { get; set; }

    public void Heal(int amount)
    {
        if (HealBlockedCheck != null && HealBlockedCheck()) return;
        CurrentHp = Mathf.Min(MaxHp, CurrentHp + amount);
    }

    public void HealToFull() => CurrentHp = MaxHp;

    // ---- Phase 19: persistence seams ----
    // The only two entry points PartyState's hydrate/dehydrate go
    // through, so the persistence layer never touches internal fields
    // or has to know about setter side effects.

    // Phase 20: fill the species-constant stat block from the DB. Only
    // touches species constants (Base*/BaseExpYield/Types) - never the
    // individual progression (Level/CurrentExp/CurrentHp/Breakthrough*),
    // which stays with the entity/record. Called from Entity._Ready
    // right after Stats is created, before the entity applies its own
    // spawn Level, so the Level setter's CurrentHp sync still lands last.
    public void FillFromSpecies(Species.SpeciesData species)
    {
        BaseMaxHp = species.BaseHP;
        BaseAtk = species.BaseAtk;
        BaseDef = species.BaseDef;
        BaseSpAtk = species.BaseSpAtk;
        BaseSpDef = species.BaseSpDef;
        BaseSpeed = species.BaseSpeed;
        BaseExpYield = species.BaseExpYield;
        Type1 = species.Types.Count > 0 ? species.Types[0].ToString() : "Neutral";
        Type2 = species.Types.Count > 1 ? species.Types[1].ToString() : "";
        Trait = species.Trait;
        Trait2 = species.Trait2 ?? "";
        Ecology = species.Ecology;
    }

    public (int Level, long CurrentExp, int CurrentHp) CapturePersistedState() => (Level, CurrentExp, CurrentHp);

    // Restore order is load-bearing:
    // 1. Level first - runs the Phase 17 setter (HAD recompute + the
    //    CurrentHp diff shift). Whatever the diff mechanism does to
    //    CurrentHp here is deliberately DISCARDED by step 3: the diff
    //    exists to preserve relative damage across level-up increments,
    //    but a hydrate restores an absolute recorded HP, so relative
    //    adjustment would be wrong. Not a bug - intended.
    // 2. CurrentExp next - assigning through the setter fills the
    //    nullable backing field, which is exactly what disarms the
    //    lazy first-read seed (it only fires while the backing field
    //    is still null). Skipping this would let the next read clobber
    //    the carried-over EXP with TotalExpForLevel(Level).
    // 3. CurrentHp last, as an absolute value clamped to [1, MaxHp] -
    //    MaxHp is only correct once step 1 settled the Level. Lower
    //    bound 1, not 0: a hydrated member is by definition alive
    //    (downed records never hydrate - see PartyState.TryHydrate),
    //    so a 0 here would spawn an instantly-dead ghost.
    public void ApplyPersistedState(int level, long currentExp, int currentHp)
    {
        Level = level;
        CurrentExp = currentExp;
        CurrentHp = Mathf.Clamp(currentHp, 1, MaxHp);
    }

    // Whenever Level/BaseMaxHp/BreakthroughHP changes, MaxHp is
    // recalculated - shift CurrentHp by exactly the same delta so an
    // already-full entity stays full (a spawned-then-rescaled enemy, see
    // FloorController) and an already-damaged entity keeps the same
    // *missing* HP, then clamp into [0, MaxHp] as a final safety net
    // (also covers construction-time transients, before _Ready's own
    // CurrentHp = MaxHp sync runs).
    private void ApplyMaxHpAffectingChange(ref int field, int value)
    {
        if (field == value) return;
        int oldMaxHp = MaxHp;
        field = value;
        int delta = MaxHp - oldMaxHp;
        CurrentHp = Mathf.Clamp(CurrentHp + delta, 0, MaxHp);
    }

    // Decrements hunger; once it hits empty, deals 1 starvation damage
    // per turn instead of decrementing further.
    public void TickBelly()
    {
        if (Belly > 0)
        {
            Belly--;
            GD.Print($"[Belly] Belly: {Belly}/{MaxBelly}");
        }
        else
        {
            TakeDamage(1);
            GD.Print($"[Belly] Starving! Took 1 damage. HP: {CurrentHp}/{MaxHp}");
        }
    }

}
