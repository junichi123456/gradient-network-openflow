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

    // Chance one non-player room is marked as a Monster House at
    // generation time (hidden until the player steps into it).
    public float MonsterHouseChance { get; set; } = 0.2f;

    // Item/trap placeholder counts, rolled per room independently.
    public int MinItemsPerRoom { get; set; } = 1;
    public int MaxItemsPerRoom { get; set; } = 3;
    public int MinTrapsPerRoom { get; set; } = 0;
    public int MaxTrapsPerRoom { get; set; } = 2;

    // A Monster House room gets its own (heavier) item/trap counts
    // instead of the normal per-room range above.
    public int MonsterHouseMinItems { get; set; } = 4;
    public int MonsterHouseMaxItems { get; set; } = 6;
    public int MonsterHouseMinTraps { get; set; } = 2;
    public int MonsterHouseMaxTraps { get; set; } = 4;

    // Total enemy count for the floor (excluding the player's room and
    // any Monster House room - that one stays empty until triggered).
    public int MinEnemyCount { get; set; } = 3;
    public int MaxEnemyCount { get; set; } = 6;

    // Enemies dumped into a Monster House the instant it's triggered.
    public int MonsterHouseMinEnemies { get; set; } = 5;
    public int MonsterHouseMaxEnemies { get; set; } = 8;

    // Chance a spawned enemy is a DummyNPC rather than a FastNPC.
    // No monster database yet (arrives in Phase 3), so this is the
    // only "species" knob until then.
    public float DummyNpcRatio { get; set; } = 0.7f;

    // The dungeon's own weather, applied to every floor at generation
    // and never expiring on its own (a move/trait can override it
    // temporarily - see WeatherState). None = no weather, which is the
    // default and a strict no-op everywhere.
    public WeatherType Weather { get; set; } = WeatherType.None;

    // Per-room roll chance for a Water/Lava pool (see DungeonGenerator's
    // drunkard's-walk blob generation). Only consulted at all when
    // DungeonConfig.GenerateWaterPools/GenerateLavaPools is true.
    public float WaterPoolChance { get; set; } = 0.15f;
    public float LavaPoolChance { get; set; } = 0.08f;
    public int PoolMinSize { get; set; } = 2;
    public int PoolMaxSize { get; set; } = 5;
}
