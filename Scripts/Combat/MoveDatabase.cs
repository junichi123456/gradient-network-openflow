using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MysteryDungeon.Combat;

internal class MoveJson
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; }
    [JsonPropertyName("power")] public int Power { get; set; }
    [JsonPropertyName("accuracy")] public int Accuracy { get; set; }
    [JsonPropertyName("max_pp")] public int MaxPp { get; set; }
    [JsonPropertyName("range")] public string Range { get; set; }
}

// JSON-driven move definitions. DungeonScene._Ready() calls Load()
// explicitly for clarity/logging, but Get() also lazy-loads on first
// use - Godot runs child nodes' _Ready() (e.g. Player learning its
// starting moves) before the scene root's _Ready(), so anything that
// only loaded there would still be empty when entities ask for moves.
public static class MoveDatabase
{
    private static readonly Dictionary<string, MoveData> _moves = new();
    private static bool _loaded;

    public static void Load(string resPath = "res://Data/moves.json")
    {
        _loaded = true;
        _moves.Clear();

        if (!FileAccess.FileExists(resPath))
        {
            GD.PushWarning($"MoveDatabase: {resPath} not found, no moves available.");
            return;
        }

        using var file = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
        string text = file.GetAsText();

        List<MoveJson> entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<MoveJson>>(text);
        }
        catch (JsonException e)
        {
            GD.PushError($"MoveDatabase: failed to parse {resPath}: {e.Message}");
            return;
        }

        if (entries == null) return;

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Id)) continue;

            _moves[entry.Id] = new MoveData
            {
                Id = entry.Id,
                Name = entry.Name,
                Type = entry.Type,
                Category = Enum.TryParse<MoveCategory>(entry.Category, out var category) ? category : MoveCategory.Physical,
                Power = entry.Power,
                Accuracy = entry.Accuracy,
                MaxPp = entry.MaxPp,
                Range = Enum.TryParse<MoveRange>(entry.Range, out var range) ? range : MoveRange.Adjacent,
            };
        }

        GD.Print($"[MoveDatabase] Loaded {_moves.Count} moves from {resPath}.");
    }

    public static MoveData Get(string moveId)
    {
        if (!_loaded) Load();
        return moveId != null && _moves.TryGetValue(moveId, out var data) ? data : null;
    }
}
