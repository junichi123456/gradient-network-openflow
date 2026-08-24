using Godot;

namespace MysteryDungeon.Hub;

// Dungeon Gate: pick a destination (DungeonDestinations.All) and depart.
// On confirm, stashes the chosen DungeonConfig/DungeonRuleId on
// HubUpgradeManager and changes to DungeonScene.tscn - see
// DungeonScene._Ready for the other half of the handoff.
public partial class DungeonSelectUI : Control
{
    private HubPlayer _player;
    private VBoxContainer _list;
    private int _cursor;

    public bool IsOpen { get; private set; }

    public void Initialize(HubPlayer player)
    {
        _player = player;

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
        _cursor = 0;
        if (_player != null) _player.InputEnabled = false;
        Render();
        GD.Print("[Hub] Dungeon Gate opened.");
    }

    public void Close()
    {
        IsOpen = false;
        Visible = false;
        if (_player != null) _player.InputEnabled = true;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen) return;

        if (@event.IsActionPressed("ui_cancel"))
        {
            Close();
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
            // Handled-flag set first: HandleAccept() may trigger
            // ChangeSceneToFile, which detaches this node from the tree
            // immediately - GetViewport() would return null afterwards.
            GetViewport().SetInputAsHandled();
            HandleAccept();
        }
    }

    private void MoveCursor(int delta)
    {
        int count = Mathf.Max(1, DungeonDestinations.All.Count);
        _cursor = (_cursor + delta + count) % count;
        Render();
    }

    private void HandleAccept()
    {
        if (DungeonDestinations.All.Count == 0) return;

        var destination = DungeonDestinations.All[_cursor];
        GD.Print($"[Hub] Departing for {destination.DisplayName} (MaxFloors={destination.Config.MaxFloors}, EndType={destination.Config.EndType})...");

        var hub = HubUpgradeManager.Instance;
        if (hub != null)
        {
            hub.PendingDungeonConfig = destination.Config;
            hub.PendingDungeonId = destination.DungeonRuleId;
        }

        Close();
        GetTree().ChangeSceneToFile("res://Scenes/DungeonScene.tscn");
    }

    private void Render()
    {
        foreach (Node child in _list.GetChildren())
            child.QueueFree();

        for (int i = 0; i < DungeonDestinations.All.Count; i++)
        {
            string prefix = i == _cursor ? "> " : "  ";
            _list.AddChild(new Label { Text = prefix + DungeonDestinations.All[i].DisplayName });
        }
    }
}
