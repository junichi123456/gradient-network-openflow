using Godot;
using System.Collections.Generic;
using MysteryDungeon.Grid;
using MysteryDungeon.Turn;
using MysteryDungeon.Entities;

namespace MysteryDungeon.Dungeon;

// Owns the full lifecycle of "the current floor": generating the map,
// scattering stairs/item/trap placeholders, spawning enemies into the
// TurnScheduler, and tearing all of that down again once the player
// reaches the stairs and the next floor is generated.
//
// Detects the stairs by listening to TurnManager.TurnEnded rather than
// having Player/MoveAction know about stairs directly - Player only
// ever deals with terrain walkability, keeping the two modules decoupled.
public partial class FloorController : Node2D
{
    private const float MarkerSize = 12f;
    private static readonly Color StairsColor = new(0.7f, 0.2f, 1f);   // purple/magenta
    private static readonly Color ItemColor = new(0.2f, 0.9f, 0.2f);   // green
    private static readonly Color TrapColor = new(0.9f, 0.15f, 0.15f); // red

    private GridManager _grid;
    private TurnManager _turnManager;
    private Player _player;
    private DungeonRule _rule;

    private readonly DungeonObjectManager _objects = new();
    private readonly List<Entity> _spawnedEnemies = new();
    private readonly List<Node> _spawnedMarkers = new();

    private int _floorNumber;

    public void Initialize(GridManager grid, TurnManager turnManager, Player player, string dungeonId)
    {
        _grid = grid;
        _turnManager = turnManager;
        _player = player;
        _rule = DungeonRuleLoader.Load(dungeonId);

        _turnManager.TurnEnded += OnTurnEnded;

        GenerateFloor();
    }

    private void OnTurnEnded(int turnNumber)
    {
        if (!_objects.IsStairs(_player.GridPosition)) return;

        GD.Print("[Dungeon] Player stepped on stairs. Progressing to next floor...");
        GenerateFloor();
    }

    private void GenerateFloor()
    {
        CleanupCurrentFloor();

        _floorNumber++;
        var rng = new RandomNumberGenerator();
        rng.Seed = GD.Randi();
        GD.Print($"[Dungeon] Generating floor {_floorNumber} (seed={rng.Seed})");

        var result = new DungeonGenerator().Generate(_grid, _rule, rng);
        if (result.Rooms.Count == 0)
        {
            GD.PushError("[FloorController] DungeonGenerator produced no rooms.");
            return;
        }

        var occupied = new HashSet<Vector2I>();

        var playerRoom = result.Rooms[0];
        var playerPos = RoomCenter(playerRoom);
        _player.PlaceAt(playerPos);
        occupied.Add(playerPos);

        PlaceStairs(result.Rooms, playerRoom, occupied, rng);
        PlaceItemsAndTraps(result.Rooms, occupied, rng);
        SpawnEnemies(result.Rooms, playerRoom, occupied, rng);

        GD.Print($"[Dungeon] Floor {_floorNumber} ready: {result.Rooms.Count} rooms, {_spawnedEnemies.Count} enemies.");
    }

    private void CleanupCurrentFloor()
    {
        foreach (var enemy in _spawnedEnemies)
        {
            _turnManager.UnregisterActor(enemy);
            enemy.QueueFree();
        }
        _spawnedEnemies.Clear();

        foreach (var marker in _spawnedMarkers)
            marker.QueueFree();
        _spawnedMarkers.Clear();

        _objects.Clear();
    }

    private void PlaceStairs(List<Rect2I> rooms, Rect2I playerRoom, HashSet<Vector2I> occupied, RandomNumberGenerator rng)
    {
        var stairsRoom = PickOtherRoom(rooms, playerRoom, rng);
        var pos = RandomFreeTileInRoom(stairsRoom, occupied, rng) ?? RoomCenter(stairsRoom);

        _objects.Set(pos, MapObjectType.Stairs);
        occupied.Add(pos);
        AddMarker(pos, StairsColor);
    }

    private void PlaceItemsAndTraps(List<Rect2I> rooms, HashSet<Vector2I> occupied, RandomNumberGenerator rng)
    {
        foreach (var room in rooms)
        {
            int itemCount = rng.RandiRange(_rule.MinItemsPerRoom, _rule.MaxItemsPerRoom);
            for (int i = 0; i < itemCount; i++)
                TryPlaceObject(room, occupied, rng, MapObjectType.Item, ItemColor);

            int trapCount = rng.RandiRange(_rule.MinTrapsPerRoom, _rule.MaxTrapsPerRoom);
            for (int i = 0; i < trapCount; i++)
                TryPlaceObject(room, occupied, rng, MapObjectType.Trap, TrapColor);
        }
    }

    private void TryPlaceObject(Rect2I room, HashSet<Vector2I> occupied, RandomNumberGenerator rng, MapObjectType type, Color color)
    {
        var pos = RandomFreeTileInRoom(room, occupied, rng);
        if (pos == null) return;

        _objects.Set(pos.Value, type);
        occupied.Add(pos.Value);
        AddMarker(pos.Value, color);
    }

    private void SpawnEnemies(List<Rect2I> rooms, Rect2I playerRoom, HashSet<Vector2I> occupied, RandomNumberGenerator rng)
    {
        int count = rng.RandiRange(_rule.MinEnemyCount, _rule.MaxEnemyCount);
        for (int i = 0; i < count; i++)
        {
            var room = PickOtherRoom(rooms, playerRoom, rng);
            var pos = RandomFreeTileInRoom(room, occupied, rng);
            if (pos == null) continue;

            SpawnEnemyAt(pos.Value, rng);
            occupied.Add(pos.Value);
        }
    }

    private void SpawnEnemyAt(Vector2I pos, RandomNumberGenerator rng)
    {
        Entity enemy = rng.Randf() < _rule.DummyNpcRatio ? new DummyNPC() : new FastNPC();
        AddChild(enemy);
        enemy.Grid = _grid;
        enemy.PlaceAt(pos);

        _turnManager.RegisterActor(enemy);
        _spawnedEnemies.Add(enemy);
    }

    // Picks a random room that isn't `exclude` (the player's room).
    // Falls back to `exclude` itself when it's the only room on the floor.
    private static Rect2I PickOtherRoom(List<Rect2I> rooms, Rect2I exclude, RandomNumberGenerator rng)
    {
        if (rooms.Count == 1) return rooms[0];

        Rect2I room;
        do
        {
            room = rooms[rng.RandiRange(0, rooms.Count - 1)];
        } while (room == exclude);
        return room;
    }

    private static Vector2I? RandomFreeTileInRoom(Rect2I room, HashSet<Vector2I> occupied, RandomNumberGenerator rng)
    {
        const int maxAttempts = 20;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int x = rng.RandiRange(room.Position.X, room.Position.X + room.Size.X - 1);
            int y = rng.RandiRange(room.Position.Y, room.Position.Y + room.Size.Y - 1);
            var pos = new Vector2I(x, y);
            if (!occupied.Contains(pos))
                return pos;
        }
        return null;
    }

    private static Vector2I RoomCenter(Rect2I room) => room.Position + room.Size / 2;

    private void AddMarker(Vector2I pos, Color color)
    {
        var marker = new ColorRect
        {
            Color = color,
            Size = new Vector2(MarkerSize, MarkerSize),
            Position = _grid.GridToWorld(pos) - new Vector2(MarkerSize / 2f, MarkerSize / 2f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(marker);
        _spawnedMarkers.Add(marker);
    }
}
