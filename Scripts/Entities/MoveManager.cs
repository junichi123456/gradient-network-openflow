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

    // AI-only picker: the first learned move that hasn't been opted out
    // of automatic use (MoveSlot.IsAutoUseAllowed). Used by
    // HostileEntity/AllyEntity instead of GetActiveMove() so a move can
    // be reserved for player-manual use only. Range isn't modeled yet
    // (every move today is only usable against an adjacent target, and
    // callers already gate on adjacency before asking for this), so
    // there's no range filter here beyond that.
    public MoveSlot GetFirstAutoUsableMove()
    {
        foreach (var slot in _slots)
            if (slot.IsAutoUseAllowed)
                return slot;
        return null;
    }
}
