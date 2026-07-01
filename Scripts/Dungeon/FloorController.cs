using Godot;
using System.Collections.Generic;
using MysteryDungeon.Grid;
using MysteryDungeon.Turn;
using MysteryDungeon.Entities;
using MysteryDungeon.Combat;

namespace MysteryDungeon.Dungeon;

// Owns the full lifecycle of "the current floor": generating the map,
// marking a Monster House, scattering stairs/item/trap placeholders,
// spawning enemies into the TurnScheduler, detecting the player
// stepping onto stairs or into a Monster House, and tearing all of
// that down again when the next floor is generated.
//
// Both the stairs and the Monster House are detected by listening to
// TurnManager.TurnEnded rather than having Player/MoveAction know
// about them directly - Player only ever deals with terrain
// walkability, keeping the two modules decoupled.
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
    private AStarPathfinder _pathfinder;

    private readonly DungeonObjectManager _objects = new();
    private readonly List<Entity> _spawnedEnemies = new();
    private readonly List<(Vector2I Pos, ColorRect Rect)> _spawnedMarkers = new();
    private List<Room> _rooms = new();

    private int _floorNumber;
    private Vector2I _lastPlayerPos;

    // Read-only access for presentation code (MinimapUI/HUD) -
    // FloorController stays the only thing that mutates these.
    public DungeonObjectManager Objects => _objects;
    public IReadOnlyList<Entity> SpawnedEnemies => _spawnedEnemies;
    public int FloorNumber => _floorNumber;

    private static readonly Vector2I[] EightDirections =
    {
        new(0, -1), new(0, 1), new(-1, 0), new(1, 0),
        new(-1, -1), new(1, -1), new(-1, 1), new(1, 1),
    };

    // Entity-occupancy lookup for Player's bump-to-attack input.
    // GridManager only knows about terrain, so this is the closest thing
    // this project has to a "what's standing here" query.
    public Entity GetEnemyAt(Vector2I pos)
    {
        foreach (var enemy in _spawnedEnemies)
            if (GodotObject.IsInstanceValid(enemy) && enemy.IsAlive && enemy.GridPosition == pos)
                return enemy;
        return null;
    }

    // Auto-aim for menu-invoked moves (Phase 6 dropped the manual
    // direction-picker for moves so future room-wide/self-buff moves
    // don't need one): prefers whatever the actor is currently facing,
    // then falls back to the first enemy found among the 8 surrounding
    // tiles. Returns null if nothing is adjacent at all - the move then
    // swings at empty air.
    public Entity FindAutoAimTarget(Vector2I origin, Vector2I facingDirection)
    {
        var facingTarget = GetEnemyAt(origin + facingDirection);
        if (facingTarget != null) return facingTarget;

        foreach (var dir in EightDirections)
        {
            var enemy = GetEnemyAt(origin + dir);
            if (enemy != null) return enemy;
        }

        return null;
    }

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
        // Enemies killed by the player's own action this turn (bump
        // attack) are already QueueFree()'d by Entity.Die() - drop them
        // from our bookkeeping list before anything else touches it.
        PruneDeadEnemies();

        // The player might have just died too (an adjacent enemy's
        // AttackAction runs during the NPC tick, right before this
        // signal fires). Stop here rather than process a dead player's
        // surroundings.
        if (CheckPlayerDeath()) return;

        if (_objects.IsStairs(_player.GridPosition))
        {
            GD.Print("[Dungeon] Player stepped on stairs. Progressing to next floor...");
            GenerateFloor(); // regenerates the floor and refreshes FOV itself
            return;
        }

        // Only auto-pick-up when the player actually stepped onto this
        // tile this turn (position changed) - otherwise resting on top
        // of a just-dropped item (DropItemAction) would immediately
        // re-pick it back up in the same turn.
        if (_player.GridPosition != _lastPlayerPos)
            TryPickupItemAt(_player.GridPosition);
        _lastPlayerPos = _player.GridPosition;

        _player.Stats.TickBelly();
        if (CheckPlayerDeath()) return; // ...or from starvation just now

        CheckMonsterHouseTrigger(); // may spawn enemies before FOV is recomputed below

        RefreshFieldOfView();
    }

    private void PruneDeadEnemies()
    {
        _spawnedEnemies.RemoveAll(e => !GodotObject.IsInstanceValid(e) || !e.IsAlive);
    }

    // Returns true (and triggers game-over) if the player's HP has
    // reached 0. Player.Die() is idempotent, so calling this more than
    // once in the same turn is harmless.
    private bool CheckPlayerDeath()
    {
        if (_player.Stats.IsAlive) return false;
        _player.Die();
        return true;
    }

    // Recomputes which tiles are currently visible/explored, then syncs
    // the Visible flag on every dynamic (enemies) and semi-static
    // (stairs/item/trap markers) presentation node to match.
    private void RefreshFieldOfView()
    {
        FovManager.UpdateVisibility(_grid, _player.GridPosition);

        foreach (var enemy in _spawnedEnemies)
            enemy.Visible = _grid.GetTile(enemy.GridPosition).IsVisible;

        foreach (var (pos, rect) in _spawnedMarkers)
            rect.Visible = _grid.GetTile(pos).IsExplored;
    }

    // O(1): the tile the player is standing on already knows which
    // room it belongs to (Tile.RoomId, stamped by DungeonGenerator).
    private void CheckMonsterHouseTrigger()
    {
        int roomId = _grid.GetRoomId(_player.GridPosition);
        if (roomId < 0 || roomId >= _rooms.Count) return;

        var room = _rooms[roomId];
        if (!room.IsMonsterHouse || room.IsTriggered) return;

        TriggerMonsterHouse(room);
    }

    private void GenerateFloor()
    {
        CleanupCurrentFloor();

        _floorNumber++;
        var rng = new RandomNumberGenerator();
        rng.Seed = GD.Randi();
        GD.Print($"[Dungeon] Generating floor {_floorNumber} (seed={rng.Seed})");

        var result = new DungeonGenerator().Generate(_grid, _rule, rng);
        _rooms = result.Rooms;
        if (_rooms.Count == 0)
        {
            GD.PushError("[FloorController] DungeonGenerator produced no rooms.");
            return;
        }

        // Built once per floor and shared by every chasing enemy - walls
        // don't change mid-floor, so there's no need to rebuild this per
        // entity or per turn (see AStarPathfinder for the full rationale).
        _pathfinder = new AStarPathfinder(_grid);

        var occupied = new HashSet<Vector2I>();

        var playerRoom = _rooms[0];
        _player.PlaceAt(playerRoom.Center);
        _lastPlayerPos = playerRoom.Center;
        occupied.Add(playerRoom.Center);

        MarkMonsterHouse(playerRoom, rng);
        var normalRooms = GetNormalSpawnRooms(playerRoom);

        PlaceStairs(normalRooms, occupied, rng);
        PlaceItemsAndTraps(occupied, rng);
        SpawnEnemies(normalRooms, occupied, rng);

        // First reveal of the new floor - without this the player would
        // see nothing until their first action fires OnTurnEnded.
        RefreshFieldOfView();

        GD.Print($"[Dungeon] Floor {_floorNumber} ready: {_rooms.Count} rooms, {_spawnedEnemies.Count} enemies.");
    }

    private void CleanupCurrentFloor()
    {
        foreach (var enemy in _spawnedEnemies)
        {
            _turnManager.UnregisterActor(enemy);
            enemy.QueueFree();
        }
        _spawnedEnemies.Clear();

        foreach (var (_, rect) in _spawnedMarkers)
            rect.QueueFree();
        _spawnedMarkers.Clear();

        _objects.Clear();
        _rooms = new List<Room>();
    }

    // Rolls once per floor for a single Monster House, chosen from any
    // room other than the player's spawn room.
    private void MarkMonsterHouse(Room playerRoom, RandomNumberGenerator rng)
    {
        if (_rooms.Count <= 1) return;
        if (rng.Randf() >= _rule.MonsterHouseChance) return;

        var candidates = new List<Room>();
        foreach (var room in _rooms)
            if (room.Id != playerRoom.Id)
                candidates.Add(room);

        var chosen = candidates[rng.RandiRange(0, candidates.Count - 1)];
        chosen.IsMonsterHouse = true;
        GD.Print($"[Dungeon] Monster House generated at Room ID: {chosen.Id}, Center: {chosen.Center}");
    }

    // Rooms eligible for stairs / normal enemy spawning: not the
    // player's room, not the (hidden) Monster House room. Computed
    // once per floor and reused, instead of re-rolling per placement.
    private List<Room> GetNormalSpawnRooms(Room playerRoom)
    {
        var candidates = new List<Room>();
        foreach (var room in _rooms)
            if (room.Id != playerRoom.Id && !room.IsMonsterHouse)
                candidates.Add(room);

        return candidates.Count > 0 ? candidates : _rooms;
    }

    private void PlaceStairs(List<Room> candidateRooms, HashSet<Vector2I> occupied, RandomNumberGenerator rng)
    {
        var stairsRoom = candidateRooms[rng.RandiRange(0, candidateRooms.Count - 1)];
        var pos = RandomFreeTileInRoom(stairsRoom.Bounds, occupied, rng) ?? stairsRoom.Center;

        _objects.Set(pos, MapObjectType.Stairs);
        occupied.Add(pos);
        AddMarker(pos, StairsColor);
    }

    // Every room gets its normal item/trap counts, except a Monster
    // House room, which uses the heavier MonsterHouse* range instead -
    // its items/traps are visible immediately (same green/red markers,
    // just far denser), only the enemies stay hidden until triggered.
    private void PlaceItemsAndTraps(HashSet<Vector2I> occupied, RandomNumberGenerator rng)
    {
        var itemIds = ItemDatabase.AllIds();

        foreach (var room in _rooms)
        {
            int minItems = room.IsMonsterHouse ? _rule.MonsterHouseMinItems : _rule.MinItemsPerRoom;
            int maxItems = room.IsMonsterHouse ? _rule.MonsterHouseMaxItems : _rule.MaxItemsPerRoom;
            int itemCount = rng.RandiRange(minItems, maxItems);
            for (int i = 0; i < itemCount; i++)
                TryPlaceItem(room.Bounds, occupied, rng, itemIds);

            int minTraps = room.IsMonsterHouse ? _rule.MonsterHouseMinTraps : _rule.MinTrapsPerRoom;
            int maxTraps = room.IsMonsterHouse ? _rule.MonsterHouseMaxTraps : _rule.MaxTrapsPerRoom;
            int trapCount = rng.RandiRange(minTraps, maxTraps);
            for (int i = 0; i < trapCount; i++)
                TryPlaceObject(room.Bounds, occupied, rng, MapObjectType.Trap, TrapColor);
        }
    }

    private void TryPlaceItem(Rect2I roomBounds, HashSet<Vector2I> occupied, RandomNumberGenerator rng, List<string> itemIds)
    {
        if (itemIds.Count == 0) return;

        var pos = RandomFreeTileInRoom(roomBounds, occupied, rng);
        if (pos == null) return;

        var itemId = itemIds[rng.RandiRange(0, itemIds.Count - 1)];
        _objects.SetItem(pos.Value, itemId);
        occupied.Add(pos.Value);
        AddMarker(pos.Value, ItemColor);
    }

    private void TryPlaceObject(Rect2I roomBounds, HashSet<Vector2I> occupied, RandomNumberGenerator rng, MapObjectType type, Color color)
    {
        var pos = RandomFreeTileInRoom(roomBounds, occupied, rng);
        if (pos == null) return;

        _objects.Set(pos.Value, type);
        occupied.Add(pos.Value);
        AddMarker(pos.Value, color);
    }

    // Called every turn the player doesn't step onto stairs. Consumes
    // the floor item into InventoryManager on success; on failure (bag
    // full) the item is left on the ground for a later attempt.
    private void TryPickupItemAt(Vector2I pos)
    {
        if (_objects.Get(pos) != MapObjectType.Item) return;

        var itemId = _objects.GetItemId(pos);
        var data = ItemDatabase.Get(itemId);
        if (data == null) return;

        if (_player.Inventory.AddItem(itemId))
        {
            _objects.RemoveAt(pos);
            RemoveMarkerAt(pos);
            GD.Print($"[Inventory] Player picked up {data.Name}.");
        }
        else
        {
            GD.Print($"[Inventory] Inventory full, could not pick up {data.Name}.");
        }
    }

    // Places a thrown-and-missed (or dropped) item back onto the floor.
    // If the landing tile is already occupied by another item, scatters
    // to a free adjacent walkable tile instead; Lava/Chasm destroy the
    // item outright, Water leaves it floating (unreachable, but not
    // destroyed) - only Floor tiles are freely walkable/pickup-able.
    public void DropItem(Vector2I pos, string itemId)
    {
        var data = ItemDatabase.Get(itemId);
        string itemName = data?.Name ?? itemId;

        var finalPos = FindDropPosition(pos);
        if (finalPos == null)
        {
            GD.Print($"[Item] {itemName} had nowhere to land near ({pos.X}, {pos.Y}) and was lost.");
            return;
        }

        var terrain = _grid.GetTile(finalPos.Value).Terrain;
        if (terrain == TerrainType.Lava || terrain == TerrainType.Chasm)
        {
            GD.Print($"[Item] {itemName} fell into {terrain} at ({finalPos.Value.X}, {finalPos.Value.Y}) and was destroyed!");
            return;
        }

        if (finalPos.Value != pos)
            GD.Print($"[Item] ({pos.X}, {pos.Y}) was already occupied, so {itemName} scattered to ({finalPos.Value.X}, {finalPos.Value.Y}).");

        _objects.SetItem(finalPos.Value, itemId);
        AddMarker(finalPos.Value, ItemColor);

        if (terrain == TerrainType.Water)
            GD.Print($"[Item] {itemName} landed in the water at ({finalPos.Value.X}, {finalPos.Value.Y}) and floats there.");
        else
            GD.Print($"[Item] {itemName} dropped at ({finalPos.Value.X}, {finalPos.Value.Y}).");
    }

    private Vector2I? FindDropPosition(Vector2I pos)
    {
        if (_objects.Get(pos) != MapObjectType.Item) return pos;

        Vector2I[] neighbors =
        {
            new(0, -1), new(0, 1), new(-1, 0), new(1, 0),
            new(-1, -1), new(1, -1), new(-1, 1), new(1, 1),
        };

        foreach (var dir in neighbors)
        {
            var candidate = pos + dir;
            if (_grid.IsWalkable(candidate) && _objects.Get(candidate) != MapObjectType.Item)
                return candidate;
        }

        return null;
    }

    private void RemoveMarkerAt(Vector2I pos)
    {
        for (int i = _spawnedMarkers.Count - 1; i >= 0; i--)
        {
            if (_spawnedMarkers[i].Pos != pos) continue;
            _spawnedMarkers[i].Rect.QueueFree();
            _spawnedMarkers.RemoveAt(i);
            return;
        }
    }

    private void SpawnEnemies(List<Room> candidateRooms, HashSet<Vector2I> occupied, RandomNumberGenerator rng)
    {
        int count = rng.RandiRange(_rule.MinEnemyCount, _rule.MaxEnemyCount);
        for (int i = 0; i < count; i++)
        {
            var room = candidateRooms[rng.RandiRange(0, candidateRooms.Count - 1)];
            var pos = RandomFreeTileInRoom(room.Bounds, occupied, rng);
            if (pos == null) continue;

            SpawnEnemyAt(pos.Value, rng);
            occupied.Add(pos.Value);
        }
    }

    // Fired once, the instant the player steps into a Monster House
    // room: dumps a burst of enemies into its free floor tiles and
    // permanently marks the room as triggered so this never fires again.
    private void TriggerMonsterHouse(Room room)
    {
        room.IsTriggered = true;
        GD.Print("[Dungeon] ⚠️ MONSTER HOUSE TRIGGERED! ⚠️");

        var rng = new RandomNumberGenerator();
        rng.Seed = GD.Randi();

        var occupied = CollectOccupiedTilesInRoom(room);
        int count = rng.RandiRange(_rule.MonsterHouseMinEnemies, _rule.MonsterHouseMaxEnemies);
        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            var pos = RandomFreeTileInRoom(room.Bounds, occupied, rng);
            if (pos == null) break; // room is full, stop trying

            SpawnEnemyAt(pos.Value, rng);
            occupied.Add(pos.Value);
            spawned++;
        }

        GD.Print($"[Dungeon] Spawned {spawned} enemies in the Monster House (Room ID: {room.Id}).");
    }

    private HashSet<Vector2I> CollectOccupiedTilesInRoom(Room room)
    {
        var occupied = new HashSet<Vector2I> { _player.GridPosition };

        foreach (var enemy in _spawnedEnemies)
            if (_grid.GetRoomId(enemy.GridPosition) == room.Id)
                occupied.Add(enemy.GridPosition);

        var bounds = room.Bounds;
        for (int x = bounds.Position.X; x < bounds.Position.X + bounds.Size.X; x++)
            for (int y = bounds.Position.Y; y < bounds.Position.Y + bounds.Size.Y; y++)
            {
                var pos = new Vector2I(x, y);
                if (_objects.Get(pos) != MapObjectType.None)
                    occupied.Add(pos);
            }

        return occupied;
    }

    private void SpawnEnemyAt(Vector2I pos, RandomNumberGenerator rng)
    {
        HostileEntity enemy = rng.Randf() < _rule.DummyNpcRatio ? new DummyNPC() : new FastNPC();
        AddChild(enemy);
        enemy.Grid = _grid;
        enemy.Pathfinder = _pathfinder;
        enemy.TargetPlayer = _player;
        enemy.PlaceAt(pos);
        enemy.Visible = false; // hidden until RefreshFieldOfView() reveals it

        _turnManager.RegisterActor(enemy);
        _spawnedEnemies.Add(enemy);
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

    private void AddMarker(Vector2I pos, Color color)
    {
        var marker = new ColorRect
        {
            Color = color,
            Size = new Vector2(MarkerSize, MarkerSize),
            Position = _grid.GridToWorld(pos) - new Vector2(MarkerSize / 2f, MarkerSize / 2f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false, // hidden until RefreshFieldOfView() reveals it
        };
        AddChild(marker);
        _spawnedMarkers.Add((pos, marker));
    }
}
