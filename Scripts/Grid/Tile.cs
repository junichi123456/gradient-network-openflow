namespace MysteryDungeon.Grid;

public struct Tile
{
    public TerrainType Terrain;
    public bool Explored;

    // Phase 1: only Wall blocks movement. Water/Lava/Chasm gain
    // type-based / event-based rules in later phases (Entities, Dungeon).
    public readonly bool IsWalkable => Terrain != TerrainType.Wall;

    public static Tile CreateFloor() => new() { Terrain = TerrainType.Floor };

    public static Tile CreateWall() => new() { Terrain = TerrainType.Wall };
}
