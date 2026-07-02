using Godot;

namespace MysteryDungeon.Hub;

// "Pal Box" placeholder: an Area2D the HubPlayer can stand inside and
// press ui_accept to open PartySetupUI - same "contact + Enter"
// interaction as HubFacility, but opens a UI overlay instead of
// upgrading something directly.
public partial class HubPalBoxTrigger : Area2D
{
    [Export] public NodePath PartySetupUIPath { get; set; }
    [Export] public Color DebugColor { get; set; } = Colors.SkyBlue;

    private PartySetupUI _ui;
    private bool _playerInside;

    public override void _Ready()
    {
        var visual = new ColorRect
        {
            Color = DebugColor,
            Size = new Vector2(36, 36),
            Position = new Vector2(-18, -18),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(visual);

        _ui = GetNode<PartySetupUI>(PartySetupUIPath);

        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body is not HubPlayer) return;
        _playerInside = true;
        GD.Print("[Hub] Press Enter near the Pal Box to manage your party.");
    }

    private void OnBodyExited(Node2D body)
    {
        if (body is HubPlayer) _playerInside = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_playerInside || _ui.IsOpen) return;
        if (!@event.IsActionPressed("ui_accept")) return;

        _ui.Open();
        GetViewport().SetInputAsHandled();
    }
}
