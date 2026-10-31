using System.Collections.Generic;

namespace MysteryDungeon.Dungeon;

// What DungeonGenerator hands back after carving a floor into a
// GridManager - consumed by FloorController for room-center spawn
// points, stairs/object/enemy placement, and Monster House lookups.
public class DungeonGenerationResult
{
    public List<Room> Rooms { get; } = new();
}
