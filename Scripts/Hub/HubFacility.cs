using Godot;
using MysteryDungeon.Visuals;

namespace MysteryDungeon.Hub;

// Facility placeholder (e.g. "pal_workbench", "pal_bed"): an Area2D the
// HubPlayer can stand inside and press ui_accept to upgrade - "contact
// + Enter" interaction, per the Phase 11 UX spec. A ColorRect visual
// placeholder matches the project's established debug-art convention
// (see Entity's own debug ColorRect).
public partial class HubFacility : Area2D
{
    [Export] public string FacilityId { get; set; } = "pal_workbench";
    [Export] public Color DebugColor { get; set; } = Colors.SaddleBrown;

    private bool _playerInside;

    private const float VisualSize = 36f;

    public override void _Ready()
    {
        // A static prop (workbench/bed), not a character - center-
        // anchored like FloorController's dungeon markers, no feet Offset.
        var visual = new Sprite2D
        {
            Texture = SpriteTextureLibrary.GetTexture("", DebugColor, (int)VisualSize),
            Centered = true,
        };
        AddChild(visual);

        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body is not HubPlayer) return;
        _playerInside = true;
        GD.Print($"[Hub] Press Enter near {FacilityId} to upgrade it (currently Lv.{HubUpgradeManager.Instance?.GetFacilityLevel(FacilityId)}).");
    }

    private void OnBodyExited(Node2D body)
    {
        if (body is HubPlayer) _playerInside = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_playerInside) return;
        if (!@event.IsActionPressed("ui_accept")) return;

        HubUpgradeManager.Instance?.UpgradeFacility(FacilityId);
        GetViewport().SetInputAsHandled();
    }
}
