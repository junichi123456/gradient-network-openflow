namespace MysteryDungeon.Combat;

// Which family a trait belongs to - Unique traits are species-specific
// (shared by a small 1-5 species group per the catalog's own design);
// Template traits are the 54 elemental fallbacks (きずな/ちから/まもり/
// 流/派/式 x 9 elements) handed to any species without a unique trait of
// its own (trait_catalog_v2 §0/§2).
public enum TraitCategory
{
    Unique,
    Template,
}

// Which of the 6 template families a Template-category trait is (null
// for Unique traits). Consumption stages (2-a onward) dispatch on this
// rather than re-parsing the Id string.
public enum TraitTemplateKind
{
    Bond,     // きずな
    Power,    // ちから
    Guard,    // まもり
    Resist,   // 流
    Stab,     // 派
    Weakness, // 式
}

// Immutable trait definition, loaded once by TraitDatabase from
// Data/traits.json (trait_catalog_v2 stage 2 - data model only, no
// mechanics consumed yet; see individual stage commits for that).
// Element/TemplateKind are null for Unique traits.
public class TraitData
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public TraitCategory Category { get; set; }
    public Element? Element { get; set; }
    public TraitTemplateKind? TemplateKind { get; set; }
}
