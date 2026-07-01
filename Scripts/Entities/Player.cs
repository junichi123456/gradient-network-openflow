using Godot;
using MysteryDungeon.Turn;
using MysteryDungeon.Dungeon;

namespace MysteryDungeon.Entities;

// Reads arrow keys / Enter-Space and submits exactly one action per
// key press to the TurnManager. Input is ignored while a turn is
// being processed (NPCs acting), once an action has been submitted for
// the current turn, or permanently once the player has died.
public partial class Player : Entity
{
    // Assigned by the composition root (TestScene) after instancing.
    public TurnManager TurnManager { get; set; }
    public FloorController FloorController { get; set; }

    private bool _inputDisabled;

    public override void _Ready()
    {
        base._Ready(); // creates Stats + Moves + the debug ColorRect visual

        Stats.MaxHp = 30;
        Stats.CurrentHp = 30;
        Stats.Attack = 12;
        Stats.Defense = 10;
        Stats.SpAttack = 10;
        Stats.SpDefense = 10;
        Stats.Type1 = "Neutral";
        Stats.Level = 10;
        Stats.MaxBelly = 100;
        Stats.Belly = 100;

        Moves.Learn("power_shot");
        Moves.Learn("flare_arrow");
    }

    public void DisableInput() => _inputDisabled = true;

    // NPCs get removed from the scene on death (see Entity.Die()); the
    // player stays on screen (no title-screen flow yet) and instead
    // just stops accepting input. AttackAction calls defender.Die()
    // uniformly for both cases - this override is what makes that safe.
    public override void Die()
    {
        if (!IsAlive) return;
        IsAlive = false;
        DisableInput();
        GD.Print("[Game] 💀 PLAYER DIED! GAME OVER 💀");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_inputDisabled) return;
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

        // Bump-to-attack: moving into an occupied tile attacks instead.
        var enemy = FloorController?.GetEnemyAt(target);
        if (enemy != null)
        {
            var moveSlot = Moves.GetActiveMove();
            if (moveSlot != null)
                TurnManager.SubmitPlayerAction(new AttackAction(this, enemy, moveSlot));
            else
                GD.Print("[Player] has no move equipped to attack with.");
        }
        else if (Grid != null && Grid.IsWalkable(target))
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
