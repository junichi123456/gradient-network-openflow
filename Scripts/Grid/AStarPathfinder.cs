using Godot;

namespace MysteryDungeon.Grid;

// Thin wrapper around Godot's built-in AStarGrid2D, snapshotting one
// GridManager's walkability. FloorController builds exactly one of
// these per floor (right after DungeonGenerator.Generate) and shares it
// across every chasing enemy that floor - walls don't change mid-floor,
// so there is no need to rebuild per entity or per turn.
public class AStarPathfinder
{
    private readonly AStarGrid2D _astar = new();

    public AStarPathfinder(GridManager grid)
    {
        _astar.Region = new Rect2I(0, 0, grid.Width, grid.Height);
        _astar.DiagonalMode = AStarGrid2D.DiagonalModeEnum.Never; // movement is 4-directional only
        _astar.Update();

        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                var pos = new Vector2I(x, y);
                if (!grid.IsWalkable(pos))
                    _astar.SetPointSolid(pos, true);
            }
        }
    }

    // Returns the next tile to step onto when moving from `from` toward
    // `target`, or null if already there or no path exists.
    public Vector2I? GetNextStep(Vector2I from, Vector2I target)
    {
        if (from == target) return null;

        var path = _astar.GetIdPath(from, target);
        return path.Count >= 2 ? path[1] : null;
    }
}
