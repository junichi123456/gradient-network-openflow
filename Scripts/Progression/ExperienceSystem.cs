using Godot;
using MysteryDungeon.Dungeon;
using MysteryDungeon.Entities;
using MysteryDungeon.UI;

namespace MysteryDungeon.Progression;

// Phase 18-A coordinator: turns "an enemy was defeated" into EXP
// distribution and level-ups for the player-side party. This step is
// the signal-only scaffold - defeat notification wiring, the
// distribution/threshold-loop logic, and the visual hookups land in
// the following Phase 18-A steps.
//
// Lives under FloorController (the de-facto battle manager) rather
// than as an autoload: every reference it needs (player, spawned
// allies) is per-dungeon-scene state that FloorController already
// owns, and party EXP is only ever earned inside a dungeon.
public partial class ExperienceSystem : Node
{
    private Player _player;
    private FloorController _floorController;

    // Called by FloorController right after it creates/childs this node.
    // The recipient set (player + currently spawned allies) is
    // re-enumerated per defeat rather than cached - allies are respawned
    // fresh every floor, so a held list would go stale immediately.
    public void Initialize(Player player, FloorController floorController)
    {
        _player = player;
        _floorController = floorController;
    }

    // Fired for every defeat the combat layer reports, before any
    // faction filtering - a future quest/achievement system can listen
    // without caring who benefits.
    [Signal] public delegate void EntityDefeatedEventHandler(Entity victim, Entity attacker);

    // Fired once per recipient per defeat, with the amount actually
    // credited (post level-cap clamp) - log/floating-text feed.
    [Signal] public delegate void ExpGainedEventHandler(Entity entity, long amount);

    // Fired once per level gained (a big EXP hit that jumps several
    // levels emits one signal per level, in order). statDeltas packs
    // (MaxHp, Attack, Defense) increases as a Vector3I so the payload
    // stays Variant-compatible.
    [Signal] public delegate void LeveledUpEventHandler(Entity entity, int oldLevel, int newLevel, Vector3I statDeltas);

    // Global scaling constant K: Gained = floor(BaseExpYield * Level / K).
    // K only matters as the ratio r = BaseExpYield / K (the per-victim-
    // level EXP rate); it exists purely so BaseExpYield stays a readable
    // integer species value (standard 55 / K 10 -> r = 5.5).
    public const int ExpDivisorK = 10;

    // The combat layer's single defeat-detection entry point -
    // AttackAction/ThrowItemAction call this the moment a victim's HP
    // check comes up dead, before victim.Die() runs (the victim node is
    // still valid for reading Level/BaseExpYield/position).
    //
    // Enemy-on-player kills never reach this method (HostileEntity's
    // AttackActions carry no FloorController, so their call site
    // null-chains away) - harmless, since defeats without a player-side
    // attacker distribute nothing anyway.
    public void NotifyDefeated(Entity victim, Entity attacker)
    {
        EmitSignal(SignalName.EntityDefeated, victim, attacker);

        // PMD-style distribution: only the player side earns EXP, and
        // ANY player-side kill (player or ally) pays every living member
        // in full - so the enemy-vs-enemy crossfire case and the
        // "enemy killed an ally" case both distribute nothing.
        if (victim == null || victim.Faction != Faction.Enemy) return;
        if (attacker == null || attacker.Faction != Faction.Player) return;

        long gained = CalculateGained(victim.Stats.BaseExpYield, victim.Stats.Level);
        if (gained <= 0) return;

        // One party-level log line per defeat (per-member lines would
        // flood the 6-line MessageLogUI on every kill); per-member
        // feedback is the floating +EXP popup in Grant instead.
        MessageLogger.Log($"The party gained {gained} EXP for defeating {victim.ActorName}!", MessageLogger.ProgressionColor);

        if (_player != null && _player.IsAlive)
            Grant(_player, gained);

        foreach (var ally in _floorController.SpawnedAllies)
            if (GodotObject.IsInstanceValid(ally) && ally.IsAlive)
                Grant(ally, gained);
    }

    // Pure and static so the confirmed benchmark table (floor(5.5 x Lv))
    // is directly verifiable without staging a real kill. Integer
    // division IS floor here - every operand is non-negative.
    public static long CalculateGained(int baseExpYield, int victimLevel) =>
        (long)Mathf.Max(0, baseExpYield) * Mathf.Max(0, victimLevel) / ExpDivisorK;

    private void Grant(Entity member, long gained)
    {
        var stats = member.Stats;

        // Cap clamp (spec 5-5): whatever would overflow Lv100's total
        // is silently discarded, and a member already at the cap total
        // gains nothing at all (no signal, no popup).
        long capTotal = ExpCurve.TotalExpForLevel(EntityStats.LevelCap);
        long credited = System.Math.Min(gained, capTotal - stats.CurrentExp);
        if (credited <= 0) return;

        stats.CurrentExp += credited;
        EmitSignal(SignalName.ExpGained, member, credited);
        member.ShowExpPopup(credited);

        // Threshold loop (spec 5-4): one iteration per level so a
        // multi-level windfall still yields ordered per-level signals.
        // Level++ goes through Phase 17's setter, which IS the
        // "Recompute()" path - HAD stats re-derive on read and the
        // setter's diff mechanism preserves missing HP (level-up is
        // deliberately NOT a heal, per PMD-style spec 9-5).
        while (stats.Level < EntityStats.LevelCap && stats.CurrentExp >= ExpCurve.TotalExpForLevel(stats.Level + 1))
        {
            int oldLevel = stats.Level;
            var before = new Vector3I(stats.MaxHp, stats.Attack, stats.Defense);
            stats.Level++;
            var deltas = new Vector3I(stats.MaxHp, stats.Attack, stats.Defense) - before;
            EmitSignal(SignalName.LeveledUp, member, oldLevel, stats.Level, deltas);
            MessageLogger.Log($"{member.ActorName} leveled up to Lv {stats.Level}! (HP +{deltas.X}, Atk +{deltas.Y}, Def +{deltas.Z})", MessageLogger.ProgressionColor);
        }
    }
}
