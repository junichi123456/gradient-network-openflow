using Godot;
using System.Collections.Generic;

namespace MysteryDungeon.Grid;

// Thin wrapper around Godot's built-in AStarGrid2D, now caching one grid
// per MovementProfile instead of a single walkability snapshot - a Water
// tile, say, is a completely different obstacle to a Normal walker than
// to a Hover one. FloorController builds exactly one of these per floor
// (right after DungeonGenerator.Generate) and shares it across every
// chasing entity that floor; each profile's AStarGrid2D is built lazily
// on first use and cached for the rest of the floor's lifetime.
//
// Only Wall tiles are ever marked SetPointSolid, so Godot's own
// DiagonalModeEnum.OnlyIfNoObstacles corner-cutting check (which keys
// off SetPointSolid) only ever blocks a diagonal shortcut past an actual
// Wall - matching GridManager.CanCutCorner's "Wall-only" rule exactly.
// A tile this profile can't stand on but that isn't a Wall (e.g. Water
// for a Normal walker) instead gets an extreme SetPointWeightScale:
// still "open" for diagonal-cutting purposes, but so expensive to
// actually step onto that the pathfinder only ever does so if there is
// truly no other way through.
public class AStarPathfinder
{
    private const float AvoidWeightScale = 99999f;

    private readonly GridManager _grid;
    private readonly Dictionary<MovementProfile, AStarGrid2D> _cache = new();

    public AStarPathfinder(GridManager grid)
    {
        _grid = grid;
    }

    // Returns the next tile to step onto when moving from `from` toward
    // `target` under the given movement profile, or null if already
    // there or no path exists.
    public Vector2I? GetNextStep(Vector2I from, Vector2I target, MovementProfile profile = MovementProfile.Normal)
    {
        if (from == target) return null;

        var path = GetOrBuildGrid(profile).GetIdPath(from, target);
        return path.Count >= 2 ? path[1] : null;
    }

    // Full path (including `from`) under the given profile - mainly for
    // verification/debugging; gameplay code consumes GetNextStep instead.
    public List<Vector2I> GetFullPath(Vector2I from, Vector2I target, MovementProfile profile = MovementProfile.Normal)
    {
        var result = new List<Vector2I>();
        foreach (Vector2I point in GetOrBuildGrid(profile).GetIdPath(from, target))
            result.Add(point);
        return result;
    }

    private AStarGrid2D GetOrBuildGrid(MovementProfile profile)
    {
        if (_cache.TryGetValue(profile, out var cached)) return cached;

        var astar = new AStarGrid2D
        {
            Region = new Rect2I(0, 0, _grid.Width, _grid.Height),
            DiagonalMode = AStarGrid2D.DiagonalModeEnum.OnlyIfNoObstacles,
        };
        astar.Update();

        for (int x = 0; x < _grid.Width; x++)
        {
            for (int y = 0; y < _grid.Height; y++)
            {
                var pos = new Vector2I(x, y);
                var terrain = _grid.GetTile(pos).Terrain;

                if (terrain == TerrainType.Wall)
                {
                    astar.SetPointSolid(pos, true); // blocks travel AND diagonal corner-cutting for every profile
                    continue;
                }

                if (!TerrainTraversalRules.IsWalkable(terrain, profile))
                    astar.SetPointWeightScale(pos, AvoidWeightScale); // still open for corner-cutting, just heavily avoided
            }
        }

        _cache[profile] = astar;
        return astar;
    }
}
