using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MysteryDungeon.Entities;
using MysteryDungeon.Species;

namespace MysteryDungeon.Battle;

internal class NpcEntryJson
{
    [JsonPropertyName("species_id")] public string SpeciesId { get; set; }
    [JsonPropertyName("move_ids")] public List<string> MoveIds { get; set; }
    [JsonPropertyName("item_id")] public string ItemId { get; set; }

    // 省略可。省略時は StatSpread.Auto（役割に応じた自動配分）。
    [JsonPropertyName("breakthrough")] public NpcBreakthroughJson Breakthrough { get; set; }
    [JsonPropertyName("nature")] public NpcNatureJson Nature { get; set; }
}

internal class NpcBreakthroughJson
{
    [JsonPropertyName("hp")] public int Hp { get; set; }
    [JsonPropertyName("atk")] public int Atk { get; set; }
    [JsonPropertyName("def")] public int Def { get; set; }
    [JsonPropertyName("spatk")] public int SpAtk { get; set; }
    [JsonPropertyName("spdef")] public int SpDef { get; set; }
    [JsonPropertyName("speed")] public int Speed { get; set; }
}

internal class NpcNatureJson
{
    [JsonPropertyName("up")] public string Up { get; set; }
    [JsonPropertyName("down")] public string Down { get; set; }
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

    // JSON に指定があればそれを、無ければ自動配分。指定は片方だけでも
    // 省略側は Zero/無補正から始める（検証は team.Validate が行う）。
    private static StatSpread SpreadOf(NpcEntryJson e)
    {
        if (e.Breakthrough == null && e.Nature == null)
            return StatSpread.Auto(SpeciesDatabase.Instance?.Get(e.SpeciesId));

        var s = StatSpread.Zero();
        if (e.Breakthrough != null)
        {
            s.Hp = e.Breakthrough.Hp; s.Atk = e.Breakthrough.Atk; s.Def = e.Breakthrough.Def;
            s.SpAtk = e.Breakthrough.SpAtk; s.SpDef = e.Breakthrough.SpDef; s.Speed = e.Breakthrough.Speed;
        }
        if (e.Nature != null)
        {
            s.NatureUp = StatSpread.ParseNature(e.Nature.Up);
            s.NatureDown = StatSpread.ParseNature(e.Nature.Down);
        }
        return s;
    }

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
                Spread = SpreadOf(e),
                UseTrait2 = BattleEntry.AutoUseTrait2(SpeciesDatabase.Instance?.Get(e.SpeciesId)),   // NPCは追加特性（乱数なし）
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
