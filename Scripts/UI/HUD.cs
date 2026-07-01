using Godot;
using MysteryDungeon.Turn;
using MysteryDungeon.Entities;
using MysteryDungeon.Dungeon;

namespace MysteryDungeon.UI;

// Always-on status readout (top-left): current floor, level, HP, belly.
// Same "subscribe to TurnEnded, refresh" pattern as MinimapUI, but
// updates Label text directly instead of _Draw()/QueueRedraw().
public partial class HUD : Control
{
    private Player _player;
    private FloorController _floorController;

    private Label _floorLabel;
    private Label _levelLabel;
    private Label _hpLabel;
    private Label _bellyLabel;

    public void Initialize(Player player, TurnManager turnManager, FloorController floorController)
    {
        _player = player;
        _floorController = floorController;

        MouseFilter = MouseFilterEnum.Ignore;
        Position = new Vector2(10, 10);

        var box = new VBoxContainer();
        AddChild(box);

        _floorLabel = new Label();
        _levelLabel = new Label();
        _hpLabel = new Label();
        _bellyLabel = new Label();
        box.AddChild(_floorLabel);
        box.AddChild(_levelLabel);
        box.AddChild(_hpLabel);
        box.AddChild(_bellyLabel);

        turnManager.TurnEnded += _ => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        var stats = _player.Stats;
        _floorLabel.Text = $"Floor {_floorController.FloorNumber}";
        _levelLabel.Text = $"Lv {stats.Level}";
        _hpLabel.Text = $"HP {stats.CurrentHp}/{stats.MaxHp}";
        _bellyLabel.Text = $"Belly {stats.Belly}/{stats.MaxBelly}";
    }
}
