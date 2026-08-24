using Godot;

namespace MysteryDungeon.Dungeon;

// Reference type (not the Rect2I struct it wraps) because IsMonsterHouse
// / IsTriggered are mutable state that must stay visible through every
// reference to this room (a List<Rect2I> indexer would hand back a copy
// on mutation - a class avoids that struct trap).
public class Room
{
    public int Id { get; }
    public Rect2I Bounds { get; }
    public bool IsMonsterHouse { get; set; }
    public bool IsTriggered { get; set; }

    public Room(int id, Rect2I bounds)
    {
        Id = id;
        Bounds = bounds;
    }

    public Vector2I Center => Bounds.Position + Bounds.Size / 2;
}
