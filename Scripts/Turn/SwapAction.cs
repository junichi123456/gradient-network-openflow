using Godot;
using MysteryDungeon.Entities;

namespace MysteryDungeon.Turn;

// Player <-> ally position swap ("passing" in a corridor), used instead
// of MoveAction when the player's move target is occupied by a party
// member rather than being genuinely blocked - see Player._UnhandledInput.
public class SwapAction : IAction
{
    public ITurnActor Actor { get; }

    private readonly Entity _mover;
    private readonly Entity _other;

    public SwapAction(Entity mover, Entity other)
    {
        Actor = mover;
        _mover = mover;
        _other = other;
    }

    public void Execute(int turnNumber)
    {
        var moverPos = _mover.GridPosition;
        var otherPos = _other.GridPosition;

        _mover.MoveTo(otherPos);
        _other.MoveTo(moverPos);

        GD.Print($"[Turn {turnNumber}] {Actor.ActorName} swapped places with {_other.ActorName}.");
    }
}
