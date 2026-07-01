namespace MysteryDungeon.Grid;

public struct Tile
{
    public TerrainType Terrain;
    public bool Explored;

    // -1 = belongs to no room (wall or corridor). DungeonGenerator stamps
    // a room's Id into every tile it carves via GridManager.SetRoomFloor,
    // giving an O(1) "which room is this tile in" lookup (used by
    // FloorController for the Monster House trigger, and later by the
    // Phase 3 field-of-view system: a whole room is visible at once).
    public int RoomId = -1;

    // Phase 1: only Wall blocks movement. Water/Lava/Chasm gain
    // type-based / event-based rules in later phases (Entities, Dungeon).
    public readonly bool IsWalkable => Terrain != TerrainType.Wall;

    public static Tile CreateFloor() => new() { Terrain = TerrainType.Floor };

    public static Tile CreateWall() => new() { Terrain = TerrainType.Wall };
}
