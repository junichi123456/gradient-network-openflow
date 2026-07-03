using Godot;
using MysteryDungeon.Turn;
using MysteryDungeon.Dungeon;
using MysteryDungeon.UI;

namespace MysteryDungeon.Entities;

// Reads arrow keys / Enter-Space and submits exactly one action per
// key press to the TurnManager. Input is ignored while a turn is
// being processed (NPCs acting), once an action has been submitted for
// the current turn, once the player has died, or while MenuUI.IsOpen -
// see MenuUI for the other half of that input-routing split.
public partial class Player : Entity
{
    // Assigned by the composition root (DungeonScene) after instancing.
    public TurnManager TurnManager { get; set; }
    public FloorController FloorController { get; set; }
    public MenuUI MenuUI { get; set; }

    // Player-only carried items (see InventoryManager); enemies never
    // pick anything up, so this lives here rather than on the Entity
    // base like Stats/Moves.
    public InventoryManager Inventory { get; private set; }

    // Fully separate from Inventory - Material-type items only ever go
    // here (see FloorController.TryPickupItemAt), and only ever leave
    // via HubUpgradeManager.DepositMaterials on a dungeon return.
    public MaterialInventory MaterialInventory { get; private set; }

    // Last direction the player pressed - used as the autoaim "facing"
    // for menu-invoked moves (Phase 6 dropped the direction-picker for
    // moves, see MenuUI.HandleAccept). Defaults to facing down.
    public Vector2I LastFacingDirection { get; private set; } = new Vector2I(0, 1);

    // Diagonal-only movement modifier (Shiren/PMD-style "hold to strafe
    // diagonally"): while held, a single cardinal key only turns the
    // player to face that direction instead of moving - this is what
    // stops a slightly-mistimed 2-key press from turning into an
    // unwanted 1-tile orthogonal step. Registered at runtime (rather
    // than hand-editing project.godot's InputMap resource syntax, which
    // is easy to get subtly wrong) the first time a Player exists.
    private const string DiagonalLockAction = "diagonal_lock";

    private bool _inputDisabled;

    public override void _Ready()
    {
        base._Ready(); // creates Stats + Moves + the debug ColorRect visual

        if (!InputMap.HasAction(DiagonalLockAction))
        {
            InputMap.AddAction(DiagonalLockAction);
            InputMap.ActionAddEvent(DiagonalLockAction, new InputEventKey { PhysicalKeycode = Key.Shift });
        }

        Faction = Faction.Player;

        Inventory = new InventoryManager { Name = "Inventory" };
        AddChild(Inventory);

        MaterialInventory = new MaterialInventory { Name = "MaterialInventory" };
        AddChild(MaterialInventory);

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
        if (MenuUI != null && MenuUI.IsOpen) return; // MenuUI owns input while open
        if (TurnManager == null || TurnManager.CurrentState != TurnState.WaitingForPlayerInput)
            return;

        if (@event.IsActionPressed("ui_focus_next")) // Tab: open the command menu
        {
            MenuUI?.Open();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("ui_accept"))
        {
            TurnManager.SubmitPlayerAction(new WaitAction(this));
            GetViewport().SetInputAsHandled();
            return;
        }

        // Only react to a fresh press of one of the 4 direction actions
        // (preserves "one action per key press"); the actual direction
        // vector below is then derived from ALL 4 actions' currently-held
        // state, not just the one that fired this event, so a diagonal
        // combo (e.g. ui_up + ui_right held together) resolves correctly.
        bool isDirectionEvent = @event.IsActionPressed("ui_up") || @event.IsActionPressed("ui_down")
            || @event.IsActionPressed("ui_left") || @event.IsActionPressed("ui_right");
        if (!isDirectionEvent) return;

        // 8-directional movement: derive the full direction vector from
        // which of the 4 cardinal actions are currently held (not just
        // the single one that triggered this event), so opposite/diagonal
        // combos - e.g. ui_up + ui_right held together - resolve to a
        // single diagonal step instead of only ever moving orthogonally.
        bool up = Input.IsActionPressed("ui_up");
        bool down = Input.IsActionPressed("ui_down");
        bool left = Input.IsActionPressed("ui_left");
        bool right = Input.IsActionPressed("ui_right");

        var direction = new Vector2I((right ? 1 : 0) - (left ? 1 : 0), (down ? 1 : 0) - (up ? 1 : 0));
        if (direction == Vector2I.Zero) return; // e.g. ui_up + ui_down held together cancel out

        // diagonal_lock held + a single cardinal key: turn to face that
        // direction only - no move, no turn consumed. A genuine 2-key
        // diagonal combo still moves normally even with the lock held.
        bool isDiagonal = direction.X != 0 && direction.Y != 0;
        if (Input.IsActionPressed(DiagonalLockAction) && !isDiagonal)
        {
            LastFacingDirection = direction;
            GetViewport().SetInputAsHandled();
            return;
        }

        LastFacingDirection = direction;

        Vector2I target = GridPosition + direction;

        // Bump-to-attack: moving into an enemy-occupied tile attacks
        // instead - but a diagonal bump obeys the same Wall corner rule
        // as movement (CanAttackAdjacent), so you can't hit around a
        // wall corner.
        var enemy = FloorController?.GetEnemyAt(target);
        if (enemy != null)
        {
            if (!CanAttackAdjacent(target))
            {
                GD.Print($"[Player] a wall corner blocks the diagonal attack toward ({target.X}, {target.Y}).");
            }
            else
            {
                var moveSlot = Moves.GetActiveMove();
                if (moveSlot != null)
                    TurnManager.SubmitPlayerAction(new AttackAction(this, enemy, moveSlot, FloorController));
                else
                    GD.Print("[Player] has no move equipped to attack with.");
            }
        }
        else if (CanMoveTo(target) && FloorController?.GetEntityAt(target) is AllyEntity ally)
        {
            // Party members never block the player outright - stepping
            // into an ally's tile swaps places instead, same as passing
            // a teammate in a corridor.
            TurnManager.SubmitPlayerAction(new SwapAction(this, ally));
        }
        else if (CanMoveTo(target) && FloorController?.GetEntityAt(target) == null)
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
