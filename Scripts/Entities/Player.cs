using Godot;
using MysteryDungeon.Turn;

namespace MysteryDungeon.Entities;

// Reads arrow keys / Enter-Space and submits exactly one action per
// key press to the TurnManager. Input is ignored while a turn is
// being processed (NPCs acting) or once an action has been submitted
// for the current turn.
public partial class Player : Entity
{
    // Assigned by the composition root (TestScene) after instancing.
    public TurnManager TurnManager { get; set; }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (TurnManager == null || TurnManager.CurrentState != TurnState.WaitingForPlayerInput)
            return;

        if (@event.IsActionPressed("ui_accept"))
        {
            TurnManager.SubmitPlayerAction(new WaitAction(this));
            GetViewport().SetInputAsHandled();
            return;
        }

        Vector2I direction;
        if (@event.IsActionPressed("ui_up")) direction = new Vector2I(0, -1);
        else if (@event.IsActionPressed("ui_down")) direction = new Vector2I(0, 1);
        else if (@event.IsActionPressed("ui_left")) direction = new Vector2I(-1, 0);
        else if (@event.IsActionPressed("ui_right")) direction = new Vector2I(1, 0);
        else return;

        Vector2I target = GridPosition + direction;

        if (Grid != null && Grid.IsWalkable(target))
        {
            TurnManager.SubmitPlayerAction(new MoveAction(this, target));
        }
        else
        {
            GD.Print($"[Player] blocked, cannot move to ({target.X}, {target.Y})");
        }

        GetViewport().SetInputAsHandled();
    }
}
