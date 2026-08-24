namespace MysteryDungeon.Dungeon;

// Dungeon-level meta rules, distinct from DungeonRule (which only
// governs a single floor's BSP generation parameters and is loaded from
// Data/dungeons.json). FloorController.Initialize() takes one of these
// and consults it once _floorNumber reaches MaxFloors to decide how the
// run resolves (see DungeonEndType / GenerateFinalFloor).
public class DungeonConfig
{
    public int MaxFloors { get; set; } = 5;
    public DungeonEndType EndType { get; set; } = DungeonEndType.FreeDungeonBoss;

    // Room-contained Water/Lava pools (see DungeonGenerator's drunkard's-
    // walk blob generation) are a per-dungeon biome choice, not a
    // universal default - off unless a specific dungeon opts in.
    public bool GenerateWaterPools { get; set; } = false;
    public bool GenerateLavaPools { get; set; } = false;
}
