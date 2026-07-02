using Godot;
using System.Collections.Generic;
using MysteryDungeon.Grid;
using MysteryDungeon.Turn;
using MysteryDungeon.Utils;
using MysteryDungeon.Combat;
using MysteryDungeon.Dungeon;

namespace MysteryDungeon.Entities;

// Shared AI for enemies: wander randomly until this entity's own tile
// becomes visible (Tile.IsVisible, computed by FovManager off the
// player's FOV - visibility is symmetric, so this doubles as "can this
// enemy see the player's party" without a separate per-enemy FOV pass),
// then pick a target from the player-side party and chase it via
// AStarPathfinder.
//
// Target selection: candidates the enemy's auto-usable move is
// type-advantaged against (multiplier > 1x) are preferred - nearest
// among them; if no advantaged candidate exists, simply the nearest
// player-side entity. Allies are targeted the same way the Player is.
public partial class HostileEntity : Entity
{
    private enum AiState { Idle, Chasing }

    private AiState _state = AiState.Idle;
    private Entity _currentTarget;
    private Vector2I? _targetPosition;

    // Assigned by FloorController at spawn time (same pattern as Grid).
    public AStarPathfinder Pathfinder { get; set; }
    public Player TargetPlayer { get; set; }
    public FloorController FloorController { get; set; }

    // DummyNPC adds an idle chance to its wander; FastNPC doesn't.
    protected virtual float WanderWaitChance => 0f;

    public override IAction DecideAction()
    {
        UpdateAiState();
        return _state == AiState.Chasing ? DecideChaseAction() : DecideWanderAction();
    }

    private void UpdateAiState()
    {
        if (Grid == null) return;
        if (!Grid.GetTile(GridPosition).IsVisible) return;

        var target = SelectTarget();
        if (target == null) return;

        if (_state != AiState.Chasing)
            GD.Print($"[AI] {ActorName} spotted {target.ActorName}! Switching to Chasing.");
        else if (_currentTarget != target && GodotObject.IsInstanceValid(target))
            GD.Print($"[AI] {ActorName} switched target to {target.ActorName}.");

        _state = AiState.Chasing;
        _currentTarget = target;
        _targetPosition = target.GridPosition;
    }

    // Prefer the nearest candidate our auto-usable move is
    // type-advantaged (>1x) against; otherwise the nearest candidate
    // outright. Distance is Chebyshev, matching 8-directional movement.
    private Entity SelectTarget()
    {
        var move = Moves.GetFirstAutoUsableMove();

        Entity nearest = null, nearestAdvantaged = null;
        int nearestDist = int.MaxValue, nearestAdvDist = int.MaxValue;

        foreach (var candidate in EnumerateTargetCandidates())
        {
            var diff = (candidate.GridPosition - GridPosition).Abs();
            int dist = Mathf.Max(diff.X, diff.Y);

            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = candidate;
            }

            if (move != null && dist < nearestAdvDist
                && TypeChartManager.GetMultiplier(move.Data.Type, candidate.Stats.Type1, candidate.Stats.Type2) > 1f)
            {
                nearestAdvDist = dist;
                nearestAdvantaged = candidate;
            }
        }

        return nearestAdvantaged ?? nearest;
    }

    private IEnumerable<Entity> EnumerateTargetCandidates()
    {
        if (TargetPlayer != null && TargetPlayer.IsAlive)
            yield return TargetPlayer;

        if (FloorController == null) yield break;

        foreach (var ally in FloorController.SpawnedAllies)
            if (GodotObject.IsInstanceValid(ally) && ally.IsAlive)
                yield return ally;
    }

    private IAction DecideChaseAction()
    {
        if (_currentTarget != null && (!GodotObject.IsInstanceValid(_currentTarget) || !_currentTarget.IsAlive))
            _currentTarget = null;

        // In range (8-directional, wall corners block diagonals):
        // attack instead of pathfinding at all.
        if (_currentTarget != null && CanAttackAdjacent(_currentTarget.GridPosition))
        {
            var moveSlot = Moves.GetFirstAutoUsableMove();
            if (moveSlot != null)
                return new AttackAction(this, _currentTarget, moveSlot);

            GD.Print($"[AI] {ActorName} is next to {_currentTarget.ActorName} but has no auto-usable move - holding position.");
            return new WaitAction(this);
        }

        if (_targetPosition == null || Pathfinder == null)
            return DecideWanderAction();

        var target = _targetPosition.Value;

        if (GridPosition == target)
        {
            GD.Print($"[AI] {ActorName} reached the last known position {target} but lost the trail. Back to wandering.");
            _state = AiState.Idle;
            _targetPosition = null;
            _currentTarget = null;
            return DecideWanderAction();
        }

        var next = Pathfinder.GetNextStep(GridPosition, target, Stats.GetMovementProfile());
        if (next == null)
        {
            GD.Print($"[AI] {ActorName} found no path to {target}. Giving up the chase.");
            _state = AiState.Idle;
            _targetPosition = null;
            _currentTarget = null;
            return DecideWanderAction();
        }

        // The pathfinder marks profile-impassable hazards (Water/Lava/
        // Chasm) as extreme-cost rather than solid, so a hazard tile can
        // still appear in the path when it's the ONLY route. Never
        // actually step onto one - wait at the edge instead.
        if (!CanMoveTo(next.Value))
        {
            GD.Print($"[AI] {ActorName} refuses to step onto {Grid.GetTile(next.Value).Terrain} at {next.Value} - waiting at the edge.");
            return new WaitAction(this);
        }

        // Never stack onto a tile someone is already standing on.
        if (FloorController != null && FloorController.GetEntityAt(next.Value) != null)
            return new WaitAction(this);

        GD.Print($"[AI] {ActorName} chasing {(_currentTarget != null ? _currentTarget.ActorName : "trail")} toward {target}, next step {next.Value}.");
        return new MoveAction(this, next.Value);
    }

    private IAction DecideWanderAction()
    {
        if (WanderWaitChance > 0f && GD.Randf() < WanderWaitChance)
            return new WaitAction(this);

        foreach (var dir in RandomUtils.ShuffledNeighbors4())
        {
            var target = GridPosition + dir;
            if (CanWalkTo(target) && (FloorController == null || FloorController.GetEntityAt(target) == null))
                return new MoveAction(this, target);
        }

        return new WaitAction(this);
    }
}
