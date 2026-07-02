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

    private const int BossRoomSize = 20;

    private GridManager _grid;
    private TurnManager _turnManager;
    private Player _player;
    private DungeonRule _rule;
    private DungeonConfig _config;
    private AStarPathfinder _pathfinder;
    private BossEntity _bossEntity; // non-null only on a FreeDungeonBoss final floor
    private bool _isGameCleared;
    private bool _storyEventCompleted; // framework hook for DungeonEndType.StoryNoBossFinalFloor

    private readonly DungeonObjectManager _objects = new();
    private readonly List<Entity> _spawnedEnemies = new();
    private readonly List<AllyEntity> _spawnedAllies = new();
    private readonly List<(Vector2I Pos, ColorRect Rect)> _spawnedMarkers = new();
    private List<Room> _rooms = new();

    // Persist for the whole run (NOT cleared in CleanupCurrentFloor,
    // unlike _spawnedEnemies/_objects/_rooms) - RunTracker counts kills
    // across every floor, and PartyManager's roster survives floor
    // transitions the same way the player's own stats do.
    private readonly RunTracker _runTracker = new();
    private readonly PartyManager _partyManager = new();

    private int _floorNumber;
    private Vector2I _lastPlayerPos;

    // Read-only access for presentation code (MinimapUI/HUD) -
    // FloorController stays the only thing that mutates these.
    public DungeonObjectManager Objects => _objects;
    public IReadOnlyList<Entity> SpawnedEnemies => _spawnedEnemies;
    public IReadOnlyList<AllyEntity> SpawnedAllies => _spawnedAllies;
    public int FloorNumber => _floorNumber;
    public RunTracker RunTracker => _runTracker;
    public PartyManager PartyManager => _partyManager;

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

    // Full occupancy lookup across all three groups (player, allies,
    // enemies). Movement code checks this before stepping so two
    // entities can never end up stacked on the same tile.
    public Entity GetEntityAt(Vector2I pos)
    {
        if (_player != null && _player.IsAlive && _player.GridPosition == pos) return _player;

        foreach (var ally in _spawnedAllies)
            if (GodotObject.IsInstanceValid(ally) && ally.IsAlive && ally.GridPosition == pos)
                return ally;

        return GetEnemyAt(pos);
    }

    // Auto-aim for menu-invoked moves (Phase 6 dropped the manual
    // direction-picker for moves so future room-wide/self-buff moves
    // don't need one): prefers whatever the actor is currently facing,
    // then falls back to the first enemy found among the 8 surrounding
    // tiles. Diagonal candidates blocked by a Wall shoulder
    // (GridManager.CanCutCorner) are skipped - a move can't bend around
    // a wall corner. Returns null if nothing reachable is adjacent -
    // the move then swings at empty air.
    public Entity FindAutoAimTarget(Vector2I origin, Vector2I facingDirection)
    {
        var facingPos = origin + facingDirection;
        var facingTarget = GetEnemyAt(facingPos);
        if (facingTarget != null && _grid.CanCutCorner(origin, facingPos)) return facingTarget;

        foreach (var dir in EightDirections)
        {
            var pos = origin + dir;
            var enemy = GetEnemyAt(pos);
            if (enemy != null && _grid.CanCutCorner(origin, pos)) return enemy;
        }

        return null;
    }

    public void Initialize(GridManager grid, TurnManager turnManager, Player player, string dungeonId, DungeonConfig config)
    {
        _grid = grid;
        _turnManager = turnManager;
        _player = player;
        _rule = DungeonRuleLoader.Load(dungeonId);
        _config = config;

        _turnManager.TurnEnded += OnTurnEnded;

        GenerateFloor();
    }

    // Framework hook for DungeonEndType.StoryNoBossFinalFloor (end
    // pattern 3): a future scenario/event-flag system calls this once
    // its trigger condition completes; OnTurnEnded then clears the
    // dungeon the same way a boss kill or EscapePortal would. No actual
    // story-event system exists yet, so nothing calls this today.
    public void CompleteStoryEvent() => _storyEventCompleted = true;

    private void OnTurnEnded(int turnNumber)
    {
        // Enemies killed by the player's own action this turn (bump
        // attack) are already QueueFree()'d by Entity.Die() - drop them
        // from our bookkeeping list before anything else touches it.
        PruneDeadEnemies();
        PruneDeadAllies();

        // The player might have just died too (an adjacent enemy's
        // AttackAction runs during the NPC tick, right before this
        // signal fires). Stop here rather than process a dead player's
        // surroundings.
        if (CheckPlayerDeath()) return;

        // Once cleared, nothing else in this method should run again -
        // HandleDungeonCleared() already disabled player input, but this
        // is a belt-and-suspenders guard against any further processing.
        if (_isGameCleared) return;

        // IsAlive flips false the instant Entity.Die() runs (before the
        // deferred QueueFree()), so this is safe to check every turn
        // without an IsInstanceValid race.
        if (_bossEntity != null && !_bossEntity.IsAlive)
        {
            HandleDungeonCleared();
            return;
        }

        // Both no-boss clear conditions only count on the FINAL floor of
        // their matching EndType - a stray story flag or portal object on
        // an earlier floor must never end the run.
        bool onFinalFloor = _floorNumber >= _config.MaxFloors;

        if (onFinalFloor && _config.EndType == DungeonEndType.StoryNoBossFinalFloor && _storyEventCompleted)
        {
            HandleDungeonCleared();
            return;
        }

        if (onFinalFloor && _config.EndType == DungeonEndType.FreeDungeonNoBossFinalFloor
            && _objects.Get(_player.GridPosition) == MapObjectType.EscapePortal)
        {
            HandleDungeonCleared();
            return;
        }

        if (_objects.IsStairs(_player.GridPosition))
        {
            NextFloor();
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

    private void PruneDeadAllies()
    {
        int before = _spawnedAllies.Count;
        _spawnedAllies.RemoveAll(a => !GodotObject.IsInstanceValid(a) || !a.IsAlive);

        // A fallen ally breaks the conga line - re-link the whole chain
        // (Player -> survivor 1 -> survivor 2 ...) so the followers
        // behind the gap don't keep trailing a dead leader's last
        // footprint forever.
        if (_spawnedAllies.Count != before)
            RelinkFollowChain();
    }

    private void RelinkFollowChain()
    {
        Entity previous = _player;
        foreach (var ally in _spawnedAllies)
        {
            ally.TargetToFollow = previous;
            previous = ally;
        }
    }

    private void NextFloor()
    {
        GD.Print("[Dungeon] Player stepped on stairs. Progressing to next floor...");
        GenerateFloor(); // regenerates the floor and refreshes FOV itself
    }

    // Common "the run is over, in a win" path for every DungeonEndType:
    // a boss kill, an EscapePortal step, or a completed story event all
    // funnel through here. Stops further play (mirrors Player.Die()'s
    // DisableInput() on the loss side) and rolls RunTracker's kills into
    // CompleteDungeon()'s recruitment results.
    private void HandleDungeonCleared()
    {
        if (_isGameCleared) return;
        _isGameCleared = true;

        _player.DisableInput();
        GD.Print("[Game] 🎉 DUNGEON CLEARED! 🎉");
        CompleteDungeon();
    }

    // Rolls RunTracker's accumulated kills into party-join attempts and,
    // on success, adds the species to PartyManager's roster. Also
    // callable directly for testing/verification without having to
    // actually reach a win condition.
    public void CompleteDungeon()
    {
        GD.Print("[Dungeon] Calculating recruitment results...");

        var rng = new RandomNumberGenerator();
        rng.Seed = GD.Randi();

        foreach (var speciesId in _runTracker.CompleteDungeon(rng))
            _partyManager.AddMember(speciesId);
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

        foreach (var ally in _spawnedAllies)
            ally.Visible = _grid.GetTile(ally.GridPosition).IsVisible;

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

    // Entry point for every floor transition (initial spawn and
    // NextFloor() alike). Handles the shared cleanup/bookkeeping, then
    // branches: everything before DungeonConfig.MaxFloors uses the
    // normal BSP pipeline, the final floor's shape depends on
    // DungeonConfig.EndType (see GenerateFinalFloor).
    private void GenerateFloor()
    {
        CleanupCurrentFloor();
        _floorNumber++;

        if (_floorNumber >= _config.MaxFloors)
        {
            GenerateFinalFloor();
            return;
        }

        GenerateNormalFloor();
    }

    private void GenerateNormalFloor()
    {
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
        SpawnPartyMembers(occupied);

        // First reveal of the new floor - without this the player would
        // see nothing until their first action fires OnTurnEnded.
        RefreshFieldOfView();

        GD.Print($"[Dungeon] Floor {_floorNumber} ready: {_rooms.Count} rooms, {_spawnedEnemies.Count} enemies.");
    }

    // DungeonConfig.MaxFloors reached - which of the 5 end patterns
    // applies depends on DungeonConfig.EndType. Only FreeDungeonBoss is
    // implemented as of Phase 8; the others log a placeholder so the
    // branch point exists for their own future phases.
    private void GenerateFinalFloor()
    {
        GD.Print($"[Dungeon] Reached the final floor (Floor {_floorNumber}, EndType={_config.EndType}).");

        switch (_config.EndType)
        {
            case DungeonEndType.FreeDungeonBoss:
                GenerateFreeDungeonBossFloor();
                break;

            case DungeonEndType.StoryBoss:
                GD.PrintErr("[Dungeon] DungeonEndType.StoryBoss final-floor generation: Not implemented yet. Falling back to a normal floor.");
                GenerateNormalFloor();
                break;

            case DungeonEndType.StoryMultiEnemyBattle:
                GD.PrintErr("[Dungeon] DungeonEndType.StoryMultiEnemyBattle final-floor generation: Not implemented yet. Falling back to a normal floor.");
                GenerateNormalFloor();
                break;

            case DungeonEndType.StoryNoBossFinalFloor:
                // No special floor shape (framework only, per spec) -
                // the clear itself is driven by CompleteStoryEvent() /
                // OnTurnEnded's _storyEventCompleted check.
                GD.PrintErr("[Dungeon] DungeonEndType.StoryNoBossFinalFloor final-floor generation: Not implemented yet. Falling back to a normal floor.");
                GenerateNormalFloor();
                break;

            case DungeonEndType.FreeDungeonNoBossFinalFloor:
                // EscapePortal placement isn't implemented yet - only
                // the MapObjectType.EscapePortal identifier and
                // OnTurnEnded's step-on-it check exist so far.
                GD.PrintErr("[Dungeon] DungeonEndType.FreeDungeonNoBossFinalFloor final-floor generation: Not implemented yet. Falling back to a normal floor.");
                GenerateNormalFloor();
                break;

            default:
                GD.PrintErr($"[Dungeon] Unhandled DungeonEndType {_config.EndType}. Falling back to a normal floor.");
                GenerateNormalFloor();
                break;
        }
    }

    // End pattern 4: a single BossRoomSize x BossRoomSize room (no BSP
    // generation, no stairs/items/traps/normal enemies) with one
    // BossEntity at its center. The party still spawns normally.
    private void GenerateFreeDungeonBossFloor()
    {
        int gridSize = BossRoomSize + 2; // 1-tile wall border
        _grid.Resize(gridSize, gridSize, TerrainType.Wall);

        var bounds = new Rect2I(1, 1, BossRoomSize, BossRoomSize);
        var bossRoom = new Room(0, bounds);
        _rooms = new List<Room> { bossRoom };

        for (int x = bounds.Position.X; x < bounds.Position.X + bounds.Size.X; x++)
            for (int y = bounds.Position.Y; y < bounds.Position.Y + bounds.Size.Y; y++)
                _grid.SetRoomFloor(new Vector2I(x, y), bossRoom.Id);

        _pathfinder = new AStarPathfinder(_grid);

        var playerPos = bounds.Position + new Vector2I(1, 1); // corner, away from the boss at center
        _player.PlaceAt(playerPos);
        _lastPlayerPos = playerPos;

        var occupied = new HashSet<Vector2I> { playerPos };
        SpawnPartyMembers(occupied);

        _bossEntity = new BossEntity();
        AddChild(_bossEntity);
        _bossEntity.Grid = _grid;
        _bossEntity.Pathfinder = _pathfinder;
        _bossEntity.TargetPlayer = _player;
        _bossEntity.FloorController = this;
        _bossEntity.Stats.Level += (_floorNumber - 1) * 2; // same per-floor scaling as normal enemies
        _bossEntity.PlaceAt(bossRoom.Center);
        _bossEntity.Visible = false; // hidden until RefreshFieldOfView() reveals it

        _turnManager.RegisterActor(_bossEntity);
        _spawnedEnemies.Add(_bossEntity);

        RefreshFieldOfView();

        GD.Print($"[Dungeon] Boss floor {_floorNumber} ready: {_bossEntity.ActorName} (Lv {_bossEntity.Stats.Level}, HP {_bossEntity.Stats.MaxHp}) spawned at {bossRoom.Center}.");
    }

    private void CleanupCurrentFloor()
    {
        foreach (var enemy in _spawnedEnemies)
        {
            _turnManager.UnregisterActor(enemy);
            enemy.QueueFree();
        }
        _spawnedEnemies.Clear();
        _bossEntity = null;

        // Allies are rebuilt fresh from PartyManager's roster every
        // floor (same as enemies) - only the roster itself (who's in
        // the party) survives the transition, not the live instances.
        foreach (var ally in _spawnedAllies)
        {
            _turnManager.UnregisterActor(ally);
            ally.QueueFree();
        }
        _spawnedAllies.Clear();

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

    // A tile can hold an item only if NO map object occupies it - not
    // just "no item": Stairs/Trap/EscapePortal tiles must scatter the
    // drop too, or _objects.SetItem would silently overwrite them (a
    // thrown item landing on the stairs used to delete the stairs and
    // strand the player on the floor).
    private Vector2I? FindDropPosition(Vector2I pos)
    {
        if (_objects.Get(pos) == MapObjectType.None) return pos;

        Vector2I[] neighbors =
        {
            new(0, -1), new(0, 1), new(-1, 0), new(1, 0),
            new(-1, -1), new(1, -1), new(-1, 1), new(1, 1),
        };

        foreach (var dir in neighbors)
        {
            var candidate = pos + dir;
            if (_grid.IsWalkable(candidate) && _objects.Get(candidate) == MapObjectType.None)
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
        enemy.FloorController = this;
        enemy.Stats.Level += (_floorNumber - 1) * 2; // deeper floors -> stronger enemies, on top of each species' own base level
        enemy.PlaceAt(pos);
        enemy.Visible = false; // hidden until RefreshFieldOfView() reveals it

        _turnManager.RegisterActor(enemy);
        _spawnedEnemies.Add(enemy);
    }

    // Spawns PartyManager's whole roster around the player's spawn tile,
    // in roster order, chaining each ally's TargetToFollow to the
    // previously-spawned member (Player -> Ally1 -> Ally2 -> Ally3) -
    // see AllyEntity for how that chain drives the follow behavior.
    private void SpawnPartyMembers(HashSet<Vector2I> occupied)
    {
        Entity previous = _player;

        foreach (var speciesId in _partyManager.AllMemberSpeciesIds())
        {
            var pos = FindFreeAdjacentTile(previous.GridPosition, occupied);

            var ally = new AllyEntity { SpeciesId = speciesId };
            AddChild(ally);
            ally.Grid = _grid;
            ally.Pathfinder = _pathfinder;
            ally.FloorController = this;
            ally.TargetToFollow = previous;
            ally.PlaceAt(pos);
            ally.Visible = false; // hidden until RefreshFieldOfView() reveals it

            _turnManager.RegisterActor(ally);
            _spawnedAllies.Add(ally);
            occupied.Add(pos);

            previous = ally;
        }
    }

    private Vector2I FindFreeAdjacentTile(Vector2I center, HashSet<Vector2I> occupied)
    {
        foreach (var dir in EightDirections)
        {
            var candidate = center + dir;
            if (_grid.IsWalkable(candidate) && !occupied.Contains(candidate))
                return candidate;
        }
        return center; // extremely unlikely: no free tile nearby at all
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
