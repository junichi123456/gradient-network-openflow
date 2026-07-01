using Godot;
using System.Collections.Generic;
using MysteryDungeon.Combat;

namespace MysteryDungeon.Entities;

// Holds up to 4 learned moves for an entity, mirroring EntityStats'
// composition pattern (auto-attached by Entity._Ready()). There is no
// move-selection UI yet, so combat always uses GetActiveMove() (slot 0);
// a real selection mechanism is future work.
public partial class MoveManager : Node
{
    public const int MaxMoves = 4;

    private readonly List<MoveSlot> _slots = new();

    public IReadOnlyList<MoveSlot> Slots => _slots;

    public bool Learn(string moveId)
    {
        if (_slots.Count >= MaxMoves) return false;

        var data = MoveDatabase.Get(moveId);
        if (data == null)
        {
            GD.PushWarning($"MoveManager: unknown move id '{moveId}'.");
            return false;
        }

        _slots.Add(new MoveSlot(data));
        return true;
    }

    public MoveSlot GetActiveMove() => _slots.Count > 0 ? _slots[0] : null;
}
