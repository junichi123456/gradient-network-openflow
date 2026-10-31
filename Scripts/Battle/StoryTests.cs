using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MysteryDungeon.Entities;
using MysteryDungeon.Hub;
using MysteryDungeon.Species;
using MysteryDungeon.Story;

namespace MysteryDungeon.Battle;

// シナリオ基盤（P0〜P2）のヘッドレス検証。BattleTestScene から呼ぶ。
public static partial class StoryTests
{
    private sealed class RecHost : IStoryHost
    {
        public readonly List<string> Lines = new();
        public int ChoiceIndex;
        public string NameToGive = "テスト名";
        public Task Say(SayLine l) { Lines.Add($"{l.DisplayName}:{l.Text}"); return Task.CompletedTask; }
        public Task Narration(string t) { Lines.Add("N:" + t); return Task.CompletedTask; }
        public Task<int> Choice(IReadOnlyList<string> o) { Lines.Add("C:" + string.Join("/", o)); return Task.FromResult(Math.Min(ChoiceIndex, o.Count - 1)); }
        public Task<string> NameInput(string d, int m) => Task.FromResult(NameToGive);
        public Task Fade(string m, double s) => Task.CompletedTask;
        public Task Wait(double s) => Task.CompletedTask;
    }

    private static string ReadRes(string rel)
    {
        string p = StoryBootstrap.Dir + rel;
        if (!Godot.FileAccess.FileExists(p)) return null;
        using var f = Godot.FileAccess.Open(p, Godot.FileAccess.ModeFlags.Read);
        return f.GetAsText();
    }

    private static readonly string[] EventFiles = Godot.DirAccess.GetFilesAt(StoryBootstrap.Dir + "events").Where(f => f.EndsWith(".json")).ToArray();
    private static bool SpeciesOk(string id) => SpeciesDatabase.Instance.Get(id) != null;
    private static HashSet<string> _rules;
    private static bool RuleOk(string id)
    {
        if (_rules == null)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(ReadResAbs("res://Data/dungeons.json"));
            _rules = doc.RootElement.EnumerateArray().Select(d => d.GetProperty("id").GetString()).ToHashSet();
        }
        return _rules.Contains(id);
    }
    private static string ReadResAbs(string p)
    {
        using var f = Godot.FileAccess.Open(p, Godot.FileAccess.ModeFlags.Read);
        return f.GetAsText();
    }

    private static StoryDatabase Db(Func<string, string> reader, out List<string> errs, IEnumerable<string> eventFiles = null)
    {
        errs = new List<string>();
        var db = StoryDatabase.Load(reader, eventFiles ?? EventFiles, errs);
        errs.AddRange(db.Validate(SpeciesOk, RuleOk, new EventInterpreter(db, new StoryState(), null).OpNames.ToList()));
        return db;
    }

    private static StoryState HeroState() => new() { MbtiType = "ISTP", Protagonist = "027B", Partner = "004", PlayerName = "ミナト" };

    private static EventInterpreter It(StoryDatabase db, StoryState st, RecHost h)
        => new(db, st, h) { SpeciesName = id => SpeciesDatabase.Instance.Get(id)?.DisplayName ?? id };

    public static void Run(Node sceneHost)
    {
        void Check(string label, bool ok, string detail) => GD.Print($"[検証] {label}: {(ok ? "OK" : "NG")} ({detail})");

        // ---- P0: データ・状態・セーブ ----
        var db = Db(ReadRes, out var errs);
        Check("StoryDatabase が参照切れ0で読める", errs.Count == 0 && db.Charts.Count == 44 && db.Characters.Count >= 32,
              errs.Count == 0 ? $"chart{db.Charts.Count} char{db.Characters.Count} event{db.Events.Count}" : errs[0]);

        string Bad(Func<string, string> mod, string needle)
        {
            Db(p => mod(p), out var e);
            return e.Any(x => x.Contains(needle)) ? "" : $"'{needle}' が検出されない: {string.Join(" / ", e)}";
        }
        string miss = Bad(p => p == "dungeons.json" ? ReadRes(p).Replace("\"088B\"", "\"999Z\"") : ReadRes(p), "999Z")
            + Bad(p => p == "charts.json" ? ReadRes(p).Replace("d01_c1", "d_none") : ReadRes(p), "d_none")
            + Bad(p => p == "selection.json" ? ReadRes(p).Replace("\"024\"", "\"998Z\"") : ReadRes(p), "998Z")
            + Bad(p => p == "events/c01.json" ? ReadRes(p).Replace("\"partner\"", "\"nobody\"") : ReadRes(p), "nobody")
            + Bad(p => p == "events/c01.json" ? ReadRes(p).Replace("set_flag", "teleport") : ReadRes(p), "teleport")
            + Bad(p => p == "events/c01.json" ? ReadRes(p).Replace("{player_name}", "{bogus}") : ReadRes(p), "bogus");
        Check("参照切れ（種族・ダンジョン・話者・op・差し込み）を検出する", miss == "", miss == "" ? "6種" : miss);

        var s = new StoryState
        {
            CurrentChart = "c07", Protagonist = "024", Partner = "031", ProtagonistIntrovertion = 80, ChoicePath = "tsugu",
            PostClear = true, PlayerName = "ゆうひ", SelectionSeed = 123456789012L, MbtiType = "INFJ",
        };
        foreach (var c in new[] { "c01", "c02" }) s.CompletedCharts.Add(c);
        s.Flags.Add("a"); s.Flags.Add("b"); s.Counters["x"] = 3; s.Counters["y"] = -1;
        s.ClearedDungeons.Add("d01"); s.UnlockedDungeons.Add("d02"); s.ResidentsArrived.Add("res_001");
        s.FiredEvents.Add("e1"); s.SkippedSupplements.Add("c32"); s.MemoryCollected.Add("c31");
        var s2 = StoryState.FromJson(s.ToJson());
        Check("StoryState→JSON→StoryState で全フィールド一致", s.Fingerprint() == s2.Fingerprint(), "18フィールド");

        string tmp = Path.Combine(Path.GetTempPath(), "story_test_" + Guid.NewGuid().ToString("N") + ".json");
        var hub = new HubUpgradeManager();
        hub.ImportState(new SaveData
        {
            Story = s,
            Hub = { FacilityLevels = { ["pal_workbench"] = 3 }, MaterialBank = { ["wood"] = 12 } },
            Party = { Protagonist = "024", Partner = "031", Roster = { "001", "002", "004" }, Active = { "001", "002" } },
            Inventory = { new InventoryEntry { Id = "potion", N = 2 } },
        });
        string before = SaveService.Serialize(hub.ExportState());
        SaveService.Save(hub.ExportState(), tmp);
        var loaded = SaveService.Load(tmp);
        var hub2 = new HubUpgradeManager();
        if (loaded.Status == LoadStatus.Ok) hub2.ImportState(loaded.Data);
        string after = SaveService.Serialize(hub2.ExportState());
        Check("セーブ→ロードで拠点・編成・所持品・物語が一致", loaded.Status == LoadStatus.Ok && before == after
              && hub2.PartyManager.Partner == "031" && hub2.GetFacilityLevel("pal_workbench") == 3
              && hub2.Story.Fingerprint() == s.Fingerprint(), $"status={loaded.Status}");
        File.WriteAllText(tmp, before.Replace("\"version\": 1", "\"version\": 2"));
        var inc = SaveService.Load(tmp);
        File.WriteAllText(tmp, "{ not json");
        var bad = SaveService.Load(tmp);
        File.Delete(tmp);
        Check("version違い・破損セーブは読まない / ファイル無しはNoFile",
              inc.Status == LoadStatus.Incompatible && bad.Status == LoadStatus.Corrupt
              && SaveService.Load(tmp).Status == LoadStatus.NoFile, $"{inc.Status}/{bad.Status}");
        hub.Free(); hub2.Free();

        // ---- P1: イベント基盤 ----
        string evJson = """
        [
         {"id":"t_once","trigger":{"type":"hub_enter","once":true},"steps":[{"op":"inc_counter","counter":"once_hits"}]},
         {"id":"t_req","trigger":{"type":"hub_enter","once":false,"requires":["gate"]},"steps":[{"op":"inc_counter","counter":"req_hits"}]},
         {"id":"t_floor","trigger":{"type":"dungeon_floor","dungeon":"d01_c1","floor":3},"steps":[{"op":"set_flag","flag":"floor3"}]},
         {"id":"t_days","trigger":{"type":"time_passed","days":5},"steps":[{"op":"set_flag","flag":"day5"}]},
         {"id":"t_clear","trigger":{"type":"dungeon_clear","dungeon":"d01_c1"},"steps":[{"op":"set_flag","flag":"cleared"}]},
         {"id":"t_cs","trigger":{"type":"chart_start","chart":"c01"},"steps":[{"op":"set_flag","flag":"cs"}]},
         {"id":"t_if","steps":[{"op":"if","cond":{"flag":"yes"},"then":[{"op":"set_flag","flag":"took_then"}],"else":[{"op":"set_flag","flag":"took_else"}]},
                              {"op":"if","cond":{"all":[{"counter":"n","gte":2},{"not_flag":"no"}]},"then":[{"op":"set_flag","flag":"all_ok"}]}]},
         {"id":"t_say","steps":[
            {"op":"say","who":"protagonist","text":"{I}は{catch}、{player_name}、{protagonist}と{partner}","by_type":{"ISTP":"おれは行く"}},
            {"op":"say","who":"partner","text":"{I}ね","by_type":{"ENFJ":"みんなで行こう！"}},
            {"op":"say","who":"res_001","text":"{name:res_022}さん。{I}です。"},
            {"op":"say","who":"protagonist","text":"フォールバック{I}","by_type":{"INFJ":"別"}},
            {"op":"narration","text":"{I}の内心"}]},
         {"id":"t_nofb","steps":[{"op":"say","who":"protagonist","by_type":{"INFJ":"別タイプのみ"},"text":"ok"}]},
         {"id":"t_choice","steps":[{"op":"choice","options":[{"text":"A","set_flag":"fa"},{"text":"B","set_flag":"fb","steps":[{"op":"set_flag","flag":"fb_steps"}]}]}]},
         {"id":"t_name","steps":[{"op":"name_input","max":4}]},
         {"id":"t_loop","steps":[{"op":"call_event","id":"t_loop"}]},
         {"id":"t_call","steps":[{"op":"call_event","id":"t_callee"},{"op":"give_item","id":"potion","n":2},{"op":"join_resident","id":"res_001"},{"op":"unlock_dungeon","id":"d01_c1"},{"op":"goto_chart","id":"c01"}]},
         {"id":"t_callee","steps":[{"op":"inc_counter","counter":"callee","n":5}]},
         {"id":"t_wait","steps":[{"op":"wait","sec":1.0,"introvert":true}]}
        ]
        """;
        Func<string, string> rdr = p => p == "events/t.json" ? evJson : ReadRes(p);
        var tdb = Db(rdr, out var terrs, EventFiles.Append("t.json"));
        Check("テスト用イベント定義が検証を通る", terrs.Count == 0, terrs.Count == 0 ? $"{tdb.Events.Count}件" : terrs[0]);

        var st = HeroState(); var host = new RecHost(); var it = It(tdb, st, host);
        var f1 = it.Fire("hub_enter").GetAwaiter().GetResult();
        var f2 = it.Fire("hub_enter").GetAwaiter().GetResult();
        Check("同じイベントを2回発火すると2回目は何も起こらない（once）",
              f1.Contains("t_once") && !f2.Contains("t_once") && st.GetCounter("once_hits") == 1, $"1回目[{string.Join(",", f1)}] 2回目[{string.Join(",", f2)}]");
        Check("requires のフラグが無いと発火しない / 立てば once:false は毎回発火",
              st.GetCounter("req_hits") == 0 && Fire3(it, st), $"req_hits={st.GetCounter("req_hits")}");
        it.Fire("dungeon_floor", new(Dungeon: "d01_c1", Floor: 2)).GetAwaiter().GetResult();
        bool floorMiss = !st.HasFlag("floor3");
        it.Fire("dungeon_floor", new(Dungeon: "d01_c1", Floor: 3)).GetAwaiter().GetResult();
        it.Fire("time_passed", new(Days: 4)).GetAwaiter().GetResult();
        bool dayMiss = !st.HasFlag("day5");
        it.Fire("time_passed", new(Days: 5)).GetAwaiter().GetResult();
        it.Fire("chart_start", new(Chart: "c02")).GetAwaiter().GetResult();
        bool csMiss = !st.HasFlag("cs");
        it.Fire("chart_start", new(Chart: "c01")).GetAwaiter().GetResult();
        it.Fire("dungeon_clear", new(Dungeon: "d01_c1")).GetAwaiter().GetResult();
        Check("トリガー種（階到達・日数・チャート開始・踏破）が条件どおり発火",
              floorMiss && dayMiss && csMiss && st.HasFlag("floor3") && st.HasFlag("day5") && st.HasFlag("cs") && st.HasFlag("cleared"),
              $"floor{floorMiss}/day{dayMiss}/cs{csMiss}");

        var st2 = HeroState(); st2.SetFlag("yes"); st2.Counters["n"] = 2;
        It(tdb, st2, new RecHost()).RunEvent("t_if").GetAwaiter().GetResult();
        var st3 = HeroState(); st3.SetFlag("no"); st3.Counters["n"] = 2;
        It(tdb, st3, new RecHost()).RunEvent("t_if").GetAwaiter().GetResult();
        Check("if 分岐がフラグ・カウンタどおりに選ばれる",
              st2.HasFlag("took_then") && !st2.HasFlag("took_else") && st2.HasFlag("all_ok")
              && st3.HasFlag("took_else") && !st3.HasFlag("took_then") && !st3.HasFlag("all_ok"), "then/else/all");

        var sayHost = new RecHost(); var sst = HeroState();
        It(tdb, sst, sayHost).RunEvent("t_say").GetAwaiter().GetResult();
        var L = sayHost.Lines;
        Check("say の by_type / text フォールバック / {I}{catch}{player_name}{protagonist}{partner}{name:id} 展開",
              L[0] == "ミナト:おれは行く" && L[1] == "クルリス:みんなで行こう！"
              && L[2] == "モコロン:ネムラムさん。わたしです。"
              && L[3] == "ミナト:フォールバックおれ" && L[4] == "N:おれの内心", string.Join(" | ", L));
        var fbHost = new RecHost();
        It(tdb, HeroState(), fbHost).RunEvent("t_nofb").GetAwaiter().GetResult();
        Check("by_type に該当タイプが無ければ text にフォールバックする", fbHost.Lines.SequenceEqual(new[] { "ミナト:ok" }), string.Join("|", fbHost.Lines));
        var nofbDb = Db(p => p == "events/t.json" ? """[{"id":"x","steps":[{"op":"say","who":"protagonist","by_type":{"INFJ":"a"}}]}]""" : ReadRes(p), out var nofbErrs, new[] { "c01.json", "c02.json", "t.json" });
        Check("by_type が16タイプ揃わず text も無い say を検証が弾く", nofbErrs.Any(x => x.Contains("16タイプ")), nofbErrs.FirstOrDefault() ?? "検出なし");

        var ch = new RecHost { ChoiceIndex = 1 }; var cst = HeroState();
        It(tdb, cst, ch).RunEvent("t_choice").GetAwaiter().GetResult();
        Check("choice の選択肢が set_flag と steps を実行する", cst.HasFlag("fb") && cst.HasFlag("fb_steps") && !cst.HasFlag("fa"), "B選択");

        var nst = HeroState(); It(tdb, nst, new RecHost { NameToGive = "あいうえおかきく" }).RunEvent("t_name").GetAwaiter().GetResult();
        var nst2 = HeroState(); It(tdb, nst2, new RecHost { NameToGive = "  " }).RunEvent("t_name").GetAwaiter().GetResult();
        Check("name_input は最大長で切り、空なら種族名を既定にして PlayerName に保存",
              nst.PlayerName == "あいうえ" && nst2.PlayerName == "チルテト", $"{nst.PlayerName}/{nst2.PlayerName}");

        bool loopThrew = false;
        try { It(tdb, HeroState(), new RecHost()).RunEvent("t_loop").GetAwaiter().GetResult(); } catch (StoryException) { loopThrew = true; }
        var cst2 = HeroState(); var gi = new List<string>();
        var it2 = It(tdb, cst2, new RecHost()); it2.Callbacks.GiveItem = (id, n) => gi.Add($"{id}x{n}");
        it2.RunEvent("t_call").GetAwaiter().GetResult();
        Check("call_event・give_item・join_resident・unlock_dungeon・goto_chart と、call_event の無限再帰を止める",
              loopThrew && cst2.GetCounter("callee") == 5 && gi.SequenceEqual(new[] { "potionx2" }) && cst2.ResidentsArrived.Contains("res_001")
              && cst2.UnlockedDungeons.Contains("d01_c1") && cst2.CurrentChart == "c01", $"throw={loopThrew}");

        var real = HeroState(); var realHost = new RecHost();
        It(db, real, realHost).Fire("chart_start", new(Chart: "c01")).GetAwaiter().GetResult();
        Check("チャート1の水面イベントが {protagonist}・名前入力・{player_name} を通して流れる",
              real.PlayerName == "テスト名" && real.HasFlag("c01_surface_done") && realHost.Lines.Any(l => l.Contains("チルテト。それが、おれの姿"))
              && realHost.Lines.Any(l => l.Contains("テスト名！よかった")), string.Join(" | ", realHost.Lines));

        // ---- P2: 振り分け ----
        var expect = new Dictionary<string, string>
        {
            ["INFJ"] = "024", ["INFP"] = "022", ["INTJ"] = "029B", ["INTP"] = "031", ["ISFJ"] = "001", ["ISFP"] = "016",
            ["ISTJ"] = "013", ["ISTP"] = "027B", ["ENFJ"] = "004", ["ENFP"] = "015", ["ENTJ"] = "027", ["ENTP"] = "042",
            ["ESFJ"] = "023", ["ESFP"] = "005", ["ESTJ"] = "002", ["ESTP"] = "011",
        };
        int typeBad = 0, combos = 0;
        foreach (var a in new[] { "E", "I" }) foreach (var b in new[] { "S", "N" }) foreach (var c in new[] { "T", "F" }) foreach (var d in new[] { "J", "P" })
        {
            combos++;
            var cs = new StoryState();
            SelectionLogic.ApplyAnswers(cs, db, new[] { a, b, c, d }, 1);
            if (!expect.TryGetValue(a + b + c + d, out var sp) || cs.Protagonist != sp || cs.MbtiType != a + b + c + d) typeBad++;
        }
        var qs = db.Selection.Questions;
        bool qOk = qs.Count == 4 && qs.Select(q => q.Axis).SequenceEqual(new[] { "EI", "SN", "TF", "JP" })
            && qs.All(q => q.Options.Count == 2 && q.Options[0].Letter == q.Axis[0].ToString() && q.Options[1].Letter == q.Axis[1].ToString());
        Check("16タイプすべてで主人公種族が台本の表どおり / 4問が E/I・S/N・T/F・J/P の順", typeBad == 0 && combos == 16 && qOk, $"不一致{typeBad}/16 問{qOk}");

        Func<string, string> prim = id => SpeciesDatabase.Instance.Require(id).Types[0].ToString();
        int trials = 0, fail = 0, nondet = 0; string firstFail = "";
        foreach (var (type, hero) in expect)
            for (long seed = 0; seed < 600; seed++)
            {
                trials++;
                var cand = SelectionLogic.CompanionCandidates(db, hero, prim, seed * 7919 + 13);
                var again = SelectionLogic.CompanionCandidates(db, hero, prim, seed * 7919 + 13);
                if (!cand.SequenceEqual(again)) nondet++;
                var types = cand.Select(prim).ToList();
                bool ok = cand.Count == 3 && cand.Distinct().Count() == 3 && !cand.Contains(hero)
                    && types.Distinct().Count() == 3 && !types.Contains(prim(hero))
                    && cand.All(x => db.Selection.TypeToSpecies.Values.Contains(x));
                if (!ok && fail++ == 0) firstFail = $"{type}/{hero} seed{seed}: {string.Join(",", cand)}";
            }
        Check("相棒候補3体の属性が互いに異なり主人公とも異なる（16タイプ×600シード総当たり）", fail == 0 && nondet == 0,
              $"{trials}通り 失敗{fail} 非決定{nondet} {firstFail}");
        var un = SelectionLogic.UnchosenResidents(db, "024", "031");
        Check("選ばれなかったプレイアブルが14体", un.Count == 14 && !un.Contains("024") && !un.Contains("031"), $"{un.Count}体");

        var pm = new PartyManager();
        bool emptyOk = pm.AllMemberSpeciesIds().Count == 0;
        pm.SetStartingParty("024", "031"); pm.AddToRoster("002");
        var all = pm.AllMemberSpeciesIds();
        RunDungeonAndFlow(sceneHost, db);

        Check("PartyManager の固定相棒(001)を廃止し、選んだ相棒が先頭", emptyOk && all[0] == "031" && all.Count == 2 && !all.Contains("001")
              && pm.Protagonist == "024", string.Join(",", all));
    }

    private static bool Fire3(EventInterpreter it, StoryState st)
    {
        st.SetFlag("gate");
        it.Fire("hub_enter").GetAwaiter().GetResult();
        it.Fire("hub_enter").GetAwaiter().GetResult();
        return st.GetCounter("req_hits") == 2;
    }
}
