using Godot;
using System.Collections.Generic;
using MysteryDungeon.Combat;

namespace MysteryDungeon.Entities;

// Player-only carried-materials list, completely separate from
// InventoryManager's general item slots (Consumable/Throwable never end
// up here, and Material items never end up in InventoryManager - see
// FloorController.TryPickupItemAt's type branch). 10 slots, each
// stacking up to 10 of the same material - deliberately smaller/tighter
// than InventoryManager's 20 slots / 10-per-Throwable-stack, since
// materials are meant to be hauled back and deposited often rather than
// hoarded indefinitely.
public partial class MaterialInventory : Node
{
    public const int MaxCapacity = 10;
    public const int MaxStackPerSlot = 10;

    private readonly List<InventorySlot> _slots = new();

    public IReadOnlyList<InventorySlot> Slots => _slots;

    public bool AddItem(string itemId)
    {
        var data = ItemDatabase.Get(itemId);
        if (data == null || data.Type != ItemType.Material) return false;

        foreach (var slot in _slots)
        {
            if (slot.Data.Id == itemId && slot.Quantity < MaxStackPerSlot)
            {
                slot.Quantity++;
                return true;
            }
        }

        if (_slots.Count >= MaxCapacity) return false;

        _slots.Add(new InventorySlot(data));
        return true;
    }

    public bool HasItem(string itemId) => _slots.Exists(s => s.Data.Id == itemId);

    public InventorySlot GetSlot(string itemId) => _slots.Find(s => s.Data.Id == itemId);

    // Empties every slot and returns its contents - used by
    // HubUpgradeManager.DepositMaterials when the player returns to base.
    public List<(string ItemId, int Quantity)> DrainAll()
    {
        var drained = new List<(string, int)>();
        foreach (var slot in _slots)
            drained.Add((slot.Data.Id, slot.Quantity));
        _slots.Clear();
        return drained;
    }
}
