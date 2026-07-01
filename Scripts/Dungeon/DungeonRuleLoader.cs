using Godot;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MysteryDungeon.Dungeon;

internal class GenerationJson
{
    [JsonPropertyName("map_width")] public int MapWidth { get; set; }
    [JsonPropertyName("map_height")] public int MapHeight { get; set; }
    [JsonPropertyName("min_leaf_size")] public int MinLeafSize { get; set; }
    [JsonPropertyName("room_min")] public int[] RoomMin { get; set; }
    [JsonPropertyName("room_max")] public int[] RoomMax { get; set; }
    [JsonPropertyName("monster_house_chance")] public float MonsterHouseChance { get; set; }
}

internal class DungeonJson
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("generation")] public GenerationJson Generation { get; set; }
}

// JSON -> DungeonRule loading interface. Reads Data/dungeons.json via
// Godot's FileAccess (works both in the editor and in exported
// Windows/Android builds, unlike raw System.IO on res://).
public static class DungeonRuleLoader
{
    // Loads Data/dungeons.json and returns the DungeonRule for `dungeonId`.
    // Falls back to DungeonRule's built-in defaults (with a warning) if
    // the file is missing, malformed, or doesn't contain that id - this
    // keeps the TestScene runnable even before real dungeon data exists.
    public static DungeonRule Load(string dungeonId, string resPath = "res://Data/dungeons.json")
    {
        if (!FileAccess.FileExists(resPath))
        {
            GD.PushWarning($"DungeonRuleLoader: {resPath} not found, using default DungeonRule.");
            return new DungeonRule();
        }

        using var file = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
        string text = file.GetAsText();

        List<DungeonJson> entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<DungeonJson>>(text);
        }
        catch (JsonException e)
        {
            GD.PushError($"DungeonRuleLoader: failed to parse {resPath}: {e.Message}");
            return new DungeonRule();
        }

        var entry = entries?.Find(d => d.Id == dungeonId);
        if (entry?.Generation == null)
        {
            GD.PushWarning($"DungeonRuleLoader: id '{dungeonId}' not found in {resPath}, using default DungeonRule.");
            return new DungeonRule();
        }

        var g = entry.Generation;
        return new DungeonRule
        {
            MapWidth = g.MapWidth > 0 ? g.MapWidth : 50,
            MapHeight = g.MapHeight > 0 ? g.MapHeight : 30,
            MinLeafSize = g.MinLeafSize > 0 ? g.MinLeafSize : 8,
            RoomMinSize = g.RoomMin is { Length: 2 } ? new Vector2I(g.RoomMin[0], g.RoomMin[1]) : new Vector2I(4, 4),
            RoomMaxSize = g.RoomMax is { Length: 2 } ? new Vector2I(g.RoomMax[0], g.RoomMax[1]) : new Vector2I(9, 7),
            MonsterHouseChance = g.MonsterHouseChance,
        };
    }
}
