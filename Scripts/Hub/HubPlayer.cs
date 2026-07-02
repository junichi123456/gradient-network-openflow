using Godot;

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

    public override void _Ready()
    {
        var visual = new ColorRect
        {
            Color = DebugColor,
            Size = new Vector2(28, 28),
            Position = new Vector2(-14, -14),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(visual);
    }

    public override void _PhysicsProcess(double delta)
    {
        // Input.GetVector normalizes the result, so diagonal movement
        // isn't faster than cardinal movement.
        var input = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
        Velocity = input * Speed;
        MoveAndSlide();
    }
}
