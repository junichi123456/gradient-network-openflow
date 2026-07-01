using Godot;
using System.Collections.Generic;
using MysteryDungeon.Combat;

namespace MysteryDungeon.Entities;

// Player-only carried-items list (mirrors MoveManager's slot pattern).
// Consumables never stack (1 per slot); Throwables stack up to
// ThrowableMaxStack per slot. Both limits are enforced in AddItem so
// FloorController's pickup logic can just check the bool return value.
public partial class InventoryManager : Node
{
    public const int MaxCapacity = 20;
    public const int ThrowableMaxStack = 10;

    private readonly List<InventorySlot> _slots = new();

    public IReadOnlyList<InventorySlot> Slots => _slots;

    public bool AddItem(string itemId)
    {
        var data = ItemDatabase.Get(itemId);
        if (data == null) return false;

        int maxStack = data.Type == ItemType.Throwable ? ThrowableMaxStack : 1;

        foreach (var slot in _slots)
        {
            if (slot.Data.Id == itemId && slot.Quantity < maxStack)
            {
                slot.Quantity++;
                return true;
            }
        }

        if (_slots.Count >= MaxCapacity) return false;

        _slots.Add(new InventorySlot(data));
        return true;
    }

    public bool RemoveItem(string itemId, int amount = 1)
    {
        var slot = _slots.Find(s => s.Data.Id == itemId);
        if (slot == null || slot.Quantity < amount) return false;

        slot.Quantity -= amount;
        if (slot.Quantity <= 0) _slots.Remove(slot);
        return true;
    }

    public bool HasItem(string itemId) => _slots.Exists(s => s.Data.Id == itemId);

    public InventorySlot GetSlot(string itemId) => _slots.Find(s => s.Data.Id == itemId);
}
