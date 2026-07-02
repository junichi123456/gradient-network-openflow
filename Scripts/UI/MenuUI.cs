using Godot;
using System.Collections.Generic;
using MysteryDungeon.Turn;
using MysteryDungeon.Entities;
using MysteryDungeon.Dungeon;

namespace MysteryDungeon.UI;

public enum MenuScreen
{
    Top,
    Moves,
    Items,
    ItemSubmenu,
    ChooseDirection,
}

// In-game command menu (opened with the "ui_focus_next" action, i.e.
// Tab). IsOpen is the single source of truth Player checks to decide
// whether field input (movement/bump-attack) or this menu's own
// _UnhandledInput should react to a key press - see Player._UnhandledInput.
//
// Screens are rendered by tearing down and rebuilding a plain
// VBoxContainer of Labels rather than using separate child scenes -
// simplest option for a text-only menu with no sprite/theme assets yet.
public partial class MenuUI : Control
{
    private Player _player;
    private TurnManager _turnManager;
    private FloorController _floorController;

    private VBoxContainer _list;

    private MenuScreen _screen;
    private int _cursor;
    private InventorySlot _selectedItemSlot;

    public bool IsOpen { get; private set; }

    public void Initialize(Player player, TurnManager turnManager, FloorController floorController)
    {
        _player = player;
        _turnManager = turnManager;
        _floorController = floorController;

        MouseFilter = MouseFilterEnum.Ignore;
        Position = new Vector2(200, 80);

        var panel = new PanelContainer();
        AddChild(panel);
        _list = new VBoxContainer();
        panel.AddChild(_list);

        Visible = false;
    }

    public void Open()
    {
        IsOpen = true;
        Visible = true;
        _screen = MenuScreen.Top;
        _cursor = 0;
        Render();
    }

    public void Close()
    {
        IsOpen = false;
        Visible = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen) return;

        if (_screen == MenuScreen.ChooseDirection)
        {
            HandleChooseDirectionInput(@event);
            return;
        }

        if (@event.IsActionPressed("ui_cancel"))
        {
            HandleCancel();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("ui_up"))
        {
            MoveCursor(-1);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("ui_down"))
        {
            MoveCursor(1);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("ui_accept"))
        {
            HandleAccept();
            GetViewport().SetInputAsHandled();
        }
    }

    private void HandleChooseDirectionInput(InputEvent @event)
    {
        Vector2I dir;
        if (@event.IsActionPressed("ui_up")) dir = new Vector2I(0, -1);
        else if (@event.IsActionPressed("ui_down")) dir = new Vector2I(0, 1);
        else if (@event.IsActionPressed("ui_left")) dir = new Vector2I(-1, 0);
        else if (@event.IsActionPressed("ui_right")) dir = new Vector2I(1, 0);
        else if (@event.IsActionPressed("ui_cancel"))
        {
            _screen = MenuScreen.ItemSubmenu;
            _cursor = 0;
            Render();
            GetViewport().SetInputAsHandled();
            return;
        }
        else
        {
            return;
        }

        var itemId = _selectedItemSlot.Data.Id;
        Close();
        _turnManager.SubmitPlayerAction(new ThrowItemAction(_player, dir, itemId, _player.Grid, _floorController));
        GetViewport().SetInputAsHandled();
    }

    private void MoveCursor(int delta)
    {
        int count = _screen switch
        {
            MenuScreen.Top => 3,
            MenuScreen.Moves => Mathf.Max(1, _player.Moves.Slots.Count),
            MenuScreen.Items => Mathf.Max(1, _player.Inventory.Slots.Count),
            MenuScreen.ItemSubmenu => 3,
            _ => 1,
        };

        _cursor = (_cursor + delta + count) % count;
        Render();
    }

    private void HandleAccept()
    {
        switch (_screen)
        {
            case MenuScreen.Top:
                if (_cursor == 0) { _screen = MenuScreen.Moves; _cursor = 0; Render(); }
                else if (_cursor == 1) { _screen = MenuScreen.Items; _cursor = 0; Render(); }
                else if (_cursor == 2) { Close(); _turnManager.SubmitPlayerAction(new WaitAction(_player)); }
                break;

            case MenuScreen.Moves:
                {
                    var slots = _player.Moves.Slots;
                    if (slots.Count == 0) return;

                    var moveSlot = slots[_cursor];
                    Close();

                    // No manual direction/target here on purpose - moves
                    // autoaim (facing tile, then any of the 8 neighbors)
                    // so future room-wide/self-targeted moves don't need
                    // a direction step at all. A whiff still costs the turn.
                    var target = _floorController.FindAutoAimTarget(_player.GridPosition, _player.LastFacingDirection);
                    _turnManager.SubmitPlayerAction(new AttackAction(_player, target, moveSlot, _floorController));
                }
                break;

            case MenuScreen.Items:
                {
                    var slots = _player.Inventory.Slots;
                    if (slots.Count == 0) return;

                    _selectedItemSlot = slots[_cursor];
                    _screen = MenuScreen.ItemSubmenu;
                    _cursor = 0;
                    Render();
                }
                break;

            case MenuScreen.ItemSubmenu:
                if (_cursor == 0) // つかう
                {
                    var itemId = _selectedItemSlot.Data.Id;
                    Close();
                    _turnManager.SubmitPlayerAction(new UseItemAction(_player, itemId));
                }
                else if (_cursor == 1) // なげる
                {
                    _screen = MenuScreen.ChooseDirection;
                    _cursor = 0;
                    Render();
                }
                else if (_cursor == 2) // おく
                {
                    var itemId = _selectedItemSlot.Data.Id;
                    Close();
                    _turnManager.SubmitPlayerAction(new DropItemAction(_player, _floorController, itemId));
                }
                break;
        }
    }

    private void HandleCancel()
    {
        switch (_screen)
        {
            case MenuScreen.Top:
                Close();
                break;
            case MenuScreen.Moves:
            case MenuScreen.Items:
                _screen = MenuScreen.Top;
                _cursor = 0;
                Render();
                break;
            case MenuScreen.ItemSubmenu:
                _screen = MenuScreen.Items;
                _cursor = 0;
                Render();
                break;
        }
    }

    private void Render()
    {
        foreach (Node child in _list.GetChildren())
            child.QueueFree();

        List<string> lines = _screen switch
        {
            MenuScreen.Top => new List<string> { "わざ (Moves)", "どうぐ (Items)", "足踏み (Rest)" },
            MenuScreen.Moves => BuildMoveLines(),
            MenuScreen.Items => BuildItemLines(),
            MenuScreen.ItemSubmenu => new List<string> { "つかう (Use)", "なげる (Throw)", "おく (Drop)" },
            MenuScreen.ChooseDirection => new List<string> { "Choose a direction (Esc to cancel)" },
            _ => new List<string>(),
        };

        bool showCursor = _screen != MenuScreen.ChooseDirection;
        for (int i = 0; i < lines.Count; i++)
        {
            var prefix = showCursor && i == _cursor ? "> " : "  ";
            _list.AddChild(new Label { Text = prefix + lines[i] });
        }
    }

    private List<string> BuildMoveLines()
    {
        var slots = _player.Moves.Slots;
        if (slots.Count == 0) return new List<string> { "(no moves learned)" };

        var lines = new List<string>();
        foreach (var slot in slots)
            lines.Add($"{slot.Data.Name} PP {slot.CurrentPp}/{slot.Data.MaxPp}");
        return lines;
    }

    private List<string> BuildItemLines()
    {
        var slots = _player.Inventory.Slots;
        if (slots.Count == 0) return new List<string> { "(bag is empty)" };

        var lines = new List<string>();
        foreach (var slot in slots)
            lines.Add($"{slot.Data.Name} x{slot.Quantity}");
        return lines;
    }
}
