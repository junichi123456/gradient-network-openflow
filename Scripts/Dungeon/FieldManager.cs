using Godot;
using System.Collections.Generic;

namespace MysteryDungeon.Dungeon;

// Position -> FieldType for the trap-move overlays (see FieldType).
// Deliberately its own layer, parallel to DungeonObjectManager rather
// than folded into it: an object tile holds ONE discrete thing that is
// consumed on contact, whereas a field is standing ground that keeps
// affecting whoever is on it.
//
// Placement is "上書き" (overwrite) by the confirmed rule - a new field
// replaces whatever was on the tile, so one dictionary slot per position
// is exactly right and no stacking logic is needed.
public class FieldManager
{
    private readonly Dictionary<Vector2I, FieldType> _fields = new();

    public void Clear() => _fields.Clear();

    public void Set(Vector2I pos, FieldType type)
    {
        if (type == FieldType.None) _fields.Remove(pos);
        else _fields[pos] = type; // overwrite, per the confirmed rule
    }

    public FieldType Get(Vector2I pos) =>
        _fields.TryGetValue(pos, out var t) ? t : FieldType.None;

    public bool RemoveAt(Vector2I pos) => _fields.Remove(pos);

    // げきりゅう: wipes every field whose tile is within Chebyshev `radius`
    // of `centre` - the same 8-directional distance rule the rest of the
    // project uses for ranges. Returns how many were removed.
    public int ClearWithinRadius(Vector2I centre, int radius)
    {
        var doomed = new List<Vector2I>();
        foreach (var pos in _fields.Keys)
        {
            var d = (pos - centre).Abs();
            if (Mathf.Max(d.X, d.Y) <= radius) doomed.Add(pos);
        }

        foreach (var pos in doomed) _fields.Remove(pos);
        return doomed.Count;
    }

    // Read-only enumeration. Presentation code deliberately does NOT draw
    // Crevasse/Fissure (confirmed "見た目は視認できない", minimap included),
    // so any future renderer must filter rather than assume.
    public IReadOnlyDictionary<Vector2I, FieldType> All => _fields;

    public int Count => _fields.Count;
}
