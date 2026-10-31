using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MysteryDungeon.Story;

// 進行状態。Godot 非依存の純データクラス（ヘッドレスで往復テストできる）。
// 基本仕様 §4 + 確定仕様書 §1-§2 の追加フィールド。
public class StoryState
{
    public const int DefaultIntrovertion = 85;

    [JsonPropertyName("current_chart")] public string CurrentChart { get; set; } = "";
    [JsonPropertyName("completed_charts")] public HashSet<string> CompletedCharts { get; set; } = new();
    [JsonPropertyName("flags")] public HashSet<string> Flags { get; set; } = new();
    [JsonPropertyName("counters")] public Dictionary<string, int> Counters { get; set; } = new();
    [JsonPropertyName("protagonist")] public string Protagonist { get; set; } = "";
    [JsonPropertyName("partner")] public string Partner { get; set; } = "";
    [JsonPropertyName("protagonist_introvertion")] public int ProtagonistIntrovertion { get; set; } = DefaultIntrovertion;
    [JsonPropertyName("cleared_dungeons")] public HashSet<string> ClearedDungeons { get; set; } = new();
    [JsonPropertyName("unlocked_dungeons")] public HashSet<string> UnlockedDungeons { get; set; } = new();
    [JsonPropertyName("residents_arrived")] public HashSet<string> ResidentsArrived { get; set; } = new();
    [JsonPropertyName("fired_events")] public HashSet<string> FiredEvents { get; set; } = new();
    [JsonPropertyName("choice_path")] public string ChoicePath { get; set; } = "";
    [JsonPropertyName("post_clear")] public bool PostClear { get; set; }
    [JsonPropertyName("player_name")] public string PlayerName { get; set; } = "";
    [JsonPropertyName("selection_seed")] public long SelectionSeed { get; set; }
    [JsonPropertyName("mbti_type")] public string MbtiType { get; set; } = "";
    [JsonPropertyName("skipped_supplements")] public HashSet<string> SkippedSupplements { get; set; } = new();
    [JsonPropertyName("memory_collected")] public HashSet<string> MemoryCollected { get; set; } = new();

    // P9: ハコニワの日数（ダンジョンから戻るたびに+1）と、住民と話した回数（親密度）。
    [JsonPropertyName("days")] public int Days { get; set; }
    [JsonPropertyName("affinity")] public int Affinity { get; set; }
    // P8: 勧誘の乱数（固定シード + 引いた回数。セーブして再現する）と、仲間になった種族（ハコニワの住民として表示）。
    [JsonPropertyName("recruit_seed")] public long RecruitSeed { get; set; }
    [JsonPropertyName("recruit_rolls")] public int RecruitRolls { get; set; }
    [JsonPropertyName("recruited_pals")] public HashSet<string> RecruitedPals { get; set; } = new();
    // P5: 戦闘後共闘。ボスを倒したあと、次のダンジョン（CoopDungeon）だけ同行する種族。
    [JsonPropertyName("coop_species")] public string CoopSpecies { get; set; } = "";
    [JsonPropertyName("coop_dungeon")] public string CoopDungeon { get; set; } = "";

    public bool HasFlag(string flag) => Flags.Contains(flag);
    public void SetFlag(string flag) => Flags.Add(flag);
    public int GetCounter(string key) => Counters.GetValueOrDefault(key);
    public int IncCounter(string key, int n = 1) => Counters[key] = GetCounter(key) + n;

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = false };

    public string ToJson() => JsonSerializer.Serialize(this, Opts);
    public static StoryState FromJson(string json) => JsonSerializer.Deserialize<StoryState>(json, Opts) ?? new StoryState();

    // 全フィールドを順序非依存で文字列化（往復一致テスト用）。
    public string Fingerprint()
    {
        static string S(IEnumerable<string> x) => string.Join(",", x.OrderBy(v => v, StringComparer.Ordinal));
        return string.Join("|", CurrentChart, S(CompletedCharts), S(Flags),
            S(Counters.Select(kv => kv.Key + "=" + kv.Value)), Protagonist, Partner, ProtagonistIntrovertion,
            S(ClearedDungeons), S(UnlockedDungeons), S(ResidentsArrived), S(FiredEvents), ChoicePath, PostClear,
            PlayerName, SelectionSeed, MbtiType, S(SkippedSupplements), S(MemoryCollected),
            Days, Affinity, RecruitSeed, RecruitRolls, S(RecruitedPals), CoopSpecies, CoopDungeon);
    }
}
