using Godot;
using MysteryDungeon.Turn;
using MysteryDungeon.Utils;

namespace MysteryDungeon.Entities;

// Speed 100 (normal): registered with the TurnScheduler and acts once
// per player turn - moves to a random walkable neighbor, or waits.
//
// Identity (name/speed/color) is fixed here in code rather than left to
// the editor Inspector, since FloorController spawns these dynamically
// at runtime (no scene instance to configure). Will be replaced by a
// real monster_id -> Database lookup in Phase 3.
public partial class DummyNPC : Entity
{
    private const float WaitChance = 0.2f;

    public override void _Ready()
    {
        ActorName = "DummyNPC";
        Speed = 100;
        DebugColor = Colors.SkyBlue;
        base._Ready();
    }

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
