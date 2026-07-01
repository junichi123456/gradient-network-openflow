namespace MysteryDungeon.Combat;

public enum ItemType
{
    Consumable,
    Throwable,
}

public enum ItemEffectTarget
{
    Hp,
    Belly,
    Damage,
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
