using Godot;
using System.Collections.Generic;
using MysteryDungeon.Species;
using MysteryDungeon.UI;

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

            // speciesId is a canonical numeric id (Phase 20); show the
            // human-readable species name in the player-facing log.
            string displayName = SpeciesDatabase.Instance?.Get(speciesId)?.DisplayName ?? speciesId;

            if (success)
                MessageLogger.Log($"{displayName} wants to join your party!", MessageLogger.ProgressionColor);
            else
                GD.Print($"[Result] Defeated {count}x {displayName} (Chance: {chancePercent}%). Roll: Failed.");

            if (success) joined.Add(speciesId);
        }

        return joined;
    }
}
