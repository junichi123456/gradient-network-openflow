using System.Collections.Generic;

namespace MysteryDungeon.Entities;

// Party roster: a fixed partner (always present) plus up to
// MaxRecruited species recruited via RunTracker.CompleteDungeon().
// Spawning, positioning, and TargetToFollow chaining are
// FloorController's job (see FloorController.SpawnPartyMembers) - this
// class only tracks *what* is in the party, not where its members
// currently stand.
public class PartyManager
{
    private const int MaxRecruited = 2;
    private const string FixedPartnerSpeciesId = "Partner";

    private readonly List<string> _recruitedSpeciesIds = new();

    // Roster order = follow-chain order: index 0 (the fixed partner)
    // follows the Player, index 1 follows index 0's AllyEntity, etc.
    public IReadOnlyList<string> AllMemberSpeciesIds()
    {
        var all = new List<string> { FixedPartnerSpeciesId };
        all.AddRange(_recruitedSpeciesIds);
        return all;
    }

    public bool AddMember(string speciesId)
    {
        if (_recruitedSpeciesIds.Count >= MaxRecruited) return false;
        _recruitedSpeciesIds.Add(speciesId);
        return true;
    }
}
