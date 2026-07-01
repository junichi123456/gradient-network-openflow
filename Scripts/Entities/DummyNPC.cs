using Godot;
using MysteryDungeon.Turn;
using MysteryDungeon.Utils;

namespace MysteryDungeon.Entities;

// Speed 100 (normal): registered with the TurnScheduler and acts once
// per player turn - moves to a random walkable neighbor, or waits.
public partial class DummyNPC : Entity
{
    private const float WaitChance = 0.2f;

    public override IAction DecideAction()
    {
        if (GD.Randf() < WaitChance)
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
