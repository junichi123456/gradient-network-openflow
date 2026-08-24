using Godot;
using System.Collections.Generic;

namespace MysteryDungeon.Dungeon;

// Position -> MapObjectType lookup for "what's sitting on this floor
// tile" (stairs / item / trap). Deliberately separate from GridManager,
// which only owns terrain/walkability.
//
// Item tiles additionally carry an ItemId (see _itemIds) so
// FloorController can look up which Data/items.json entry to hand to
// InventoryManager on pickup, or to re-place on ThrowItemAction's drop.
public class DungeonObjectManager
{
    private readonly Dictionary<Vector2I, MapObjectType> _objects = new();
    private readonly Dictionary<Vector2I, string> _itemIds = new();

    public void Clear()
    {
        _objects.Clear();
        _itemIds.Clear();
    }

    public void Set(Vector2I pos, MapObjectType type) => _objects[pos] = type;

    public void SetItem(Vector2I pos, string itemId)
    {
        _objects[pos] = MapObjectType.Item;
        _itemIds[pos] = itemId;
    }

    public string GetItemId(Vector2I pos) =>
        _itemIds.TryGetValue(pos, out var itemId) ? itemId : null;

    public void RemoveAt(Vector2I pos)
    {
        _objects.Remove(pos);
        _itemIds.Remove(pos);
    }

    public MapObjectType Get(Vector2I pos) =>
        _objects.TryGetValue(pos, out var type) ? type : MapObjectType.None;

    public bool IsStairs(Vector2I pos) => Get(pos) == MapObjectType.Stairs;

    // Read-only enumeration for presentation code (MinimapUI).
    public IReadOnlyDictionary<Vector2I, MapObjectType> All => _objects;
}
