namespace MysteryDungeon.Combat;

public enum ItemType
{
    Consumable,
    Throwable,

    // Hub-upgrade currency (Phase 11). Never usable in-dungeon
    // (UseItemAction's default branch just logs "no effect" and
    // doesn't consume it) - only picked up into MaterialInventory
    // (never InventoryManager) and spent via HubUpgradeManager.
    Material,
}

public enum ItemEffectTarget
{
    Hp,
    Belly,
    Damage,

    // Material items carry no direct-use effect.
    None,
}

// Immutable item definition, loaded once by ItemDatabase from
// Data/items.json. ElementType is only meaningful for Throwable items -
// it feeds TypeChartManager the same way a move's Type does.
public class ItemData
{
    public string Id { get; set; }
    public string Name { get; set; }
    public ItemType Type { get; set; }
    public ItemEffectTarget EffectTarget { get; set; }
    public int EffectValue { get; set; }
    public string ElementType { get; set; }
}
