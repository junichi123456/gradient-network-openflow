using Godot;

namespace MysteryDungeon.Dungeon;

// Recursively splits a rectangular region into a binary tree of
// sub-regions until every leaf is smaller than DungeonRule.MinLeafSize
// on both axes. Pure data-structure/algorithm - no GridManager access.
public class BspTree
{
    public BspNode Root { get; }

    public BspTree(Rect2I bounds, DungeonRule rule, RandomNumberGenerator rng)
    {
        Root = new BspNode(bounds);
        Split(Root, rule, rng);
    }

    private void Split(BspNode node, DungeonRule rule, RandomNumberGenerator rng)
    {
        int minSize = rule.MinLeafSize;

        bool canSplitHorizontally = node.Bounds.Size.Y >= minSize * 2;
        bool canSplitVertically = node.Bounds.Size.X >= minSize * 2;
        if (!canSplitHorizontally && !canSplitVertically) return; // leaf

        bool splitHorizontally = canSplitHorizontally && canSplitVertically
            ? rng.Randf() < 0.5f
            : canSplitHorizontally;

        if (splitHorizontally)
        {
            int splitY = rng.RandiRange(minSize, node.Bounds.Size.Y - minSize);
            var top = new Rect2I(node.Bounds.Position, new Vector2I(node.Bounds.Size.X, splitY));
            var bottom = new Rect2I(
                node.Bounds.Position + new Vector2I(0, splitY),
                new Vector2I(node.Bounds.Size.X, node.Bounds.Size.Y - splitY));
            node.Left = new BspNode(top);
            node.Right = new BspNode(bottom);
        }
        else
        {
            int splitX = rng.RandiRange(minSize, node.Bounds.Size.X - minSize);
            var left = new Rect2I(node.Bounds.Position, new Vector2I(splitX, node.Bounds.Size.Y));
            var right = new Rect2I(
                node.Bounds.Position + new Vector2I(splitX, 0),
                new Vector2I(node.Bounds.Size.X - splitX, node.Bounds.Size.Y));
            node.Left = new BspNode(left);
            node.Right = new BspNode(right);
        }

        Split(node.Left, rule, rng);
        Split(node.Right, rule, rng);
    }
}
