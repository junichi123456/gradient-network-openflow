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
    }
}
