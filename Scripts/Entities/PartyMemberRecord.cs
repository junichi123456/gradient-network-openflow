namespace MysteryDungeon.Entities;

// Phase 19: the persistent DATA half of a party member, deliberately
// not a Node - the scene-tree AllyEntity instance is rebuilt from the
// roster every floor and throws its state away, while this record
// lives for the whole run (see PartyState) and is what Level/EXP/HP
// actually persist through. Also the natural future serialization unit
// for save/load (out of scope this phase).
public class PartyMemberRecord
{
    // Stable identity within a run: the member's index in
    // PartyManager.AllMemberSpeciesIds() (fixed partner = 0, then the
    // active party in roster order). The roster can only change in the
    // Hub between runs, so the index never shifts mid-run - and unlike
    // SpeciesId alone it stays unique if two members share a species.
    public int MemberId { get; init; }

    // Which species to re-instance each floor. Base stats / BaseExpYield
    // still come from the existing AllyEntity._Ready() wiring - this
    // record only carries the per-individual progress on top.
    public string SpeciesId { get; init; }

    public int Level { get; set; }
    public long CurrentExp { get; set; }
    public int CurrentHp { get; set; }

    // PMD-style: a fainted ally leaves the run - hydrate skips downed
    // records entirely, so the member never spawns again this run.
    public bool IsDowned { get; set; }

    // False until the first dehydrate writes real values. Floor 1's
    // spawn has nothing to restore (the fresh _Ready() defaults ARE the
    // member's starting state), so hydrate is a no-op until this flips.
    public bool HasSnapshot { get; set; }

    // (Future) status conditions etc. - fields land here in a later
    // phase; this class stays behavior-free data either way.
}
