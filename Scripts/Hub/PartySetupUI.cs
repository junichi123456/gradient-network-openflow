using Godot;
using MysteryDungeon.Entities;

namespace MysteryDungeon.Hub;

// Pal Box: lets the player toggle which recruited pals (PartyManager.
// RecruitedRoster) are in the ActiveParty (max PartyManager.
// MaxActiveParty) for the next dungeon run. Same "rebuild a
// VBoxContainer of Labels" pattern as the dungeon's MenuUI - simplest
// option for a text-only list with no sprite/theme assets yet.
public partial class PartySetupUI : Control
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
        GD.Print("[Hub] Pal Box opened.");
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
            HandleAccept();
            GetViewport().SetInputAsHandled();
        }
    }

    private void MoveCursor(int delta)
    {
        var roster = HubUpgradeManager.Instance?.PartyManager.RecruitedRoster;
        int count = Mathf.Max(1, roster?.Count ?? 0);
        _cursor = (_cursor + delta + count) % count;
        Render();
    }

    private void HandleAccept()
    {
        var partyManager = HubUpgradeManager.Instance?.PartyManager;
        var roster = partyManager?.RecruitedRoster;
        if (roster == null || roster.Count == 0) return;

        var speciesId = roster[_cursor];
        bool wasActive = partyManager.IsActive(speciesId);
        bool ok = partyManager.ToggleActive(speciesId);

        if (!ok)
            GD.Print($"[Hub] Active party is full ({PartyManager.MaxActiveParty}/{PartyManager.MaxActiveParty}) - remove one first.");
        else
            GD.Print($"[Hub] {speciesId} is now {(!wasActive ? "ACTIVE" : "benched")} for the next run.");

        Render();
    }

    private void Render()
    {
        foreach (Node child in _list.GetChildren())
            child.QueueFree();

        var partyManager = HubUpgradeManager.Instance?.PartyManager;
        var roster = partyManager?.RecruitedRoster;

        if (roster == null || roster.Count == 0)
        {
            _list.AddChild(new Label { Text = "(no recruited pals yet)" });
            return;
        }

        for (int i = 0; i < roster.Count; i++)
        {
            bool active = partyManager.IsActive(roster[i]);
            string checkbox = active ? "[x]" : "[ ]";
            string prefix = i == _cursor ? "> " : "  ";
            _list.AddChild(new Label { Text = $"{prefix}{checkbox} {roster[i]}" });
        }
    }
}
