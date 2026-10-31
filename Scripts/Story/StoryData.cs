using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MysteryDungeon.Story;

public class UnlockDef
{
    [JsonPropertyName("charts")] public List<string> Charts { get; set; } = new();
    [JsonPropertyName("flags")] public List<string> Flags { get; set; } = new();
}

public class InsertAfterDef
{
    [JsonPropertyName("chart")] public string Chart { get; set; } = "";
    [JsonPropertyName("event")] public string Event { get; set; } = "";
}

public class ChartDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("part")] public int Part { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "main"; // main/supplement/extra
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("unlock")] public UnlockDef Unlock { get; set; } = new();
    [JsonPropertyName("dungeons")] public List<string> Dungeons { get; set; } = new();
    [JsonPropertyName("events")] public List<string> Events { get; set; } = new();
    [JsonPropertyName("hub_changes")] public List<string> HubChanges { get; set; } = new();
    [JsonPropertyName("insert_after")] public InsertAfterDef InsertAfter { get; set; }
}

public class BossSpecDef
{
    [JsonPropertyName("species_id")] public string SpeciesId { get; set; } = "";
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("hp_mult")] public double HpMult { get; set; } = 1.0;
    [JsonPropertyName("atk_mult")] public double AtkMult { get; set; } = 1.0;
    [JsonPropertyName("defeat_ok")] public bool DefeatOk { get; set; }
}

public class DungeonDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("chart")] public string Chart { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("floors")] public int Floors { get; set; }
    [JsonPropertyName("rule_id")] public string RuleId { get; set; } = "";
    [JsonPropertyName("end_type")] public string EndType { get; set; } = "FreeDungeonBoss";
    [JsonPropertyName("boss")] public BossSpecDef Boss { get; set; }
}

public class CharacterDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("species_id")] public string SpeciesId { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("mbti")] public string Mbti { get; set; } = "";
    [JsonPropertyName("first_chart")] public string FirstChart { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "";
}

public class VoiceDef
{
    [JsonPropertyName("first_person")] public string FirstPerson { get; set; } = "";
    [JsonPropertyName("speech")] public string Speech { get; set; } = "";
    [JsonPropertyName("catch")] public string Catch { get; set; } = "";
    [JsonPropertyName("emotion")] public string Emotion { get; set; } = "";
}

public class SelectionOption
{
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("letter")] public string Letter { get; set; } = "";
}

public class SelectionQuestion
{
    [JsonPropertyName("axis")] public string Axis { get; set; } = ""; // "EI" "SN" "TF" "JP"
    [JsonPropertyName("scene")] public string Scene { get; set; } = "";
    [JsonPropertyName("options")] public List<SelectionOption> Options { get; set; } = new();
}

public class SelectionDef
{
    [JsonPropertyName("questions")] public List<SelectionQuestion> Questions { get; set; } = new();
    [JsonPropertyName("type_to_species")] public Dictionary<string, string> TypeToSpecies { get; set; } = new();
}

public class TriggerDef
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("requires")] public List<string> Requires { get; set; } = new();
    [JsonPropertyName("once")] public bool Once { get; set; } = true;
    [JsonPropertyName("chart")] public string Chart { get; set; } = "";
    [JsonPropertyName("dungeon")] public string Dungeon { get; set; } = "";
    [JsonPropertyName("floor")] public int Floor { get; set; }
    [JsonPropertyName("days")] public int Days { get; set; }
}

public class EventDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("trigger")] public TriggerDef Trigger { get; set; }  // null = call_event 専用
    [JsonPropertyName("steps")] public JsonArray Steps { get; set; } = new();
}
