using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MysteryDungeon.Combat;

internal class ItemJson
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; }
    [JsonPropertyName("effect_target")] public string EffectTarget { get; set; }
    [JsonPropertyName("effect_value")] public int EffectValue { get; set; }
    [JsonPropertyName("element_type")] public string ElementType { get; set; }
    [JsonPropertyName("battle_effect")] public string BattleEffect { get; set; }
    [JsonPropertyName("consumed_on_trigger")] public bool ConsumedOnTrigger { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; }
}

// JSON-driven item definitions. Mirrors MoveDatabase: Get()/AllIds()
// lazy-load on first use, since FloorController can place items during
// floor generation before the scene root's _Ready() (which calls Load()
// explicitly) has run.
public static class ItemDatabase
{
    private static readonly Dictionary<string, ItemData> _items = new();
    private static bool _loaded;

    public static void Load(string resPath = "res://Data/items.json")
    {
        _loaded = true;
        _items.Clear();

        if (!FileAccess.FileExists(resPath))
        {
            GD.PushWarning($"ItemDatabase: {resPath} not found, no items available.");
            return;
        }

        using var file = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
        string text = file.GetAsText();

        List<ItemJson> entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<ItemJson>>(text);
        }
        catch (JsonException e)
        {
            GD.PushError($"ItemDatabase: failed to parse {resPath}: {e.Message}");
            return;
        }

        if (entries == null) return;

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Id)) continue;

            _items[entry.Id] = new ItemData
            {
                Id = entry.Id,
                Name = entry.Name,
                Type = Enum.TryParse<ItemType>(entry.Type, out var type) ? type : ItemType.Consumable,
                EffectTarget = Enum.TryParse<ItemEffectTarget>(entry.EffectTarget, out var target) ? target : ItemEffectTarget.Hp,
                EffectValue = entry.EffectValue,
                ElementType = entry.ElementType,
                BattleEffect = Enum.TryParse<BattleItemEffect>(entry.BattleEffect, out var be) ? be : BattleItemEffect.None,
                ConsumedOnTrigger = entry.ConsumedOnTrigger,
                Description = entry.Description ?? "",
            };
        }

        GD.Print($"[ItemDatabase] Loaded {_items.Count} items from {resPath}.");
    }

    public static ItemData Get(string itemId)
    {
        if (!_loaded) Load();
        return itemId != null && _items.TryGetValue(itemId, out var data) ? data : null;
    }

    public static List<string> AllIds()
    {
        if (!_loaded) Load();
        return new List<string>(_items.Keys);
    }
}
