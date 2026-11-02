using Godot;
using System;
using System.Collections.Generic;

namespace MysteryDungeon.Dungeon;

// Carves one randomly-sized room inside each BSP leaf, with at least a
// 1-tile margin from the leaf's own bounds so a leaf's room never
// touches a neighboring leaf's boundary directly.
public static class RoomPlacer
{
    public static List<BspNode> PlaceRooms(BspNode root, DungeonRule rule, RandomNumberGenerator rng)
    {
        var leavesWithRooms = new List<BspNode>();
        Visit(root, rule, rng, leavesWithRooms);
        return leavesWithRooms;
    }

    private static void Visit(BspNode node, DungeonRule rule, RandomNumberGenerator rng, List<BspNode> leavesWithRooms)
    {
        if (!node.IsLeaf)
        {
            Visit(node.Left, rule, rng, leavesWithRooms);
            Visit(node.Right, rule, rng, leavesWithRooms);
            return;
        }

        int availableW = node.Bounds.Size.X - 2;
        int availableH = node.Bounds.Size.Y - 2;
        if (availableW < rule.RoomMinSize.X || availableH < rule.RoomMinSize.Y)
            return; // leaf too small even for the minimum room - stays solid wall

        int roomW = Math.Clamp(rng.RandiRange(rule.RoomMinSize.X, rule.RoomMaxSize.X), rule.RoomMinSize.X, availableW);
        int roomH = Math.Clamp(rng.RandiRange(rule.RoomMinSize.Y, rule.RoomMaxSize.Y), rule.RoomMinSize.Y, availableH);

        int maxOffsetX = availableW - roomW;
        int maxOffsetY = availableH - roomH;
        int offsetX = maxOffsetX > 0 ? rng.RandiRange(0, maxOffsetX) : 0;
        int offsetY = maxOffsetY > 0 ? rng.RandiRange(0, maxOffsetY) : 0;

        var roomPos = node.Bounds.Position + new Vector2I(1 + offsetX, 1 + offsetY);
        node.RoomRect = new Rect2I(roomPos, new Vector2I(roomW, roomH));
        node.HasRoom = true;
        leavesWithRooms.Add(node);
    }
}
