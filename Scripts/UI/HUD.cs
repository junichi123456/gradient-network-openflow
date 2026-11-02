using Godot;
using MysteryDungeon.Turn;
using MysteryDungeon.Entities;
using MysteryDungeon.Dungeon;

namespace MysteryDungeon.UI;

// Always-on status readout (top-left): current floor, level, belly, and
// an HP bar row per active party member (player first, then each
// AllyEntity FloorController still has spawned). Same "subscribe to
// TurnEnded, refresh" pattern as MinimapUI, but updates Label
// text/ColorRect sizes directly instead of _Draw()/QueueRedraw().
public partial class HUD : Control
{
    private const float BarWidth = 100f;
    private const float BarHeight = 10f;

    private Player _player;
    private FloorController _floorController;

    private Label _floorLabel;
    private Label _levelLabel;
    private Label _bellyLabel;
    private Label _weatherLabel;
    private VBoxContainer _partyBox;

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
        _bellyLabel = new Label();
        _weatherLabel = new Label();
        _partyBox = new VBoxContainer();
        box.AddChild(_floorLabel);
        box.AddChild(_levelLabel);
        box.AddChild(_bellyLabel);
        box.AddChild(_weatherLabel);
        box.AddChild(_partyBox);

        turnManager.TurnEnded += _ => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        var stats = _player.Stats;
        _floorLabel.Text = $"Floor {_floorController.FloorNumber}";
        _levelLabel.Text = $"Lv {stats.Level}";
        _bellyLabel.Text = $"Belly {stats.Belly}/{stats.MaxBelly}";

        // Hidden entirely when there is no weather, so a weatherless
        // dungeon's HUD is unchanged from before the system existed.
        var weather = _floorController.Weather;
        _weatherLabel.Visible = weather.Current != WeatherType.None;
        _weatherLabel.Text = weather.IsTemporary
            ? $"天候 {WeatherTypeNames.Japanese(weather.Current)} (残り{weather.Remaining})"
            : $"天候 {WeatherTypeNames.Japanese(weather.Current)}";

        foreach (Node child in _partyBox.GetChildren())
            child.QueueFree();

        AddPartyRow(_player.ActorName, stats.CurrentHp, stats.MaxHp);
        foreach (var ally in _floorController.SpawnedAllies)
        {
            if (!GodotObject.IsInstanceValid(ally) || !ally.IsAlive) continue;
            AddPartyRow(ally.ActorName, ally.Stats.CurrentHp, ally.Stats.MaxHp);
        }
    }

    private void AddPartyRow(string name, int hp, int maxHp)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(70, 0) });

        var barBg = new ColorRect { Color = new Color(0.15f, 0.15f, 0.15f), Size = new Vector2(BarWidth, BarHeight) };
        float ratio = maxHp > 0 ? Mathf.Clamp((float)hp / maxHp, 0f, 1f) : 0f;
        var barFill = new ColorRect { Color = HpBarColor(ratio), Size = new Vector2(BarWidth * ratio, BarHeight) };
        barBg.AddChild(barFill);
        row.AddChild(barBg);

        row.AddChild(new Label { Text = $" {hp}/{maxHp}" });
        _partyBox.AddChild(row);
    }

    private static Color HpBarColor(float ratio)
    {
        if (ratio >= 0.5f) return new Color(0.25f, 0.85f, 0.25f); // green
        if (ratio >= 0.2f) return new Color(0.9f, 0.85f, 0.2f);   // yellow
        return new Color(0.9f, 0.2f, 0.2f);                        // red
    }
}
