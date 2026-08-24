using Godot;
using MysteryDungeon.Entities;

namespace MysteryDungeon.Turn;

public class MoveAction : IAction
{
    public ITurnActor Actor { get; }

    private readonly Entity _entity;
    private readonly Vector2I _targetPos;

    public MoveAction(Entity entity, Vector2I targetPos)
    {
        Actor = entity;
        _entity = entity;
        _targetPos = targetPos;
    }

    public void Execute(int turnNumber)
    {
        _entity.MoveTo(_targetPos);
        GD.Print($"[Turn {turnNumber}] {Actor.ActorName} moved to ({_targetPos.X}, {_targetPos.Y})");

        // Trap-move fields that react to being stepped on (うすらひ's slide,
        // クレバス/じわれ's bite). Kept out of MoveTo itself so forced
        // relocations that are not a "step" (spawns, floor placement) stay
        // inert - see Entity.ResolveTileEntry.
        _entity.ResolveTileEntry();
    }
}
