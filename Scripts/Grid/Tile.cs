namespace MysteryDungeon.Grid;

public struct Tile
{
    public TerrainType Terrain;

    // Ever been seen -> terrain/marker memory. Persists once true; only
    // GridManager.Resize() (i.e. a brand new floor) resets it.
    public bool IsExplored;

    // Currently in the player's line of sight this turn -> gates dynamic
    // entities (enemies). Recomputed every turn by FovManager.
    public bool IsVisible;

    // -1 = belongs to no room (wall or corridor). DungeonGenerator stamps
    // a room's Id into every tile it carves via GridManager.SetRoomFloor,
    // giving an O(1) "which room is this tile in" lookup - used by
    // FloorController's Monster House trigger and by FovManager to
    // reveal a whole room at once.
    public int RoomId = -1;

    // Phase 1: only Wall blocks movement. Water/Lava/Chasm gain
    // type-based / event-based rules in later phases (Entities, Dungeon).
    public readonly bool IsWalkable => Terrain != TerrainType.Wall;

    public static Tile CreateFloor() => new() { Terrain = TerrainType.Floor };

    public static Tile CreateWall() => new() { Terrain = TerrainType.Wall };
}
