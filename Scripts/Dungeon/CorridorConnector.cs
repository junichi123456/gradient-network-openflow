using Godot;
using System;
using MysteryDungeon.Grid;

namespace MysteryDungeon.Dungeon;

// Walks the BSP tree bottom-up and carves an L-shaped, 1-tile-wide
// corridor between the two child subtrees at every internal node. Since
// every leaf ends up connected to its sibling this way, the whole floor
// is guaranteed connected without needing a separate pathfinding pass.
public static class CorridorConnector
{
    public static void Connect(BspNode node, GridManager grid, RandomNumberGenerator rng)
    {
        if (node.IsLeaf) return;

        Connect(node.Left, grid, rng);
        Connect(node.Right, grid, rng);

        var a = FindRoomCenter(node.Left);
        var b = FindRoomCenter(node.Right);
        if (a == null || b == null) return;

        CarveLShapedCorridor(grid, a.Value, b.Value, rng);
    }

    private static Vector2I? FindRoomCenter(BspNode node)
    {
        if (node == null) return null;
        if (node.IsLeaf)
            return node.HasRoom ? node.RoomRect.Position + node.RoomRect.Size / 2 : null;

        // Prefer left subtree's room; fall back to right if left is empty
        // (can happen when a leaf was too small to fit its minimum room).
        return FindRoomCenter(node.Left) ?? FindRoomCenter(node.Right);
    }

    private static void CarveLShapedCorridor(GridManager grid, Vector2I from, Vector2I to, RandomNumberGenerator rng)
    {
        if (rng.Randf() < 0.5f)
        {
            CarveHorizontal(grid, from.X, to.X, from.Y);
            CarveVertical(grid, from.Y, to.Y, to.X);
        }
        else
        {
            CarveVertical(grid, from.Y, to.Y, from.X);
            CarveHorizontal(grid, from.X, to.X, to.Y);
        }
    }

    private static void CarveHorizontal(GridManager grid, int x1, int x2, int y)
    {
        int start = Math.Min(x1, x2);
        int end = Math.Max(x1, x2);
        for (int x = start; x <= end; x++)
            grid.SetTerrain(new Vector2I(x, y), TerrainType.Floor);
    }

    private static void CarveVertical(GridManager grid, int y1, int y2, int x)
    {
        int start = Math.Min(y1, y2);
        int end = Math.Max(y1, y2);
        for (int y = start; y <= end; y++)
            grid.SetTerrain(new Vector2I(x, y), TerrainType.Floor);
    }
}
