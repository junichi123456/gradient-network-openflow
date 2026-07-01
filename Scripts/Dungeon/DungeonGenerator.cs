using Godot;
using MysteryDungeon.Grid;

namespace MysteryDungeon.Dungeon;

// Entry point for map generation: BSP-splits the map, carves one room
// per leaf, connects them with L-shaped corridors, and writes the
// result directly into the given GridManager. Pure logic class (not a
// Node) so it stays independently testable and reusable for floor
// transitions later.
//
// Takes an already-seeded RandomNumberGenerator (rather than a seed)
// so the caller can keep using the same instance afterward to place
// stairs/items/traps/enemies - the whole floor then reproduces from
// one seed, not just its terrain.
public class DungeonGenerator
{
    public DungeonGenerationResult Generate(GridManager grid, DungeonRule rule, RandomNumberGenerator rng)
    {
        GD.Print($"[DungeonGenerator] generating {rule.MapWidth}x{rule.MapHeight} map (seed={rng.Seed})");

        grid.Resize(rule.MapWidth, rule.MapHeight, TerrainType.Wall);

        var bounds = new Rect2I(Vector2I.Zero, new Vector2I(rule.MapWidth, rule.MapHeight));
        var tree = new BspTree(bounds, rule, rng);

        var roomLeaves = RoomPlacer.PlaceRooms(tree.Root, rule, rng);

        var result = new DungeonGenerationResult();
        for (int i = 0; i < roomLeaves.Count; i++)
        {
            var room = new Room(i, roomLeaves[i].RoomRect);
            result.Rooms.Add(room);
            CarveRoom(grid, room);
        }

        CorridorConnector.Connect(tree.Root, grid, rng);

        grid.QueueRedraw();

        GD.Print($"[DungeonGenerator] done: {result.Rooms.Count} rooms carved");
        return result;
    }

    private static void CarveRoom(GridManager grid, Room room)
    {
        var bounds = room.Bounds;
        for (int x = bounds.Position.X; x < bounds.Position.X + bounds.Size.X; x++)
            for (int y = bounds.Position.Y; y < bounds.Position.Y + bounds.Size.Y; y++)
                grid.SetRoomFloor(new Vector2I(x, y), room.Id);
    }
}
