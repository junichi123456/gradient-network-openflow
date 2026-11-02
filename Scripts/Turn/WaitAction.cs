using Godot;
using MysteryDungeon.Entities;

namespace MysteryDungeon.Turn;

public class WaitAction : IAction
{
    public ITurnActor Actor { get; }

    private readonly Entity _entity;

    public WaitAction(Entity entity)
    {
        Actor = entity;
        _entity = entity;
    }

    public void Execute(int turnNumber)
    {
        _entity.Wait();
        var pos = _entity.GridPosition;
        GD.Print($"[Turn {turnNumber}] {Actor.ActorName} waited (footstep) at ({pos.X}, {pos.Y})");
    }
}
