using Godot;
using MysteryDungeon.Grid;
using MysteryDungeon.Turn;
using MysteryDungeon.Utils;

namespace MysteryDungeon.Entities;

// Shared AI for enemies: wander randomly until the player enters a
// currently-visible tile (Tile.IsVisible, computed by FovManager off
// the player's own FOV - visibility is symmetric, so this doubles as
// "can this enemy see the player" without a separate per-enemy FOV
// pass), then chase the last-known player position via AStarPathfinder.
public partial class HostileEntity : Entity
{
    private enum AiState { Idle, Chasing }

    private AiState _state = AiState.Idle;
    private Vector2I? _targetPosition;

    // Assigned by FloorController at spawn time (same pattern as Grid).
    public AStarPathfinder Pathfinder { get; set; }
    public Player TargetPlayer { get; set; }

    // DummyNPC adds an idle chance to its wander; FastNPC doesn't.
    protected virtual float WanderWaitChance => 0f;

    public override IAction DecideAction()
    {
        UpdateAiState();
        return _state == AiState.Chasing ? DecideChaseAction() : DecideWanderAction();
    }

    private void UpdateAiState()
    {
        if (TargetPlayer == null || Grid == null) return;
        if (!Grid.GetTile(GridPosition).IsVisible) return;

        if (_state != AiState.Chasing)
            GD.Print($"[AI] {ActorName} spotted the player! Switching to Chasing.");

        _state = AiState.Chasing;
        _targetPosition = TargetPlayer.GridPosition;
    }

    private IAction DecideChaseAction()
    {
        if (_targetPosition == null || Pathfinder == null)
            return DecideWanderAction();

        var target = _targetPosition.Value;

        if (GridPosition == target)
        {
            GD.Print($"[AI] {ActorName} reached the last known position {target} but lost the trail. Back to wandering.");
            _state = AiState.Idle;
            _targetPosition = null;
            return DecideWanderAction();
        }

        var next = Pathfinder.GetNextStep(GridPosition, target);
        if (next == null)
        {
            GD.Print($"[AI] {ActorName} found no path to {target}. Giving up the chase.");
            _state = AiState.Idle;
            _targetPosition = null;
            return DecideWanderAction();
        }

        if (TargetPlayer != null && next.Value == TargetPlayer.GridPosition)
        {
            GD.Print($"[AI] {ActorName} is right next to the player - holding position.");
            return new WaitAction(this);
        }

        GD.Print($"[AI] {ActorName} chasing toward {target}, next step {next.Value}.");
        return new MoveAction(this, next.Value);
    }

    private IAction DecideWanderAction()
    {
        if (WanderWaitChance > 0f && GD.Randf() < WanderWaitChance)
            return new WaitAction(this);

        foreach (var dir in RandomUtils.ShuffledNeighbors4())
        {
            var target = GridPosition + dir;
            if (Grid != null && Grid.IsWalkable(target))
                return new MoveAction(this, target);
        }

        return new WaitAction(this);
    }
}
