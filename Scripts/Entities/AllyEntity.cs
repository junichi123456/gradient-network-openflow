using Godot;
using MysteryDungeon.Grid;
using MysteryDungeon.Turn;
using MysteryDungeon.Dungeon;

namespace MysteryDungeon.Entities;

// Player-side party member AI. Follows via a "conga line" (leader-
// follower chain): each ally tracks a single TargetToFollow (the
// Player for the first ally, the previous ally for the next) and, when
// not fighting, walks toward that target's PreviousPosition every turn
// - the exact tile the target just vacated. This needs no shared queue
// (a single FIFO would let multiple allies race for the same tile) and
// no A* for the common case, so it's O(1) per ally per turn; A* is only
// used in Combat, to close the last few tiles on a spotted enemy.
public partial class AllyEntity : Entity
{
    public string SpeciesId { get; set; } = "Ally";

    // Assigned by FloorController at spawn time (same pattern as
    // HostileEntity's Grid/Pathfinder).
    public AStarPathfinder Pathfinder { get; set; }
    public FloorController FloorController { get; set; }
    public Entity TargetToFollow { get; set; }

    public override void _Ready()
    {
        ActorName = SpeciesId;
        DebugColor = Colors.LightGreen;

        base._Ready(); // creates Stats + Moves + the debug ColorRect visual

        Faction = Faction.Player;

        Stats.MaxHp = 25;
        Stats.CurrentHp = 25;
        Stats.Attack = 10;
        Stats.Defense = 8;
        Stats.SpAttack = 8;
        Stats.SpDefense = 8;
        Stats.Type1 = "Neutral";
        Stats.Level = 8;

        Moves.Learn("power_shot");
    }

    public override IAction DecideAction()
    {
        var enemy = FindVisibleEnemy();
        if (enemy != null) return DecideCombatAction(enemy);

        if (TargetToFollow != null && IsAdjacent(GridPosition, TargetToFollow.GridPosition))
            return new WaitAction(this); // Idle: already at the target's side

        return DecideFollowAction();
    }

    // Reuses the same FOV-symmetry trick HostileEntity relies on: a
    // currently-visible tile (from the player's viewpoint) is
    // spottable by anyone standing near the player, allies included.
    private Entity FindVisibleEnemy()
    {
        if (FloorController == null || Grid == null) return null;

        foreach (var enemy in FloorController.SpawnedEnemies)
        {
            if (!GodotObject.IsInstanceValid(enemy) || !enemy.IsAlive) continue;
            if (Grid.GetTile(enemy.GridPosition).IsVisible) return enemy;
        }

        return null;
    }

    private IAction DecideCombatAction(Entity enemy)
    {
        if (IsAdjacent(GridPosition, enemy.GridPosition))
        {
            var moveSlot = Moves.GetFirstAutoUsableMove();
            if (moveSlot != null)
                return new AttackAction(this, enemy, moveSlot, FloorController);

            GD.Print($"[AI] {ActorName} is next to an enemy but has no auto-usable move - holding position.");
            return new WaitAction(this);
        }

        if (Pathfinder == null) return new WaitAction(this);

        var next = Pathfinder.GetNextStep(GridPosition, enemy.GridPosition);
        if (next == null) return new WaitAction(this);

        return new MoveAction(this, next.Value);
    }

    private IAction DecideFollowAction()
    {
        if (TargetToFollow == null || TargetToFollow.PreviousPosition == null)
            return new WaitAction(this); // target hasn't moved yet - nothing to follow

        var footprint = TargetToFollow.PreviousPosition.Value;
        if (footprint == GridPosition)
            return new WaitAction(this); // already standing on the latest footprint

        return new MoveAction(this, footprint);
    }

    private static bool IsAdjacent(Vector2I a, Vector2I b)
    {
        var diff = (a - b).Abs();
        return (diff.X == 1 && diff.Y == 0) || (diff.X == 0 && diff.Y == 1);
    }
}
