namespace MysteryDungeon.Combat;

// Which family a trait belongs to - Unique traits are species-specific
// (shared by a small 1-5 species group per the catalog's own design);
// Template traits are the elemental fallbacks (きずな/ちから/まもり/流/派/
// 式/おしえ x 9 elements) handed to any species without a unique trait of
// its own (trait_catalog_v2 §0/§2).
public enum TraitCategory
{
    Unique,
    Template,
}

// Which of the 7 template families a Template-category trait is (null
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

    // 〇〇のおしえ (§4, stage 2-b): uses the same "〇〇の..." placeholder
    // notation as Bond/Power/Guard in the source doc, so it's a template
    // family like them (not the standalone Unique trait stage 2
    // originally catalogued it as - corrected here, before any species
    // ever referenced it, since stage 9's assignment hadn't run yet).
    Oshie,
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
