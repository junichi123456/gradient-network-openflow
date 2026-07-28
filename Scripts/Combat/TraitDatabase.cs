using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MysteryDungeon.Combat;

internal class TraitJson
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; }
    [JsonPropertyName("element")] public string Element { get; set; }
    [JsonPropertyName("template_kind")] public string TemplateKind { get; set; }
}

// JSON-driven trait catalog (trait_catalog_v2 - Palworld-style: mostly
// species-specific Unique traits shared by small groups, plus 54
// elemental Template fallbacks for anything without a unique trait of
// its own). Mirrors ItemDatabase/MoveDatabase: Get() lazy-loads on first
// use since SpeciesDatabase's own _Ready() (which resolves each
// species' Trait/Ecology ids) can run before the scene root's explicit
// Load() call.
//
// Stage 2 (this file): pure catalog, no mechanics consumed yet - every
// trait's actual battle effect lands in a later stage (see
// trait_catalog_v2_instructions.md §8).
public static class TraitDatabase
{
    private static readonly Dictionary<string, TraitData> _traits = new();
    private static bool _loaded;

    public static void Load(string resPath = "res://Data/traits.json")
    {
        _loaded = true;
        _traits.Clear();

        if (!FileAccess.FileExists(resPath))
        {
            GD.PushWarning($"TraitDatabase: {resPath} not found, no traits available.");
            return;
        }

        using var file = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
        string text = file.GetAsText();

        List<TraitJson> entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<TraitJson>>(text);
        }
        catch (JsonException e)
        {
            GD.PushError($"TraitDatabase: failed to parse {resPath}: {e.Message}");
            return;
        }

        if (entries == null) return;

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Id)) continue;

            Element? element = Enum.TryParse<Element>(entry.Element, out var parsedElement) ? parsedElement : null;
            TraitTemplateKind? templateKind = Enum.TryParse<TraitTemplateKind>(entry.TemplateKind, true, out var parsedKind) ? parsedKind : null;

            _traits[entry.Id] = new TraitData
            {
                Id = entry.Id,
                Name = entry.Name,
                Description = entry.Description,
                Category = Enum.TryParse<TraitCategory>(entry.Category, true, out var category) ? category : TraitCategory.Unique,
                Element = element,
                TemplateKind = templateKind,
            };
        }

        GD.Print($"[TraitDatabase] Loaded {_traits.Count} traits from {resPath}.");
    }

    public static TraitData Get(string traitId)
    {
        if (!_loaded) Load();
        return traitId != null && _traits.TryGetValue(traitId, out var data) ? data : null;
    }

    public static List<string> AllIds()
    {
        if (!_loaded) Load();
        return new List<string>(_traits.Keys);
    }
}
