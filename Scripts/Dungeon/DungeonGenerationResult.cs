using Godot;
using System.Collections.Generic;

namespace MysteryDungeon.Dungeon;

// What DungeonGenerator hands back after carving a floor into a
// GridManager - consumed by TestScene today (room-center spawn points)
// and by ObjectPlacer/MonsterHouseGenerator in the next Phase 2 steps.
public class DungeonGenerationResult
{
    public List<Rect2I> Rooms { get; } = new();
}
