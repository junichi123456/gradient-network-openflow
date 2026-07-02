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
}
