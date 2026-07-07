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

    // Player-assigned via MenuUI's Tactics screen. ActFreely is this
    // class's original always-engage AI; FollowOnly skips combat
    // entirely (see DecideAction below).
    public Tactics CurrentTactics { get; set; } = Tactics.ActFreely;

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
        if (CurrentTactics == Tactics.ActFreely)
        {
            var enemy = FindVisibleEnemy();
            if (enemy != null) return DecideCombatAction(enemy);
        }

        if (IsFollowTargetValid() && IsAdjacent(GridPosition, TargetToFollow.GridPosition))
            return new WaitAction(this); // Idle: already at the target's side

        return DecideFollowAction();
    }

    // FloorController re-links the chain when an ally dies
    // (RelinkFollowChain), but stay defensive against a freed/dead
    // leader in the window before that runs.
    private bool IsFollowTargetValid() =>
        TargetToFollow != null && GodotObject.IsInstanceValid(TargetToFollow) && TargetToFollow.IsAlive;

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
        // 8-directional melee reach; a Wall shoulder blocks the diagonal
        // (CanAttackAdjacent), same rule as every other attacker.
        if (CanAttackAdjacent(enemy.GridPosition))
        {
            var moveSlot = Moves.GetFirstAutoUsableMove();
            if (moveSlot != null)
                return new AttackAction(this, enemy, moveSlot, FloorController);

            GD.Print($"[AI] {ActorName} is next to an enemy but has no auto-usable move - holding position.");
            return new WaitAction(this);
        }

        if (Pathfinder == null) return new WaitAction(this);

        var next = Pathfinder.GetNextStep(GridPosition, enemy.GridPosition, Stats.GetMovementProfile());
        if (next == null) return new WaitAction(this);

        // Same guards as HostileEntity's chase: never step onto a
        // profile-impassable hazard the pathfinder only "avoided", and
        // never stack onto an occupied tile.
        if (!CanMoveTo(next.Value)) return new WaitAction(this);
        if (FloorController != null && FloorController.GetEntityAt(next.Value) != null)
            return new WaitAction(this);

        return new MoveAction(this, next.Value);
    }

    private IAction DecideFollowAction()
    {
        if (!IsFollowTargetValid() || TargetToFollow.PreviousPosition == null)
            return new WaitAction(this); // target hasn't moved yet (or chain is mid-relink) - nothing to follow

        var footprint = TargetToFollow.PreviousPosition.Value;
        if (footprint == GridPosition)
            return new WaitAction(this); // already standing on the latest footprint

        // The leader might have a different terrain profile (e.g. Hover)
        // than this follower - don't blindly step onto a footprint this
        // entity can't actually survive/stand on. Also never stack onto
        // a tile someone else still occupies.
        if (!CanMoveTo(footprint))
            return new WaitAction(this);
        if (FloorController != null && FloorController.GetEntityAt(footprint) != null)
            return new WaitAction(this);

        return new MoveAction(this, footprint);
    }

    // Follow-idle proximity: with 8-directional movement, a diagonal
    // neighbor also counts as "at the leader's side" (Chebyshev == 1).
    private static bool IsAdjacent(Vector2I a, Vector2I b)
    {
        var diff = (a - b).Abs();
        return diff.X <= 1 && diff.Y <= 1 && (diff.X + diff.Y) > 0;
    }
}
