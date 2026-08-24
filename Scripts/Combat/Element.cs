namespace MysteryDungeon.Combat;

// Phase 20: the 9-element closed set (無/炎/水/草/雷/地/氷/竜/闇).
// The names mirror Data/type_chart.json's "types" array EXACTLY and in
// the same order, on purpose: Element.ToString() then yields the same
// string that EntityStats.Type1/Type2 and TypeChartManager already
// speak, so this enum is a typo-proof, compile-checked handle onto
// those 9 names without introducing a second source of truth. The
// actual matchup multipliers stay in type_chart.json (see
// TypeChartManager) - Phase 20 only seats the enum + species Types as
// data; damage still reads the string types exactly as before.
public enum Element
{
    Neutral,
    Fire,
    Water,
    Grass,
    Electric,
    Ground,
    Ice,
    Dragon,
    Dark,
}
