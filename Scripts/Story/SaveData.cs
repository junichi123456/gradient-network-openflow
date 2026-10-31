using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MysteryDungeon.Story;

// セーブ形式 { version, story, hub, party, inventory }（基本仕様 §4）。
public class SaveData
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")] public int Version { get; set; } = CurrentVersion;
    [JsonPropertyName("story")] public StoryState Story { get; set; } = new();
    [JsonPropertyName("hub")] public HubSave Hub { get; set; } = new();
    [JsonPropertyName("party")] public PartySave Party { get; set; } = new();
    [JsonPropertyName("inventory")] public List<InventoryEntry> Inventory { get; set; } = new();
}

public class HubSave
{
    [JsonPropertyName("facility_levels")] public Dictionary<string, int> FacilityLevels { get; set; } = new();
    [JsonPropertyName("material_bank")] public Dictionary<string, int> MaterialBank { get; set; } = new();
    // ポイントと倉庫在庫。version は上げない（旧セーブは欠損 = 0 / 空で読める）。
    [JsonPropertyName("points")] public int Points { get; set; }
    [JsonPropertyName("storage")] public Dictionary<string, int> Storage { get; set; } = new();
}

public class PartySave
{
    [JsonPropertyName("protagonist")] public string Protagonist { get; set; } = "";
    [JsonPropertyName("partner")] public string Partner { get; set; } = "";
    [JsonPropertyName("roster")] public List<string> Roster { get; set; } = new();
    [JsonPropertyName("active")] public List<string> Active { get; set; } = new();
}

public class InventoryEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("n")] public int N { get; set; }
}
