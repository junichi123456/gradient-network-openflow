using System.Collections.Generic;

namespace MysteryDungeon.Combat;

// Immutable ecology-slot definition, loaded once by EcologyDatabase from
// Data/ecology.json (trait_catalog_v2 §2/§6). Fully independent from
// Trait - a species can hold both a Trait AND an Ecology at once, or an
// Ecology alone is never assigned without a Trait (Trait stays single/
// required per species; Ecology is optional).
//
// Hooks are plain declarative tags (e.g. "lava_walk", "trap_immune") -
// not an execution engine. Consumption stages read the Ecology's Id
// directly via a hardcoded switch (same pattern MoveData.AilmentEffect/
// TraitData.TemplateKind already use), matching trait_catalog_v2 §1's
// finding that most of these hooks map onto EXISTING systems
// (MovementProfile, the accumulation tracker, Tile.IsExplored) rather
// than needing a new generic hook-dispatch mechanism.
public class EcologyData
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public List<string> Hooks { get; set; } = new();
}
