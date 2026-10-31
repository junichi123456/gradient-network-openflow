using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MysteryDungeon.Story;

public class UnlockDef
{
    [JsonPropertyName("charts")] public List<string> Charts { get; set; } = new();
    [JsonPropertyName("flags")] public List<string> Flags { get; set; } = new();
    // 補完21〜30（ハコニワの住民イベント）の解放条件。評価は P9。0 = 条件なし。
    [JsonPropertyName("days")] public int Days { get; set; }
    [JsonPropertyName("affinity")] public int Affinity { get; set; }
    // true のとき、クリア後（PostClear）でなければ開かない。
    [JsonPropertyName("post_clear")] public bool PostClear { get; set; }
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
    // 補完チャートを挿入点で出すときの案内文（スキップ可）。
    [JsonPropertyName("offer")] public string Offer { get; set; } = "";
    // true のチャートの完了でエンディング（PostClear）に入る。
    [JsonPropertyName("ends_game")] public bool EndsGame { get; set; }
    // 本編チャート番号（レベル曲線の入力）。補完は挿入先、追加枠は 0。
    [JsonPropertyName("level_chart")] public int LevelChart { get; set; }
}

public class BossSpecDef
{
    [JsonPropertyName("species_id")] public string SpeciesId { get; set; } = "";
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("hp_mult")] public double HpMult { get; set; } = 1.0;
    [JsonPropertyName("atk_mult")] public double AtkMult { get; set; } = 1.0;
    [JsonPropertyName("defeat_ok")] public bool DefeatOk { get; set; }
    // 2体ボス・群れ戦の追加メンバー（主ボスに加えて同時に出す）。
    [JsonPropertyName("group")] public List<BossSpecDef> Group { get; set; } = new();
    // 撃破後の処理（戦闘後共闘など）。
    [JsonPropertyName("post_battle")] public PostBattleDef PostBattle { get; set; }
}

// ボス撃破後の共闘: coop_character（characters.json の id）が仲間として、coop_dungeon の間だけ同行する。
public class PostBattleDef
{
    [JsonPropertyName("coop_character")] public string CoopCharacter { get; set; } = "";
    [JsonPropertyName("coop_dungeon")] public string CoopDungeon { get; set; } = "";
}

public class LevelCurveDef
{
    [JsonPropertyName("first_chart")] public int FirstChart { get; set; } = 1;
    [JsonPropertyName("first_level")] public int FirstLevel { get; set; } = 5;
    [JsonPropertyName("last_chart")] public int LastChart { get; set; } = 20;
    [JsonPropertyName("last_level")] public int LastLevel { get; set; } = 60;
    [JsonPropertyName("legendary_level")] public int LegendaryLevel { get; set; } = 70;
    [JsonPropertyName("rematch_level")] public int RematchLevel { get; set; } = 65;
    [JsonPropertyName("wild_offset")] public int WildOffset { get; set; } = -3;

    public int At(int chart)
    {
        if (chart <= FirstChart) return FirstLevel;
        if (chart >= LastChart) return LastLevel;
        return (int)System.Math.Round(FirstLevel + (chart - FirstChart) * (double)(LastLevel - FirstLevel) / (LastChart - FirstChart));
    }
}

public class PalPlanRow
{
    [JsonPropertyName("species_id")] public string SpeciesId { get; set; } = "";
    [JsonPropertyName("first_chart")] public int? FirstChart { get; set; }
    [JsonPropertyName("dungeons")] public List<string> Dungeons { get; set; } = new();
    [JsonPropertyName("boss_dungeons")] public List<string> BossDungeons { get; set; } = new();
    [JsonPropertyName("wild")] public bool Wild { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "normal";
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
    // ダンジョン選択に出さない（シナリオ戦から入る隠しダンジョン）。
    [JsonPropertyName("hidden")] public bool Hidden { get; set; }
    // 追加枠（クリア後に開く）。
    [JsonPropertyName("post_clear")] public bool PostClear { get; set; }
    // 野生種を出さない（枠だけの追加ダンジョン等）。
    [JsonPropertyName("no_wild")] public bool NoWild { get; set; }
    // このダンジョンを選べるようになる条件: ここに挙げたダンジョンをすべてクリア済み（解放済みに加えて）。
    [JsonPropertyName("requires_cleared")] public List<string> RequiresCleared { get; set; } = new();
}

public class CharacterDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("species_id")] public string SpeciesId { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("mbti")] public string Mbti { get; set; } = "";
    [JsonPropertyName("first_chart")] public string FirstChart { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    // 一人称の上書き（ブシガエル「拙者」・レヴィドラ「我」・ゼノグリフ「わたしたち」）。
    [JsonPropertyName("first_person")] public string FirstPerson { get; set; } = "";
    // ハコニワ常駐の開始チャート（空 = 常駐しない）。
    [JsonPropertyName("resident_from")] public string ResidentFrom { get; set; } = "";
    [JsonPropertyName("speech_style")] public string SpeechStyle { get; set; } = "";
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
