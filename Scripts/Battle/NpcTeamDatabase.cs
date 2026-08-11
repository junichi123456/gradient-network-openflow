using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace MysteryDungeon.Battle;

internal class NpcEntryJson
{
    [JsonPropertyName("species_id")] public string SpeciesId { get; set; }
    [JsonPropertyName("move_ids")] public List<string> MoveIds { get; set; }
    [JsonPropertyName("item_id")] public string ItemId { get; set; }
}

internal class NpcTeamJson
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("main_type")] public string MainType { get; set; }
    [JsonPropertyName("total_bst")] public int TotalBst { get; set; }
    [JsonPropertyName("entries")] public List<NpcEntryJson> Entries { get; set; }
}

// 1人ぶんのNPC対戦相手。人が操作する側の BattleTeam と同じ規則に従う
// （6匹・技4つ・持ち物の重複なし）ので、対戦のロジックから見ると
// 相手が人かNPCかは区別が付かない。
public sealed class NpcTeam
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string MainType { get; init; }
    public int TotalBst { get; init; }
    public BattleTeam Team { get; init; }

    // 相手に開示してよい範囲（種族のみ）。人の相手と同じ経路を通す。
    public List<PublicEntryView> Disclose() => BattleSession.DiscloseTeam(Team);
}

// Data/npc_teams.json を読む。マッチングが実装できる段階にないので、
// 対戦の相手はここから選ぶ。
//
// 編成は Tools/generate_npc_teams.py が作り、Tools/verify_npc_teams.py が
// 規則を検証している。読み込み側でも BattleTeam.Validate を通すので、
// 規則違反のデータは対戦に出られない。
public static class NpcTeamDatabase
{
    private static readonly List<NpcTeam> _teams = new();
    private static bool _loaded;

    public static void Load(string resPath = "res://Data/npc_teams.json")
    {
        _loaded = true;
        _teams.Clear();

        if (!FileAccess.FileExists(resPath))
        {
            GD.PushWarning($"NpcTeamDatabase: {resPath} が無いのでNPC対戦相手は0人。");
            return;
        }

        using var file = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
        List<NpcTeamJson> entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<NpcTeamJson>>(file.GetAsText());
        }
        catch (JsonException e)
        {
            GD.PushError($"NpcTeamDatabase: {resPath} の解析に失敗: {e.Message}");
            return;
        }

        foreach (var t in entries ?? new List<NpcTeamJson>())
        {
            var team = new BattleTeam((t.Entries ?? new List<NpcEntryJson>()).Select(e => new BattleEntry
            {
                SpeciesId = e.SpeciesId,
                MoveIds = e.MoveIds ?? new List<string>(),
                ItemId = e.ItemId,
            }));

            // 構築規則を通らない相手は出さない。黙って弱い相手として出すより、
            // 気づける形で落とすほうがよい（生成器の不具合がここで見える）。
            var errors = team.Validate();
            if (errors.Count > 0)
            {
                GD.PushError($"NpcTeamDatabase: {t.Name} は構築規則を満たさない: "
                             + string.Join(" / ", errors));
                continue;
            }

            _teams.Add(new NpcTeam
            {
                Id = t.Id, Name = t.Name, MainType = t.MainType,
                TotalBst = t.TotalBst, Team = team,
            });
        }
    }

    public static IReadOnlyList<NpcTeam> All
    {
        get { if (!_loaded) Load(); return _teams; }
    }

    public static NpcTeam Get(string id) => All.FirstOrDefault(t => t.Id == id);

    public static NpcTeam First() => All.Count > 0 ? All[0] : null;
}
