using Godot;

namespace MysteryDungeon.Dungeon;

public class BspNode
{
    public Rect2I Bounds;
    public BspNode Left;
    public BspNode Right;

    // Set by RoomPlacer once this leaf's room has been carved.
    public Rect2I RoomRect;
    public bool HasRoom;

    public BspNode(Rect2I bounds)
    {
        Bounds = bounds;
    }

    public bool IsLeaf => Left == null && Right == null;
}
