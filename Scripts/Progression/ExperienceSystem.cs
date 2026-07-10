using Godot;
using MysteryDungeon.Dungeon;
using MysteryDungeon.Entities;

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

    // The combat layer's single defeat-detection entry point -
    // AttackAction/ThrowItemAction call this the moment a victim's HP
    // check comes up dead, before victim.Die() runs (the victim node is
    // still valid for reading Level/BaseExpYield/position). EXP
    // distribution hangs off this in the next Phase 18-A step; for now
    // it only rebroadcasts the defeat as a signal.
    //
    // Enemy-on-player kills never reach this method (HostileEntity's
    // AttackActions carry no FloorController, so their call site
    // null-chains away) - harmless, since defeats without a player-side
    // attacker distribute nothing anyway.
    public void NotifyDefeated(Entity victim, Entity attacker)
    {
        EmitSignal(SignalName.EntityDefeated, victim, attacker);
    }
}
