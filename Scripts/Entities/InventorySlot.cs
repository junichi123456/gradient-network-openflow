using MysteryDungeon.Combat;

namespace MysteryDungeon.Entities;

// Runtime stack of a carried item: the immutable ItemData plus how many
// currently sit in this slot (see InventoryManager for stacking rules).
public class InventorySlot
{
    public ItemData Data { get; }
    public int Quantity { get; set; }

    public InventorySlot(ItemData data, int quantity = 1)
    {
        Data = data;
        Quantity = quantity;
    }
}
