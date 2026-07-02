using Godot;
using System.Collections.Generic;

namespace MysteryDungeon.Entities;

// Tracks Player-side defeats by species across the whole dungeon run -
// FloorController holds exactly one instance for its entire lifetime
// (NOT reset per floor, unlike _spawnedEnemies/_objects/_rooms).
// CompleteDungeon() computes each tracked species' party-join roll from
// its kill count and logs the result; recruiting (adding to
// PartyManager) is left to the caller so this class stays a pure
// counter+roll with no roster-mutation side effects.
public class RunTracker
{
    private readonly Dictionary<string, int> _killCounts = new();

    public void RecordKill(string speciesId)
    {
        if (string.IsNullOrEmpty(speciesId)) return;
        _killCounts[speciesId] = _killCounts.GetValueOrDefault(speciesId) + 1;
    }

    // Chance = floor(kills / 2) * 1%, rolled once per tracked species.
    // Returns the species ids whose roll succeeded (i.e. joined).
    public List<string> CompleteDungeon(RandomNumberGenerator rng)
    {
        var joined = new List<string>();

        foreach (var (speciesId, count) in _killCounts)
        {
            int chancePercent = count / 2;
            bool success = rng.RandiRange(1, 100) <= chancePercent;
            string outcome = success ? $"Success! {speciesId} joined the party." : "Failed.";
            GD.Print($"[Result] Defeated {count}x {speciesId} (Chance: {chancePercent}%). Roll: {outcome}");

            if (success) joined.Add(speciesId);
        }

        return joined;
    }
}
