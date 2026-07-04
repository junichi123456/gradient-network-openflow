using Godot;

namespace MysteryDungeon.Grid;

// Owns the logical Tile[,] grid, grid<->world coordinate conversion,
// and walkability queries. Terrain is rendered onto a TileMapLayer
// child (see BuildTileMapLayer/RefreshTileMap) painted from a
// dynamically-generated 5-color placeholder atlas - a stand-in for real
// tile art, swappable later without touching the SetCell logic that
// mirrors _tiles onto it.
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

    private TileMapLayer _tileMapLayer;
    private int _tileSourceId;

    // Column order in the generated placeholder atlas - swap
    // BuildPlaceholderAtlasTexture for real tile art later without
    // touching RefreshTileMap's SetCell logic, since it only ever
    // refers to these indices.
    private const int WallAtlasX = 0;
    private const int FloorAtlasX = 1;
    private const int WaterAtlasX = 2;
    private const int LavaAtlasX = 3;
    private const int ChasmAtlasX = 4;
    private const int AtlasColumnCount = 5;

    public override void _Ready()
    {
        BuildTileMapLayer();
        Resize(Width, Height, TerrainType.Wall);
    }

    private void BuildTileMapLayer()
    {
        var tileSet = new TileSet { TileSize = new Vector2I(TileSize, TileSize) };

        var source = new TileSetAtlasSource
        {
            Texture = BuildPlaceholderAtlasTexture(),
            TextureRegionSize = new Vector2I(TileSize, TileSize),
        };
        for (int i = 0; i < AtlasColumnCount; i++)
            source.CreateTile(new Vector2I(i, 0));

        _tileSourceId = tileSet.AddSource(source);

        _tileMapLayer = new TileMapLayer { TileSet = tileSet };
        AddChild(_tileMapLayer);
    }

    // One flat color per TerrainType, laid out left-to-right in a single
    // row - a placeholder for a real tile atlas PNG later. Wall/Floor
    // keep the same colors GridManager's old _Draw() used; Water/Lava/
    // Chasm are new (the old _Draw() collapsed all three into the same
    // "floor gray" - it only ever distinguished Wall from "everything
    // else").
    private Texture2D BuildPlaceholderAtlasTexture()
    {
        var image = Image.CreateEmpty(TileSize * AtlasColumnCount, TileSize, false, Image.Format.Rgba8);

        void FillColumn(int index, Color color) =>
            image.FillRect(new Rect2I(new Vector2I(index * TileSize, 0), new Vector2I(TileSize, TileSize)), color);

        FillColumn(WallAtlasX, new Color(0.15f, 0.15f, 0.15f));
        FillColumn(FloorAtlasX, new Color(0.35f, 0.35f, 0.35f));
        FillColumn(WaterAtlasX, new Color(0.2f, 0.4f, 0.9f));
        FillColumn(LavaAtlasX, new Color(0.9f, 0.35f, 0.1f));
        FillColumn(ChasmAtlasX, new Color(0.05f, 0.05f, 0.05f));

        return ImageTexture.CreateFromImage(image);
    }

    // Reallocates the tile array at the given dimensions, filled entirely
    // with `fill`. Also updates Width/Height so GridToWorld/RefreshTileMap
    // stay in sync with the new size.
    public void Resize(int width, int height, TerrainType fill = TerrainType.Wall)
    {
        Width = width;
        Height = height;
        _tiles = new Tile[Width, Height];
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                _tiles[x, y] = new Tile { Terrain = fill };
        RefreshTileMap();
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

    // Water/Lava/Chasm let a thrown item fly over them even though
    // IsWalkable is false for those tiles - only a Wall (or the map
    // edge) actually stops a projectile.
    public bool IsProjectilePassable(Vector2I pos) =>
        InBounds(pos) && _tiles[pos.X, pos.Y].IsProjectilePassable;

    // Corner-cutting rule for a diagonal step from `from` to `to`: blocked
    // only if at least one of the two orthogonal "shoulder" tiles is a
    // Wall (or off the map). Water/Lava/Chasm never block a diagonal cut
    // regardless of who's moving - see TerrainTraversalRules for the
    // separate, entity-specific "can I actually stand there" question.
    // Non-diagonal steps always return true (the rule doesn't apply).
    public bool CanCutCorner(Vector2I from, Vector2I to)
    {
        var delta = to - from;
        if (Mathf.Abs(delta.X) != 1 || Mathf.Abs(delta.Y) != 1) return true;

        var shoulderH = new Vector2I(from.X + delta.X, from.Y);
        var shoulderV = new Vector2I(from.X, from.Y + delta.Y);

        bool BlockedByWall(Vector2I p) => !InBounds(p) || GetTile(p).Terrain == TerrainType.Wall;
        return !BlockedByWall(shoulderH) && !BlockedByWall(shoulderV);
    }

    public Vector2 GridToWorld(Vector2I pos) =>
        new(pos.X * TileSize + TileSize / 2f, pos.Y * TileSize + TileSize / 2f);

    public Vector2I WorldToGrid(Vector2 world) =>
        new(Mathf.FloorToInt(world.X / TileSize), Mathf.FloorToInt(world.Y / TileSize));

    // Mirrors _tiles onto the TileMapLayer: an unexplored tile is erased
    // (nothing drawn - same "stays black" behavior the old _Draw() gave
    // via its IsExplored early-continue), an explored tile gets the
    // atlas cell matching its current Terrain. Called whenever terrain
    // or visibility changes (Resize, DungeonGenerator.Generate,
    // FovManager.UpdateVisibility) - the same call sites that used to
    // call QueueRedraw().
    public void RefreshTileMap()
    {
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                var pos = new Vector2I(x, y);

                if (!_tiles[x, y].IsExplored)
                {
                    _tileMapLayer.EraseCell(pos);
                    continue;
                }

                int atlasX = _tiles[x, y].Terrain switch
                {
                    TerrainType.Wall => WallAtlasX,
                    TerrainType.Floor => FloorAtlasX,
                    TerrainType.Water => WaterAtlasX,
                    TerrainType.Lava => LavaAtlasX,
                    TerrainType.Chasm => ChasmAtlasX,
                    _ => FloorAtlasX,
                };
                _tileMapLayer.SetCell(pos, _tileSourceId, new Vector2I(atlasX, 0));
            }
        }
    }
}
