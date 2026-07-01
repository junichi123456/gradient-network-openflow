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
    [JsonPropertyName("min_items_per_room")] public int MinItemsPerRoom { get; set; }
    [JsonPropertyName("max_items_per_room")] public int MaxItemsPerRoom { get; set; }
    [JsonPropertyName("min_traps_per_room")] public int MinTrapsPerRoom { get; set; }
    [JsonPropertyName("max_traps_per_room")] public int MaxTrapsPerRoom { get; set; }
    [JsonPropertyName("monster_house_min_items")] public int MonsterHouseMinItems { get; set; }
    [JsonPropertyName("monster_house_max_items")] public int MonsterHouseMaxItems { get; set; }
    [JsonPropertyName("monster_house_min_traps")] public int MonsterHouseMinTraps { get; set; }
    [JsonPropertyName("monster_house_max_traps")] public int MonsterHouseMaxTraps { get; set; }
    [JsonPropertyName("min_enemy_count")] public int MinEnemyCount { get; set; }
    [JsonPropertyName("max_enemy_count")] public int MaxEnemyCount { get; set; }
    [JsonPropertyName("monster_house_min_enemies")] public int MonsterHouseMinEnemies { get; set; }
    [JsonPropertyName("monster_house_max_enemies")] public int MonsterHouseMaxEnemies { get; set; }
    [JsonPropertyName("dummy_npc_ratio")] public float DummyNpcRatio { get; set; }
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

        var defaults = new DungeonRule();
        var g = entry.Generation;
        return new DungeonRule
        {
            MapWidth = g.MapWidth > 0 ? g.MapWidth : defaults.MapWidth,
            MapHeight = g.MapHeight > 0 ? g.MapHeight : defaults.MapHeight,
            MinLeafSize = g.MinLeafSize > 0 ? g.MinLeafSize : defaults.MinLeafSize,
            RoomMinSize = g.RoomMin is { Length: 2 } ? new Vector2I(g.RoomMin[0], g.RoomMin[1]) : defaults.RoomMinSize,
            RoomMaxSize = g.RoomMax is { Length: 2 } ? new Vector2I(g.RoomMax[0], g.RoomMax[1]) : defaults.RoomMaxSize,
            MonsterHouseChance = g.MonsterHouseChance > 0 ? g.MonsterHouseChance : defaults.MonsterHouseChance,
            MinItemsPerRoom = g.MinItemsPerRoom > 0 ? g.MinItemsPerRoom : defaults.MinItemsPerRoom,
            MaxItemsPerRoom = g.MaxItemsPerRoom > 0 ? g.MaxItemsPerRoom : defaults.MaxItemsPerRoom,
            MinTrapsPerRoom = g.MinTrapsPerRoom,
            MaxTrapsPerRoom = g.MaxTrapsPerRoom > 0 ? g.MaxTrapsPerRoom : defaults.MaxTrapsPerRoom,
            MonsterHouseMinItems = g.MonsterHouseMinItems > 0 ? g.MonsterHouseMinItems : defaults.MonsterHouseMinItems,
            MonsterHouseMaxItems = g.MonsterHouseMaxItems > 0 ? g.MonsterHouseMaxItems : defaults.MonsterHouseMaxItems,
            MonsterHouseMinTraps = g.MonsterHouseMinTraps > 0 ? g.MonsterHouseMinTraps : defaults.MonsterHouseMinTraps,
            MonsterHouseMaxTraps = g.MonsterHouseMaxTraps > 0 ? g.MonsterHouseMaxTraps : defaults.MonsterHouseMaxTraps,
            MinEnemyCount = g.MinEnemyCount > 0 ? g.MinEnemyCount : defaults.MinEnemyCount,
            MaxEnemyCount = g.MaxEnemyCount > 0 ? g.MaxEnemyCount : defaults.MaxEnemyCount,
            MonsterHouseMinEnemies = g.MonsterHouseMinEnemies > 0 ? g.MonsterHouseMinEnemies : defaults.MonsterHouseMinEnemies,
            MonsterHouseMaxEnemies = g.MonsterHouseMaxEnemies > 0 ? g.MonsterHouseMaxEnemies : defaults.MonsterHouseMaxEnemies,
            DummyNpcRatio = g.DummyNpcRatio > 0 ? g.DummyNpcRatio : defaults.DummyNpcRatio,
        };
    }
}
