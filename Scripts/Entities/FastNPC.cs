using MysteryDungeon.Turn;
using MysteryDungeon.Utils;

namespace MysteryDungeon.Entities;

// Speed 200 (double speed): the TurnScheduler grants it 200 energy per
// player turn, so DecideAction() is called twice per turn (100 energy
// consumed each time) - it moves to a random walkable neighbor each call.
public partial class FastNPC : Entity
{
    public override IAction DecideAction()
    {
        foreach (var dir in RandomUtils.ShuffledNeighbors4())
        {
            var target = GridPosition + dir;
            if (Grid != null && Grid.IsWalkable(target))
                return new MoveAction(this, target);
        }

        return new WaitAction(this);
    }
}
