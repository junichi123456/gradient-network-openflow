using Godot;
using MysteryDungeon.Turn;
using MysteryDungeon.Utils;

namespace MysteryDungeon.Entities;

// Speed 200 (double speed): the TurnScheduler grants it 200 energy per
// player turn, so DecideAction() is called twice per turn (100 energy
// consumed each time) - it moves to a random walkable neighbor each call.
//
// Identity (name/speed/color) is fixed here in code rather than left to
// the editor Inspector, since FloorController spawns these dynamically
// at runtime (no scene instance to configure). Will be replaced by a
// real monster_id -> Database lookup in Phase 3.
public partial class FastNPC : Entity
{
    public override void _Ready()
    {
        ActorName = "FastNPC";
        Speed = 200;
        DebugColor = Colors.OrangeRed;
        base._Ready();
    }

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
