using Godot;

namespace MysteryDungeon.Hub;

// "Dungeon Gate" placeholder: an Area2D the HubPlayer can stand inside
// and press ui_accept to open DungeonSelectUI - same "contact + Enter"
// interaction as HubFacility/HubPalBoxTrigger.
public partial class HubDungeonGateTrigger : Area2D
{
    [Export] public NodePath DungeonSelectUIPath { get; set; }
    [Export] public Color DebugColor { get; set; } = Colors.MediumPurple;

    private DungeonSelectUI _ui;
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

        _ui = GetNode<DungeonSelectUI>(DungeonSelectUIPath);

        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body is not HubPlayer) return;
        _playerInside = true;
        GD.Print("[Hub] Press Enter near the Dungeon Gate to choose a destination.");
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
