using Godot;
using MysteryDungeon.Grid;
using MysteryDungeon.Turn;
using MysteryDungeon.Entities;
using MysteryDungeon.Dungeon;

namespace MysteryDungeon.UI;

// Small overlay map in the top-right corner. Must live under a
// CanvasLayer (see Scenes/DungeonScene.tscn) rather than directly under
// the world's Node2D tree - Control nodes parented under a plain
// Node2D would otherwise pan/zoom along with Player's Camera2D instead
// of staying fixed on screen.
//
// Purely a read-only view: pulls tile/object/entity state through
// GridManager and FloorController's public accessors and redraws
// whenever a turn ends, without either of them knowing this exists.
public partial class MinimapUI : Control
{
    private const float CellPixels = 3f;
    private const float ScreenMargin = 10f;

    private GridManager _grid;
    private Player _player;
    private FloorController _floorController;

    public void Initialize(GridManager grid, TurnManager turnManager, Player player, FloorController floorController)
    {
        _grid = grid;
        _player = player;
        _floorController = floorController;

        Size = new Vector2(grid.Width * CellPixels, grid.Height * CellPixels);
        Position = new Vector2(GetViewport().GetVisibleRect().Size.X - Size.X - ScreenMargin, ScreenMargin);
        MouseFilter = MouseFilterEnum.Ignore;

        turnManager.TurnEnded += _ => QueueRedraw();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_grid == null) return;

        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0f, 0f, 0f, 0.5f));

        for (int x = 0; x < _grid.Width; x++)
        {
            for (int y = 0; y < _grid.Height; y++)
            {
                var pos = new Vector2I(x, y);
                var tile = _grid.GetTile(pos);
                if (!tile.IsExplored) continue;

                var color = tile.Terrain == TerrainType.Wall
                    ? new Color(0.4f, 0.4f, 0.4f, 0.9f)
                    : new Color(0.8f, 0.8f, 0.8f, 0.9f);
                DrawRect(CellRect(pos), color);
            }
        }

        foreach (var (pos, type) in _floorController.Objects.All)
        {
            if (!_grid.GetTile(pos).IsExplored) continue;

            var color = type switch
            {
                MapObjectType.Stairs => new Color(0.7f, 0.2f, 1f),
                MapObjectType.Item => new Color(0.2f, 0.9f, 0.2f),
                MapObjectType.Trap => new Color(0.9f, 0.15f, 0.15f),
                _ => Colors.Transparent,
            };
            DrawRect(CellRect(pos), color);
        }

        foreach (var enemy in _floorController.SpawnedEnemies)
        {
            if (!_grid.GetTile(enemy.GridPosition).IsVisible) continue;
            DrawRect(CellRect(enemy.GridPosition), enemy.DebugColor);
        }

        DrawRect(CellRect(_player.GridPosition), Colors.Yellow);
    }

    private static Rect2 CellRect(Vector2I pos) =>
        new(pos.X * CellPixels, pos.Y * CellPixels, CellPixels, CellPixels);
}
