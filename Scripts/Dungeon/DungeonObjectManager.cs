using Godot;
using System.Collections.Generic;

namespace MysteryDungeon.Dungeon;

// Position -> MapObjectType lookup for "what's sitting on this floor
// tile" (stairs / item / trap placeholders). Deliberately separate
// from GridManager, which only owns terrain/walkability - this class
// is the seam Phase 5 will replace with real Item instances.
public class DungeonObjectManager
{
    private readonly Dictionary<Vector2I, MapObjectType> _objects = new();

    public void Clear() => _objects.Clear();

    public void Set(Vector2I pos, MapObjectType type) => _objects[pos] = type;

    public MapObjectType Get(Vector2I pos) =>
        _objects.TryGetValue(pos, out var type) ? type : MapObjectType.None;

    public bool IsStairs(Vector2I pos) => Get(pos) == MapObjectType.Stairs;
}
