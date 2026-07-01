using Godot;

namespace MysteryDungeon.Dungeon;

// Generation parameters for one floor. Mirrors the "generation" block
// of Data/dungeons.json (see docs/architecture/ARCHITECTURE.md and
// Scripts/Dungeon/DungeonRuleLoader.cs). Plain data holder, no logic.
public class DungeonRule
{
    public int MapWidth { get; set; } = 50;
    public int MapHeight { get; set; } = 30;

    // BSP stops splitting a region once it would produce a child
    // smaller than this on either axis.
    public int MinLeafSize { get; set; } = 8;

    // Each leaf's room is sized randomly within these bounds (clamped
    // to fit inside the leaf, minus a 1-tile wall margin).
    public Vector2I RoomMinSize { get; set; } = new(4, 4);
    public Vector2I RoomMaxSize { get; set; } = new(9, 7);

    // Consumed starting Phase 2 step 3 (Monster House trigger).
    public float MonsterHouseChance { get; set; } = 0.1f;
}
