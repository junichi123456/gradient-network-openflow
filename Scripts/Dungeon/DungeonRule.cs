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

    // Item/trap placeholder counts, rolled per room independently.
    public int MinItemsPerRoom { get; set; } = 1;
    public int MaxItemsPerRoom { get; set; } = 3;
    public int MinTrapsPerRoom { get; set; } = 0;
    public int MaxTrapsPerRoom { get; set; } = 2;

    // Total enemy count for the floor (excluding the player's room).
    public int MinEnemyCount { get; set; } = 3;
    public int MaxEnemyCount { get; set; } = 6;

    // Chance a spawned enemy is a DummyNPC rather than a FastNPC.
    // No monster database yet (arrives in Phase 3), so this is the
    // only "species" knob until then.
    public float DummyNpcRatio { get; set; } = 0.7f;
}
