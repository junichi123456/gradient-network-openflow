using Godot;
using System.Collections.Generic;
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
    // generateWaterPools/generateLavaPools default to false - hazard
    // pools are a per-dungeon biome choice (DungeonConfig), not a
    // universal default, so a caller has to opt in explicitly.
    public DungeonGenerationResult Generate(GridManager grid, DungeonRule rule, RandomNumberGenerator rng,
        bool generateWaterPools = false, bool generateLavaPools = false)
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

        if (generateWaterPools || generateLavaPools)
            GenerateHazardPools(grid, rule, result, rng, generateWaterPools, generateLavaPools);

        grid.RefreshTileMap();

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

    // Rolls each room independently for a Water pool and/or a Lava pool
    // (both can land in the same room, in different spots). Corridors
    // are never touched - pools only ever grow inside a room's own
    // interior (see GenerateHazardBlob), so no hazard can ever sever the
    // only path between two rooms.
    private static void GenerateHazardPools(GridManager grid, DungeonRule rule, DungeonGenerationResult result,
        RandomNumberGenerator rng, bool generateWaterPools, bool generateLavaPools)
    {
        foreach (var room in result.Rooms)
        {
            if (generateWaterPools && rng.Randf() < rule.WaterPoolChance)
                GenerateHazardBlob(grid, room, TerrainType.Water, rule, rng);

            if (generateLavaPools && rng.Randf() < rule.LavaPoolChance)
                GenerateHazardBlob(grid, room, TerrainType.Lava, rule, rng);
        }
    }

    // Drunkard's-walk blob confined to the room's interior: a 1-tile
    // margin from the room's boundary ring (where corridors attach) is
    // never touched, and Room.Center is always excluded (kept Floor for
    // player-spawn/stairs-fallback safety - see FloorController). Since
    // the boundary ring always stays walkable and the blob is capped to
    // a handful of tiles, a walkable route around the pool is always
    // structurally guaranteed within the room.
    private static void GenerateHazardBlob(GridManager grid, Room room, TerrainType terrain, DungeonRule rule, RandomNumberGenerator rng)
    {
        var bounds = room.Bounds;
        int minX = bounds.Position.X + 1;
        int maxX = bounds.Position.X + bounds.Size.X - 2;
        int minY = bounds.Position.Y + 1;
        int maxY = bounds.Position.Y + bounds.Size.Y - 2;

        if (minX > maxX || minY > maxY) return; // room too small for a safe interior margin

        var center = room.Center;
        var current = new Vector2I(rng.RandiRange(minX, maxX), rng.RandiRange(minY, maxY));

        var blob = new HashSet<Vector2I>();
        int targetSize = rng.RandiRange(rule.PoolMinSize, rule.PoolMaxSize);
        Vector2I[] dirs = { new(0, -1), new(0, 1), new(-1, 0), new(1, 0) };

        int attempts = 0;
        int maxAttempts = targetSize * 10;
        while (blob.Count < targetSize && attempts < maxAttempts)
        {
            attempts++;
            if (current != center) blob.Add(current);

            var next = current + dirs[rng.RandiRange(0, dirs.Length - 1)];
            if (next.X < minX || next.X > maxX || next.Y < minY || next.Y > maxY) continue; // stay inside the margin
            current = next;
        }

        if (blob.Count == 0) return;

        foreach (var pos in blob)
            grid.SetTerrain(pos, terrain);

        GD.Print($"[DungeonGenerator] Generated a {terrain} pool ({blob.Count} tiles) in Room ID: {room.Id}.");
    }
}
