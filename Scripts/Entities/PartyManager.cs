using System.Collections.Generic;

namespace MysteryDungeon.Entities;

// Party roster: the story-chosen partner (always present, not selectable) plus
// every species ever recruited via RunTracker.CompleteDungeon()
// (RecruitedRoster, unbounded), of which up to MaxActiveParty are
// actually brought along on the next run (ActiveParty, selected via the
// Hub's Pal Box UI - see PartySetupUI). Spawning, positioning, and
// TargetToFollow chaining are FloorController's job (see
// FloorController.SpawnPartyMembers) - this class only tracks *who*,
// not where they currently stand.
//
// Owned by HubUpgradeManager (a project.godot autoload) rather than
// FloorController: a dungeon-scene-local instance would be destroyed on
// every Dungeon<->Hub scene transition, silently wiping the recruited
// roster - see HubUpgradeManager.PartyManager for the persistent one
// FloorController actually reads from.
public class PartyManager
{
    public const int MaxActiveParty = 2;

    private readonly List<string> _recruitedRoster = new();
    private readonly List<string> _activeParty = new();

    // 振り分けで決まる主人公（ダンジョンの Player 個体に相当、入れ替え不可）と相棒
    // （常に同行。Pal Box では外せない）。固定の相棒"001"は廃止した。
    public string Protagonist { get; private set; } = "";
    public string Partner { get; private set; } = "";

    public void SetStartingParty(string protagonist, string partner)
    {
        Protagonist = protagonist ?? "";
        Partner = partner ?? "";
    }

    public IReadOnlyList<string> RecruitedRoster => _recruitedRoster;
    public IReadOnlyList<string> ActiveParty => _activeParty;

    // Spawn order for FloorController.SpawnPartyMembers: fixed partner
    // first, then the current active-party selection - matches the
    // conga-line follow chain's Player -> Ally1 -> Ally2 order.
    public IReadOnlyList<string> AllMemberSpeciesIds()
    {
        var all = new List<string>();
        if (Partner != "") all.Add(Partner);
        all.AddRange(_activeParty);
        return all;
    }

    // Called on a successful RunTracker.CompleteDungeon() join roll.
    // Auto-fills an open active-party slot so a fresh recruit isn't
    // silently benched until the player visits the Pal Box.
    public void AddToRoster(string speciesId)
    {
        _recruitedRoster.Add(speciesId);
        if (_activeParty.Count < MaxActiveParty)
            _activeParty.Add(speciesId);
    }

    // セーブ用。Pending 等の一時値は含めない。
    public void Restore(string protagonist, string partner, IEnumerable<string> roster, IEnumerable<string> active)
    {
        SetStartingParty(protagonist, partner);
        _recruitedRoster.Clear(); _recruitedRoster.AddRange(roster);
        _activeParty.Clear();
        foreach (var id in active)
            if (_activeParty.Count < MaxActiveParty) _activeParty.Add(id);
    }

    public bool IsActive(string speciesId) => _activeParty.Contains(speciesId);

    // Toggles speciesId in/out of the active party (PartySetupUI).
    // Returns false if trying to activate while already at the
    // MaxActiveParty cap - the caller stays on the same selection.
    public bool ToggleActive(string speciesId)
    {
        if (_activeParty.Contains(speciesId))
        {
            _activeParty.Remove(speciesId);
            return true;
        }

        if (_activeParty.Count >= MaxActiveParty) return false;

        _activeParty.Add(speciesId);
        return true;
    }
}
