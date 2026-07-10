using System.Collections.Generic;
using Godot;

namespace MysteryDungeon.Entities;

// Phase 19: run-scoped container for every party member's persistent
// record, bridging the roster (who) and the per-floor AllyEntity
// instances (where/how they currently stand) with hydrate/dehydrate.
//
// Lifetime: owned by FloorController as a plain field, exactly like
// RunTracker - FloorController itself is run-scoped (created once per
// dungeon scene, survives every floor transition, destroyed when the
// run ends and the scene changes), so "initialized at run start,
// discarded at run end" comes for free with zero extra nodes. Note the
// Phase 19 proposal assumed FloorController was floor-scoped; it isn't
// - CleanupCurrentFloor only clears per-floor lists, never the
// controller itself.
public class PartyState
{
    private readonly Dictionary<int, PartyMemberRecord> _records = new();

    // Fetch-or-create. Doubles as the recruitment hook: when a future
    // phase lets a member join mid-run, calling this at join time is
    // the entire record-side integration. A species mismatch on an
    // existing id means the roster slot was reassigned (can't happen
    // mid-run today) - treated defensively as a brand-new member.
    public PartyMemberRecord EnsureRecord(int memberId, string speciesId)
    {
        if (_records.TryGetValue(memberId, out var existing) && existing.SpeciesId == speciesId)
            return existing;

        var record = new PartyMemberRecord { MemberId = memberId, SpeciesId = speciesId };
        _records[memberId] = record;
        return record;
    }

    public PartyMemberRecord GetRecord(int memberId) => _records.GetValueOrDefault(memberId);

    // Floor-exit snapshot: called from GenerateFloor BEFORE
    // CleanupCurrentFloor frees the ally nodes. Only living listed
    // allies are captured - fainted ones were already pruned from the
    // list and marked via MarkDowned at death time (the node QueueFrees
    // immediately on death, so a dehydrate-time downed check would
    // never see it).
    public void Dehydrate(IReadOnlyList<AllyEntity> allies)
    {
        foreach (var ally in allies)
        {
            if (!GodotObject.IsInstanceValid(ally) || !ally.IsAlive || ally.PartyMemberId < 0) continue;

            var record = EnsureRecord(ally.PartyMemberId, ally.SpeciesId);
            var (level, currentExp, currentHp) = ally.Stats.CapturePersistedState();
            record.Level = level;
            record.CurrentExp = currentExp;
            record.CurrentHp = currentHp;
            record.HasSnapshot = true;
        }
    }

    // Floor-entry restore for one freshly spawned ally. Returns false
    // when there is nothing to apply (first floor, or a downed member
    // that shouldn't have spawned). Must be the LAST thing that touches
    // the ally's Level this floor - see ApplyPersistedState for the
    // Level -> CurrentExp -> absolute-CurrentHp ordering it guarantees.
    public bool TryHydrate(AllyEntity ally)
    {
        var record = GetRecord(ally.PartyMemberId);
        if (record == null || !record.HasSnapshot || record.IsDowned) return false;

        ally.Stats.ApplyPersistedState(record.Level, record.CurrentExp, record.CurrentHp);
        return true;
    }

    // Death-time hook (AllyEntity.Die): the member sits out the rest of
    // the run (PMD-style), recorded with 0 HP. Unknown ids (e.g. a
    // hand-spawned test ally that never got a roster slot) are ignored.
    public void MarkDowned(int memberId)
    {
        var record = GetRecord(memberId);
        if (record == null) return;

        record.IsDowned = true;
        record.CurrentHp = 0;
    }

    public bool IsDowned(int memberId) => GetRecord(memberId)?.IsDowned ?? false;
}
