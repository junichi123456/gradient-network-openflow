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
