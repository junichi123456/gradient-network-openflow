using Godot;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MysteryDungeon.Combat;

internal class EcologyJson
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; }
    [JsonPropertyName("hooks")] public List<string> Hooks { get; set; } = new();
}

// JSON-driven ecology-slot catalog (trait_catalog_v2 §2/§6). Mirrors
// ItemDatabase/TraitDatabase: Get() lazy-loads on first use. Stage 2
// (this file): pure catalog - hooks are declared but not all consumed
// yet (trap_immune/terrain_damage_immune have no real effect to hook
// into today per the stage-0 terrain investigation; the rest map onto
// existing systems and are wired in stage 4/6).
public static class EcologyDatabase
{
    private static readonly Dictionary<string, EcologyData> _ecology = new();
    private static bool _loaded;

    public static void Load(string resPath = "res://Data/ecology.json")
    {
        _loaded = true;
        _ecology.Clear();

        if (!FileAccess.FileExists(resPath))
        {
            GD.PushWarning($"EcologyDatabase: {resPath} not found, no ecology slots available.");
            return;
        }

        using var file = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
        string text = file.GetAsText();

        List<EcologyJson> entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<EcologyJson>>(text);
        }
        catch (JsonException e)
        {
            GD.PushError($"EcologyDatabase: failed to parse {resPath}: {e.Message}");
            return;
        }

        if (entries == null) return;

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Id)) continue;

            _ecology[entry.Id] = new EcologyData
            {
                Id = entry.Id,
                Name = entry.Name,
                Description = entry.Description,
                Hooks = entry.Hooks ?? new List<string>(),
            };
        }

        GD.Print($"[EcologyDatabase] Loaded {_ecology.Count} ecology slots from {resPath}.");
    }

    public static EcologyData Get(string ecologyId)
    {
        if (!_loaded) Load();
        return ecologyId != null && _ecology.TryGetValue(ecologyId, out var data) ? data : null;
    }

    public static List<string> AllIds()
    {
        if (!_loaded) Load();
        return new List<string>(_ecology.Keys);
    }
}
