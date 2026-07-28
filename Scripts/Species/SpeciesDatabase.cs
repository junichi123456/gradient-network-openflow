using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MysteryDungeon.Combat;

namespace MysteryDungeon.Species;

// JSON DTOs (snake_case on disk, matching the other Data/*.json).
internal class SpeciesJson
{
    [JsonPropertyName("species_id")] public string SpeciesId { get; set; }
    [JsonPropertyName("display_name")] public string DisplayName { get; set; }
    [JsonPropertyName("base_hp")] public int BaseHP { get; set; }
    [JsonPropertyName("base_atk")] public int BaseAtk { get; set; }
    [JsonPropertyName("base_def")] public int BaseDef { get; set; }
    [JsonPropertyName("base_exp_yield")] public int BaseExpYield { get; set; } = 55;
    [JsonPropertyName("sprite_key")] public string SpriteKey { get; set; } = "";
    [JsonPropertyName("default_visual_scale")] public float DefaultVisualScale { get; set; } = 1.0f;
    [JsonPropertyName("types")] public List<string> Types { get; set; } = new();
    [JsonPropertyName("learnset")] public List<LearnsetJson> Learnset { get; set; } = new();
    [JsonPropertyName("evolution")] public EvolutionJson Evolution { get; set; }

    // trait_catalog_v2 stage 2: both optional for now (data model only -
    // the actual 287-species assignment is stage 9). "" = unassigned.
    [JsonPropertyName("trait")] public string Trait { get; set; } = "";
    [JsonPropertyName("ecology")] public string Ecology { get; set; } = "";
}

internal class LearnsetJson
{
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("move_id")] public string MoveId { get; set; }
}

internal class EvolutionJson
{
    [JsonPropertyName("to_species_id")] public string ToSpeciesId { get; set; }
    [JsonPropertyName("level")] public int Level { get; set; }
}

// Phase 20: the central species-constant DB, autoload singleton (per
// §9-3, matching HubUpgradeManager/MessageLogger) so it's resident for
// the whole game and validated once at startup. Discovery mechanism:
// a single Data/species.json array (same shape as moves.json), loaded
// through System.Text.Json exactly like MoveDatabase.
//
// Fail-loud posture (like the Phase 18/19 tripwires): startup runs full
// integrity validation and GD.PushError's every problem; Require()
// throws on an undefined SpeciesId so a bad spawn path surfaces
// immediately instead of silently spawning a statless entity.
public partial class SpeciesDatabase : Node
{
    public static SpeciesDatabase Instance { get; private set; }

    private readonly Dictionary<string, SpeciesData> _species = new();

    public IReadOnlyDictionary<string, SpeciesData> All => _species;

    public override void _Ready()
    {
        Instance = this;
        Load();

        var errors = Validate();
        foreach (var error in errors)
            GD.PushError($"[SpeciesDatabase] {error}");

        if (errors.Count == 0)
            GD.Print($"[SpeciesDatabase] Loaded and validated {_species.Count} species.");
        else
            GD.PushError($"[SpeciesDatabase] Validation FAILED with {errors.Count} problem(s) - see errors above.");
    }

    public void Load(string resPath = "res://Data/species.json")
    {
        _species.Clear();

        if (!FileAccess.FileExists(resPath))
        {
            GD.PushError($"[SpeciesDatabase] {resPath} not found - no species available.");
            return;
        }

        using var file = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
        string text = file.GetAsText();

        List<SpeciesJson> entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<SpeciesJson>>(text);
        }
        catch (JsonException e)
        {
            GD.PushError($"[SpeciesDatabase] failed to parse {resPath}: {e.Message}");
            return;
        }

        if (entries == null) return;

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.SpeciesId))
            {
                GD.PushError("[SpeciesDatabase] a species entry has an empty species_id - skipped.");
                continue;
            }

            // Duplicate ids are reported by Validate() below; last-wins
            // here just to get a deterministic map to validate against.
            _species[entry.SpeciesId] = ToData(entry);
        }
    }

    private static SpeciesData ToData(SpeciesJson j)
    {
        var types = new List<Element>();
        foreach (var typeName in j.Types)
            if (Enum.TryParse<Element>(typeName, out var element))
                types.Add(element);
            // an unparseable type name is left out here and flagged by
            // Validate() (which re-checks the raw json names).

        var learnset = new List<LearnsetEntry>();
        foreach (var l in j.Learnset)
            learnset.Add(new LearnsetEntry { Level = l.Level, MoveId = l.MoveId });

        EvolutionData evolution = null;
        if (j.Evolution != null && !string.IsNullOrEmpty(j.Evolution.ToSpeciesId))
            evolution = new EvolutionData { ToSpeciesId = j.Evolution.ToSpeciesId, Level = j.Evolution.Level };

        return new SpeciesData
        {
            SpeciesId = j.SpeciesId,
            DisplayName = j.DisplayName,
            BaseHP = j.BaseHP,
            BaseAtk = j.BaseAtk,
            BaseDef = j.BaseDef,
            BaseExpYield = j.BaseExpYield,
            SpriteKey = j.SpriteKey,
            DefaultVisualScale = j.DefaultVisualScale,
            Types = types,
            Learnset = learnset,
            Evolution = evolution,
            Trait = j.Trait ?? "",
            Ecology = j.Ecology ?? "",
        };
    }

    // Startup integrity check (§4). Re-reads the file so it can catch
    // duplicate ids and unparseable type names that the map-building
    // Load() above can't see once collapsed. learnset moveId existence
    // is deliberately NOT checked (forward-declared until the Move DB is
    // wired - §9-6). Returns a human-readable problem list (empty = OK).
    public List<string> Validate(string resPath = "res://Data/species.json")
    {
        var errors = new List<string>();

        // Duplicate SpeciesId detection (needs the raw list, pre-collapse).
        if (FileAccess.FileExists(resPath))
        {
            using var file = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
            List<SpeciesJson> raw = null;
            try { raw = JsonSerializer.Deserialize<List<SpeciesJson>>(file.GetAsText()); }
            catch (JsonException) { /* parse failure already reported by Load */ }

            if (raw != null)
            {
                var seen = new HashSet<string>();
                foreach (var entry in raw)
                {
                    if (string.IsNullOrEmpty(entry.SpeciesId)) continue;
                    if (!seen.Add(entry.SpeciesId))
                        errors.Add($"Duplicate species_id '{entry.SpeciesId}'.");

                    foreach (var typeName in entry.Types)
                        if (!Enum.TryParse<Element>(typeName, out _))
                            errors.Add($"Species '{entry.SpeciesId}' has type '{typeName}' outside the Element closed set.");

                    if (entry.Types.Count < 1 || entry.Types.Count > 2)
                        errors.Add($"Species '{entry.SpeciesId}' has {entry.Types.Count} types (must be 1-2).");
                }
            }
        }

        // Evolution targets must resolve (§7-5).
        foreach (var species in _species.Values)
            if (species.Evolution != null && !_species.ContainsKey(species.Evolution.ToSpeciesId))
                errors.Add($"Species '{species.SpeciesId}' evolves to unknown species '{species.Evolution.ToSpeciesId}'.");

        // The Element enum and type_chart.json must stay in lockstep -
        // every Element name has to exist as a type in the chart, or a
        // species' typing would silently resolve to neutral 1.0 in
        // damage (§7-7 cross-check).
        foreach (Element element in Enum.GetValues<Element>())
            if (!TypeChartManager.HasType(element.ToString()))
                errors.Add($"Element '{element}' is missing from type_chart.json - the chart and enum are out of sync.");

        // trait_catalog_v2 stage 2: Trait/Ecology are soft-checked only -
        // a non-empty id must resolve against its catalog, but an EMPTY
        // Trait is not (yet) an error, since the real 287-species
        // assignment is stage 9, landing after this data-model stage.
        foreach (var species in _species.Values)
        {
            if (!string.IsNullOrEmpty(species.Trait) && TraitDatabase.Get(species.Trait) == null)
                errors.Add($"Species '{species.SpeciesId}' has trait '{species.Trait}' not found in traits.json.");

            if (!string.IsNullOrEmpty(species.Ecology) && EcologyDatabase.Get(species.Ecology) == null)
                errors.Add($"Species '{species.SpeciesId}' has ecology '{species.Ecology}' not found in ecology.json.");
        }

        return errors;
    }

    public SpeciesData Get(string speciesId) =>
        speciesId != null && _species.TryGetValue(speciesId, out var data) ? data : null;

    // Fail-loud accessor for spawn paths (§7-4): an undefined SpeciesId
    // is a wiring bug, not a recoverable condition, so surface it
    // immediately rather than spawning a zero-stat entity.
    public SpeciesData Require(string speciesId)
    {
        var data = Get(speciesId);
        if (data == null)
            throw new InvalidOperationException($"[SpeciesDatabase] no species defined for id '{speciesId}'. Check the spawn path and Data/species.json.");
        return data;
    }
}
