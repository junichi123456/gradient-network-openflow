using Godot;
using MysteryDungeon.Visuals;

namespace MysteryDungeon.Hub;

// Real-time, non-grid movement for the Hub scene - entirely separate
// from the turn-based Entity/GridManager system used in dungeons.
// CharacterBody2D + MoveAndSlide gives smooth free-direction movement
// with real collision against facility/NPC placeholders
// (CollisionShape2D), unlike the dungeon's tile-stepping Entity.MoveTo.
public partial class HubPlayer : CharacterBody2D
{
    [Export] public float Speed { get; set; } = 200f;
    [Export] public Color DebugColor { get; set; } = Colors.Yellow;

    // Cleared while PartySetupUI/DungeonSelectUI is open (see HubScene,
    // which is the single coordinator deciding when an overlay owns
    // input) so the player can't wander around behind the panel.
    public bool InputEnabled { get; set; } = true;

    private const float VisualSize = 28f;

    public override void _Ready()
    {
        // Feet-anchored (Offset shifts the sprite up so its bottom edge
        // sits at this node's origin) - HubScene has y_sort_enabled too,
        // so the player needs the same Y-Sort-correct anchor as dungeon
        // entities (see Entity._Ready()).
        var visual = new Sprite2D
        {
            Texture = SpriteTextureLibrary.GetTexture("", DebugColor, (int)VisualSize),
            Centered = true,
            Offset = new Vector2(0, -VisualSize / 2f),
        };
        AddChild(visual);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!InputEnabled)
        {
            Velocity = Vector2.Zero;
            MoveAndSlide();
            return;
        }

        // Input.GetVector normalizes the result, so diagonal movement
        // isn't faster than cardinal movement.
        var input = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
        Velocity = input * Speed;
        MoveAndSlide();
    }
}
