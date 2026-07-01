using Godot;

namespace MysteryDungeon.Grid;

// Owns the logical Tile[,] grid, grid<->world coordinate conversion,
// and walkability queries. Rendering is a debug placeholder (_Draw)
// until real tile art / TileMap is introduced later.
//
// The grid starts as a solid Width x Height wall block; callers (e.g.
// Dungeon.DungeonGenerator) call Resize()/SetTerrain() to carve it into
// an actual floor plan. This keeps GridManager a dumb data holder with
// no generation logic of its own.
public partial class GridManager : Node2D
{
    [Export] public int Width { get; set; } = 15;
    [Export] public int Height { get; set; } = 10;
    [Export] public int TileSize { get; set; } = 32;

    private Tile[,] _tiles;

    public override void _Ready()
    {
        Resize(Width, Height, TerrainType.Wall);
    }

    // Reallocates the tile array at the given dimensions, filled entirely
    // with `fill`. Also updates Width/Height so GridToWorld/_Draw/etc.
    // stay in sync with the new size.
    public void Resize(int width, int height, TerrainType fill = TerrainType.Wall)
    {
        Width = width;
        Height = height;
        _tiles = new Tile[Width, Height];
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                _tiles[x, y] = new Tile { Terrain = fill };
        QueueRedraw();
    }

    public void SetTerrain(Vector2I pos, TerrainType terrain)
    {
        if (!InBounds(pos)) return;
        _tiles[pos.X, pos.Y].Terrain = terrain;
    }

    // Room-carving specific: sets the tile to Floor and stamps which
    // room it belongs to in one call. Corridor carving (SetTerrain)
    // deliberately never touches RoomId, so corridors stay RoomId=-1.
    public void SetRoomFloor(Vector2I pos, int roomId)
    {
        if (!InBounds(pos)) return;
        _tiles[pos.X, pos.Y].Terrain = TerrainType.Floor;
        _tiles[pos.X, pos.Y].RoomId = roomId;
    }

    // O(1) "which room is this tile in" lookup; -1 if none (wall/corridor).
    public int GetRoomId(Vector2I pos) => InBounds(pos) ? _tiles[pos.X, pos.Y].RoomId : -1;

    // --- Field of view primitives, driven by Dungeon.FovManager. ---
    // GridManager only knows how to mutate tiles; the room-vs-corridor
    // policy decision lives in FovManager, not here.

    // Resets "currently seen" for every tile. IsExplored (memory) is
    // untouched, so previously-seen terrain/markers stay visible.
    public void ClearVisibility()
    {
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                _tiles[x, y].IsVisible = false;
    }

    // Reveals every tile belonging to `roomId`, plus any Wall tile
    // directly adjacent to one of them (the room's boundary walls),
    // so the room reads as a fully-enclosed space rather than floating
    // floor tiles with black gaps at the edges.
    public void RevealRoom(int roomId)
    {
        Vector2I[] neighbors = { new(0, -1), new(0, 1), new(-1, 0), new(1, 0) };

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                if (_tiles[x, y].RoomId != roomId) continue;

                Reveal(x, y);

                foreach (var dir in neighbors)
                {
                    var n = new Vector2I(x + dir.X, y + dir.Y);
                    if (InBounds(n) && _tiles[n.X, n.Y].Terrain == TerrainType.Wall)
                        Reveal(n.X, n.Y);
                }
            }
        }
    }

    // Reveals the (2*radius+1) square centered on `center` (used for
    // corridors, where RoomId == -1 - a 3x3 window at radius 1).
    public void RevealAround(Vector2I center, int radius)
    {
        for (int x = center.X - radius; x <= center.X + radius; x++)
            for (int y = center.Y - radius; y <= center.Y + radius; y++)
                if (InBounds(new Vector2I(x, y)))
                    Reveal(x, y);
    }

    private void Reveal(int x, int y)
    {
        _tiles[x, y].IsVisible = true;
        _tiles[x, y].IsExplored = true;
    }

    public bool InBounds(Vector2I pos) =>
        pos.X >= 0 && pos.X < Width && pos.Y >= 0 && pos.Y < Height;

    public Tile GetTile(Vector2I pos) => _tiles[pos.X, pos.Y];

    public bool IsWalkable(Vector2I pos) =>
        InBounds(pos) && _tiles[pos.X, pos.Y].IsWalkable;

    public Vector2 GridToWorld(Vector2I pos) =>
        new(pos.X * TileSize + TileSize / 2f, pos.Y * TileSize + TileSize / 2f);

    public Vector2I WorldToGrid(Vector2 world) =>
        new(Mathf.FloorToInt(world.X / TileSize), Mathf.FloorToInt(world.Y / TileSize));

    public override void _Draw()
    {
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                if (!_tiles[x, y].IsExplored) continue; // unseen = stays black

                var rect = new Rect2(x * TileSize, y * TileSize, TileSize, TileSize);
                var color = _tiles[x, y].Terrain == TerrainType.Wall
                    ? new Color(0.15f, 0.15f, 0.15f)
                    : new Color(0.35f, 0.35f, 0.35f);
                DrawRect(rect, color, true);
                DrawRect(rect, new Color(0f, 0f, 0f, 0.3f), false);
            }
        }
    }
}
