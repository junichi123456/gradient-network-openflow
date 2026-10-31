using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MysteryDungeon.Dungeon;

namespace MysteryDungeon.Story;

// Data/story/*.json を一括ロードし、参照切れを列挙する（純 C#。読み出しは reader を渡す）。
// 起動時は StoryBootstrap（autoload）が Load + Validate して、エラーを PushError する。
public class StoryDatabase
{
    public static StoryDatabase Instance { get; set; }

    public static readonly string[] TriggerTypes =
        { "hub_enter", "dungeon_clear", "dungeon_floor", "boss_defeated", "chart_start", "chart_end", "time_passed" };
    public static readonly string[] FixedSpeakers = { "protagonist", "partner", "narrator" };

    public List<ChartDef> Charts { get; private set; } = new();
    public List<DungeonDef> Dungeons { get; private set; } = new();
    public List<CharacterDef> Characters { get; private set; } = new();
    public Dictionary<string, VoiceDef> Voices { get; private set; } = new();
    public SelectionDef Selection { get; private set; } = new();
    public List<EventDef> Events { get; private set; } = new();
    public LevelCurveDef LevelCurve { get; private set; } = new();
    public List<PalPlanRow> PalPlan { get; private set; } = new();

    public ChartDef Chart(string id) => Charts.FirstOrDefault(c => c.Id == id);
    public DungeonDef Dungeon(string id) => Dungeons.FirstOrDefault(d => d.Id == id);
    public CharacterDef Character(string id) => Characters.FirstOrDefault(c => c.Id == id);
    public EventDef Event(string id) => Events.FirstOrDefault(e => e.Id == id);
    // 種族ID → 話者定義（プレイアブル16体は MBTI を持つ）。
    public CharacterDef CharacterBySpecies(string speciesId) => Characters.FirstOrDefault(c => c.SpeciesId == speciesId);

    // ---- 野生出現・レベル ----

    // チャート番号（本編 1〜20）に対するボスレベル。
    public int BossLevelAt(int chart) => LevelCurve.At(chart);

    // ダンジョンのボスレベル: 追加枠は固定値、それ以外は level_chart の曲線値。
    public int BossLevelFor(DungeonDef d)
    {
        var c = Chart(d.Chart);
        if (c == null) return LevelCurve.FirstLevel;
        if (c.Kind == "extra") return d.Id == "x43" ? LevelCurve.RematchLevel : LevelCurve.LegendaryLevel;
        return LevelCurve.At(c.LevelChart > 0 ? c.LevelChart : ChartNumber(c.Id));
    }

    public static int ChartNumber(string chartId)
        => chartId.Length > 1 && int.TryParse(chartId.AsSpan(1), out var n) ? n : 0;

    // 野生で出る種族: pal_plan の dungeons[] に当該ダンジョンを持ち、wild=true で、
    // 初登場チャートがダンジョンの（実効）チャート以前のもの。ボス種族は除く。
    public List<string> WildPool(string dungeonId)
    {
        var d = Dungeon(dungeonId);
        var pool = new List<string>();
        if (d == null || d.NoWild) return pool;
        var c = Chart(d.Chart);
        int eff = c == null ? 0 : (c.LevelChart > 0 ? c.LevelChart : ChartNumber(c.Id));
        foreach (var r in PalPlan)
        {
            if (!r.Wild || !r.Dungeons.Contains(dungeonId)) continue;
            if (r.FirstChart != null && r.FirstChart > eff) continue;
            if (r.BossDungeons.Contains(dungeonId)) continue;
            pool.Add(r.SpeciesId);
        }
        return pool;
    }

    // 通常ダンジョンの「実効チャート」（第1水準の野生レベル用）。
    public int EffectiveChart(DungeonDef d)
    {
        var c = Chart(d.Chart);
        return c == null ? 1 : (c.LevelChart > 0 ? c.LevelChart : Math.Max(1, ChartNumber(c.Id)));
    }

    // reader: "charts.json" や "events/c01.json" のような相対パス → 本文（無ければ null）。
    // eventFiles: events/ 以下のファイル名一覧。
    public static StoryDatabase Load(Func<string, string> reader, IEnumerable<string> eventFiles, List<string> errors)
    {
        var db = new StoryDatabase();
        db.Charts = Read<List<ChartDef>>(reader, "charts.json", errors) ?? new();
        db.Dungeons = Read<List<DungeonDef>>(reader, "dungeons.json", errors) ?? new();
        db.Characters = Read<List<CharacterDef>>(reader, "characters.json", errors) ?? new();
        db.Voices = Read<Dictionary<string, VoiceDef>>(reader, "voice.json", errors) ?? new();
        db.Selection = Read<SelectionDef>(reader, "selection.json", errors) ?? new();
        db.LevelCurve = Read<LevelCurveDef>(reader, "level_curve.json", errors) ?? new();
        db.PalPlan = Read<List<PalPlanRow>>(reader, "pal_plan.json", errors) ?? new();
        foreach (var f in eventFiles.OrderBy(x => x, StringComparer.Ordinal))
            db.Events.AddRange(Read<List<EventDef>>(reader, "events/" + f, errors) ?? new());
        return db;
    }

    private static T Read<T>(Func<string, string> reader, string path, List<string> errors) where T : class
    {
        string text = reader(path);
        if (text == null) { errors.Add($"{path} が見つからない"); return null; }
        try { return JsonSerializer.Deserialize<T>(text); }
        catch (JsonException e) { errors.Add($"{path} の解析に失敗: {e.Message}"); return null; }
    }

    private static readonly Regex Placeholder = new(@"\{([A-Za-z_]+)(?::([^}]*))?\}");
    public static readonly string[] SimplePlaceholders = { "I", "catch", "player_name", "protagonist", "partner", "arrivals" };

    // 参照切れを全部返す。空なら健全。
    public List<string> Validate(Func<string, bool> speciesExists, Func<string, bool> ruleExists, ICollection<string> knownOps)
    {
        var e = new List<string>();
        void Dup(IEnumerable<string> ids, string what)
        {
            foreach (var g in ids.GroupBy(x => x).Where(g => g.Count() > 1)) e.Add($"{what} の id が重複: {g.Key}");
        }
        Dup(Charts.Select(c => c.Id), "chart"); Dup(Dungeons.Select(d => d.Id), "dungeon");
        Dup(Characters.Select(c => c.Id), "character"); Dup(Events.Select(x => x.Id), "event");

        foreach (var c in Charts)
        {
            foreach (var u in c.Unlock.Charts) if (Chart(u) == null) e.Add($"chart {c.Id}: unlock.charts '{u}' が無い");
            foreach (var d in c.Dungeons) if (Dungeon(d) == null) e.Add($"chart {c.Id}: dungeon '{d}' が無い");
            foreach (var ev in c.Events) if (Event(ev) == null) e.Add($"chart {c.Id}: event '{ev}' が無い");
            if (c.InsertAfter != null)
            {
                var host = Chart(c.InsertAfter.Chart);
                string ie = c.InsertAfter.Event;
                if (host == null) e.Add($"chart {c.Id}: insert_after.chart '{c.InsertAfter.Chart}' が無い");
                else if (ie != "chart_end")
                {
                    // イベントIDは cNN_eMM（前方一致。MM = 台本の流れ番号）。本文が未作成のチャートは形式だけ検査する。
                    bool form = Regex.IsMatch(ie, "^" + host.Id + @"_e\d\d(_.*)?$");
                    bool found = host.Events.Count == 0 || host.Events.Any(x => x == ie || x.StartsWith(ie + "_"));
                    if (!form || !found) e.Add($"chart {c.Id}: insert_after.event '{ie}' が {host.Id} に無い");
                }
            }
        }
        foreach (var d in Dungeons)
        {
            if (Chart(d.Chart) == null) e.Add($"dungeon {d.Id}: chart '{d.Chart}' が無い");
            if (!ruleExists(d.RuleId)) e.Add($"dungeon {d.Id}: rule_id '{d.RuleId}' が Data/dungeons.json に無い");
            if (!Enum.TryParse<DungeonEndType>(d.EndType, out _)) e.Add($"dungeon {d.Id}: end_type '{d.EndType}' が不正");
            if (d.Floors < 1) e.Add($"dungeon {d.Id}: floors が不正");
            if (d.Boss != null && !speciesExists(d.Boss.SpeciesId)) e.Add($"dungeon {d.Id}: boss 種族 '{d.Boss.SpeciesId}' が無い");
            if (d.Boss != null) foreach (var g in d.Boss.Group)
                if (!speciesExists(g.SpeciesId)) e.Add($"dungeon {d.Id}: group 種族 '{g.SpeciesId}' が無い");
            if (d.Boss == null && Enum.TryParse<DungeonEndType>(d.EndType, out var et)
                && et is DungeonEndType.FreeDungeonBoss or DungeonEndType.StoryBoss or DungeonEndType.StoryMultiEnemyBattle
                && !d.PostClear)
                e.Add($"dungeon {d.Id}: end_type {d.EndType} に boss が無い");
        }
        foreach (var r in PalPlan)
        {
            if (!speciesExists(r.SpeciesId)) e.Add($"pal_plan: 種族 '{r.SpeciesId}' が無い");
            foreach (var did in r.Dungeons.Concat(r.BossDungeons))
                if (!did.StartsWith("x") && Dungeon(did) == null) e.Add($"pal_plan {r.SpeciesId}: dungeon '{did}' が無い");
        }
        foreach (var c in Characters)
        {
            if (!speciesExists(c.SpeciesId)) e.Add($"character {c.Id}: 種族 '{c.SpeciesId}' が無い");
            if (!Voices.ContainsKey(c.Mbti)) e.Add($"character {c.Id}: mbti '{c.Mbti}' が voice.json に無い");
        }
        foreach (var (t, sp) in Selection.TypeToSpecies)
        {
            if (!Voices.ContainsKey(t)) e.Add($"selection: タイプ '{t}' が voice.json に無い");
            if (!speciesExists(sp)) e.Add($"selection: 種族 '{sp}' が無い");
        }
        foreach (var ev in Events) ValidateEvent(ev, e, knownOps);
        return e;
    }

    private void ValidateEvent(EventDef ev, List<string> e, ICollection<string> knownOps)
    {
        if (ev.Trigger != null)
        {
            if (!TriggerTypes.Contains(ev.Trigger.Type)) e.Add($"event {ev.Id}: trigger '{ev.Trigger.Type}' が不正");
            if (ev.Trigger.Chart != "" && Chart(ev.Trigger.Chart) == null) e.Add($"event {ev.Id}: trigger.chart が無い");
            if (ev.Trigger.Dungeon != "" && Dungeon(ev.Trigger.Dungeon) == null) e.Add($"event {ev.Id}: trigger.dungeon が無い");
        }
        void Steps(System.Text.Json.Nodes.JsonArray steps)
        {
            foreach (var n in steps)
            {
                if (n is not System.Text.Json.Nodes.JsonObject s) { e.Add($"event {ev.Id}: step がオブジェクトでない"); continue; }
                string op = s["op"]?.GetValue<string>() ?? "";
                if (!knownOps.Contains(op)) { e.Add($"event {ev.Id}: 未知の op '{op}'"); continue; }
                void Id(string key, Func<string, bool> ok, string what)
                {
                    string v = s[key]?.GetValue<string>();
                    if (v == null || !ok(v)) e.Add($"event {ev.Id}: {op}.{key} '{v}' の{what}が無い");
                }
                switch (op)
                {
                    case "say":
                        string who = s["who"]?.GetValue<string>() ?? "";
                        if (!FixedSpeakers.Contains(who) && Character(who) == null) e.Add($"event {ev.Id}: who '{who}' が解決しない");
                        var texts = new List<string>();
                        if (s["text"] != null) texts.Add(s["text"].GetValue<string>());
                        if (s["by_type"] is System.Text.Json.Nodes.JsonObject bt)
                            foreach (var (k, v) in bt)
                            {
                                if (!Voices.ContainsKey(k)) e.Add($"event {ev.Id}: by_type '{k}' が不正");
                                texts.Add(v?.GetValue<string>() ?? "");
                            }
                        if (texts.Count == 0) e.Add($"event {ev.Id}: say に text も by_type も無い");
                        if (s["by_type"] != null && s["text"] == null && who is "protagonist" or "partner" && ((System.Text.Json.Nodes.JsonObject)s["by_type"]).Count < Voices.Count)
                            e.Add($"event {ev.Id}: text が無いのに by_type が16タイプ揃っていない");
                        foreach (var t in texts) CheckPlaceholders(ev.Id, t, e);
                        break;
                    case "narration":
                        CheckPlaceholders(ev.Id, s["text"]?.GetValue<string>() ?? "", e);
                        break;
                    case "unlock_dungeon": case "start_scenario_fight": Id("id", x => Dungeon(x) != null, "ダンジョン"); break;
                    case "join_resident": Id("id", x => Character(x) != null, "人物"); break;
                    case "join_unchosen": if (s["slot"] == null) e.Add($"event {ev.Id}: join_unchosen に slot が無い"); break;
                    case "goto_chart": Id("id", x => Chart(x) != null, "チャート"); break;
                    case "call_event": Id("id", x => Event(x) != null, "イベント"); break;
                    case "choice":
                        foreach (var o in s["options"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray())
                            if (o?["steps"] is System.Text.Json.Nodes.JsonArray sub) Steps(sub);
                        break;
                    case "if":
                        if (s["then"] is System.Text.Json.Nodes.JsonArray t2) Steps(t2);
                        if (s["else"] is System.Text.Json.Nodes.JsonArray e2) Steps(e2);
                        break;
                }
            }
        }
        Steps(ev.Steps);
    }

    private void CheckPlaceholders(string evId, string text, List<string> e)
    {
        foreach (Match m in Placeholder.Matches(text))
        {
            string key = m.Groups[1].Value;
            if (key == "name")
            {
                if (Character(m.Groups[2].Value) == null) e.Add($"event {evId}: {{name:{m.Groups[2].Value}}} の人物が無い");
            }
            else if (!SimplePlaceholders.Contains(key)) e.Add($"event {evId}: 未知のプレースホルダ {m.Value}");
        }
    }
}
