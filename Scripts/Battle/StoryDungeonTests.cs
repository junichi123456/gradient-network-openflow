using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MysteryDungeon.Core;
using MysteryDungeon.Dungeon;
using MysteryDungeon.Entities;
using MysteryDungeon.Hub;
using MysteryDungeon.Species;
using MysteryDungeon.Story;

namespace MysteryDungeon.Battle;

// P3/P4 のヘッドレス検証: チャート進行（StoryFlow）と、35本＋根もとのダンジョン生成。
public static partial class StoryTests
{
    private static DungeonScene Enter(Node host, HubUpgradeManager hub, StoryDatabase db, DungeonDef d, int startFloor, StoryState st = null)
    {
        var dest = DungeonDestinations.From(db, d, st);
        dest.Config.StartFloor = startFloor;
        hub.PendingDungeonConfig = dest.Config;
        hub.PendingDungeonId = dest.DungeonRuleId;
        var scene = GD.Load<PackedScene>("res://Scenes/DungeonScene.tscn").Instantiate<DungeonScene>();
        host.AddChild(scene);
        return scene;
    }

    private static FloorController FloorOf(DungeonScene s) => s.GetNode<FloorController>(s.FloorControllerPath);

    private static void Leave(Node host, Node scene) { host.RemoveChild(scene); scene.Free(); }

    private static void RunDungeonAndFlow(Node host, StoryDatabase db)
    {
        void Check(string label, bool ok, string detail) => GD.Print($"[検証] {label}: {(ok ? "OK" : "NG")} ({detail})");
        var hub = HubUpgradeManager.Instance;

        // ---- P3/P4: データの形 ----
        var main35 = db.Dungeons.Where(d => !d.Hidden && !d.PostClear && db.Chart(d.Chart).Kind is "main" or "supplement").ToList();
        var extra = db.Dungeons.Where(d => d.PostClear).ToList();
        Check("ダンジョンは35本＋隠し1（根もと）＋追加枠8、全チャートは44",
              main35.Count == 35 && extra.Count == 8 && db.Dungeon("x_root_base")?.Hidden == true && db.Charts.Count == 44
              && db.Dungeon("x_root_base").EndType == "StoryBoss" && db.Dungeon("x_root_base").Boss.DefeatOk && db.Dungeon("d02_c2").Boss.DefeatOk,
              $"main={main35.Count} extra={extra.Count}");
        var cv = db.LevelCurve;
        Check("レベル曲線: チャート1=Lv5・チャート20=Lv60・単調増加、ボスのレベルは曲線どおり",
              cv.At(1) == 5 && cv.At(20) == 60 && Enumerable.Range(1, 19).All(c => cv.At(c) <= cv.At(c + 1))
              && main35.All(d => d.Boss.Level == db.BossLevelFor(d)), $"c2={cv.At(2)} c10={cv.At(10)}");

        // ---- P3: チャート進行（StoryFlow） ----
        var st = HeroState(); var h = new RecHost { ChoiceIndex = 1 };
        var flow = new StoryFlow(db, st, It(db, st, h));
        flow.EnterHub().GetAwaiter().GetResult();
        bool c1Started = st.CurrentChart == "c01" && st.UnlockedDungeons.Contains("d01_c1") && !flow.AvailableDungeons().Any(d => d.Id == "d02_c2")
            && flow.AvailableDungeons().Select(d => d.Id).SequenceEqual(new[] { "d01_c1" });
        flow.OnDungeonResult(new DungeonResult { DungeonId = "d01_c1" }).GetAwaiter().GetResult();
        Check("チャート1: 開始→ビーチのどうくつをクリア→完了→補完31を提示（スキップ）→チャート2が開始可能",
              c1Started && st.CompletedCharts.Contains("c01") && st.HasFlag("c01_done") && st.HasFlag("hub_stake")
              && st.SkippedSupplements.Contains("c31") && st.HasFlag("started_c02") && st.CurrentChart == "c02"
              && st.UnlockedDungeons.Contains("d02_c2") && st.ResidentsArrived.Count == 5 && !st.UnlockedDungeons.Contains("d31_s31"),
              $"chart={st.CurrentChart} skipped=[{string.Join(",", st.SkippedSupplements)}] res={st.ResidentsArrived.Count}");
        Check("チャート1の本文: 木の実を渡す・名前入力・16タイプの引き行が通る",
              h.Lines.Any(l => l.Contains("テスト名！よかった")) && h.Lines.Any(l => l.Contains("ここは、ハコニワ") || l.Contains("ハコニワ"))
              && h.Lines.Count(l => l.StartsWith("N:") ) > 10, $"{h.Lines.Count}行");

        var st2 = HeroState(); var h2 = new RecHost { ChoiceIndex = 0 };
        var f2 = new StoryFlow(db, st2, It(db, st2, h2));
        f2.EnterHub().GetAwaiter().GetResult();
        f2.OnDungeonResult(new DungeonResult { DungeonId = "d01_c1" }).GetAwaiter().GetResult();
        Check("補完31を受けると浜の岩棚が解放され、チャート2も始まる（補完は本編を止めない）",
              st2.UnlockedDungeons.Contains("d31_s31") && st2.HasFlag("started_c31") && st2.CurrentChart == "c02" && !st2.SkippedSupplements.Contains("c31"), "");

        flow.OnDungeonResult(new DungeonResult { DungeonId = "d02_c2", Defeated = true }).GetAwaiter().GetResult();
        Check("ヘルガルダ戦で全滅しても進行フラグが立ち、チャート2が完了してチャート3が始まる",
              st.ClearedDungeons.Contains("d02_c2") && st.HasFlag("c02_done") && st.HasFlag("lost_d02_c2") && st.CompletedCharts.Contains("c02")
              && st.HasFlag("hub_workbench") && st.CurrentChart == "c03" && st.UnlockedDungeons.Contains("d03_c3") && st.ResidentsArrived.Contains("bushigaeru"),
              $"chart={st.CurrentChart}");

        // ---- 全チャート完走（本文が空のチャートも含む）→ エンディング → 追加枠 ----
        var st3 = HeroState(); var h3 = new RecHost { ChoiceIndex = 1 };
        var f3 = new StoryFlow(db, st3, It(db, st3, h3));
        f3.EnterHub().GetAwaiter().GetResult();
        bool extrasClosed = !f3.AvailableDungeons().Any(d => d.PostClear);
        int guard = 0;
        while (!st3.PostClear && guard++ < 80)
        {
            var next = f3.AvailableDungeons().FirstOrDefault(d => !st3.ClearedDungeons.Contains(d.Id));
            if (next == null) break;
            f3.OnDungeonResult(new DungeonResult { DungeonId = next.Id }).GetAwaiter().GetResult();
            // シナリオ戦（start_scenario_fight）が要求されたら、その隠しダンジョンを負け扱いで進める。
            var fight = f3.TakePendingFight();
            if (fight != null) f3.OnDungeonResult(new DungeonResult { DungeonId = fight, Defeated = true }).GetAwaiter().GetResult();
        }
        bool mainDone = Enumerable.Range(1, 20).All(c => st3.CompletedCharts.Contains($"c{c:00}"));
        Check("チャート1〜20が連続して進み、20の完了で PostClear になる／それまで追加枠は開かない",
              extrasClosed && mainDone && st3.PostClear && guard < 80
              && Enumerable.Range(36, 7).All(n => f3.AvailableDungeons().Any(d => d.Id == $"x{n}")) && f3.AvailableDungeons().Any(d => d.Id == "x43")
              && !f3.AvailableDungeons().Any(d => d.Id == "x_root_base"), $"回数={guard} 完了={st3.CompletedCharts.Count} 現在={st3.CurrentChart} 候補=[{string.Join(",", f3.AvailableDungeons().Select(d => d.Id))}] done={st3.HasFlag("c14_done")} cleared={st3.ClearedDungeons.Count} root={st3.ClearedDungeons.Contains("x_root_base")} d21={st3.ClearedDungeons.Contains("d21_c14")}");

        // ---- スザク・影の森などのデータ: 35本の生成を実際に回す ----
        if (hub == null) { Check("HubUpgradeManager が使える", false, "autoload 無し"); return; }
        hub.PartyManager.SetStartingParty("027B", "004");
        FloorController.SuppressSceneChange = true;
        var rules = db.Dungeons.Where(d => !d.PostClear).ToList();   // 35本＋根もと
        int bad = 0; string first = "";
        void Bad(string m) { if (bad++ == 0) first = m; }
        int wildSeen = 0, wildBad = 0;
        foreach (var d in rules)
        {
            // 1) 最終階へ直行: 階数・終端・ボス種族・レベルがデータどおり
            var s = Enter(host, hub, db, d, d.Floors);
            var fc = FloorOf(s);
            var want = new List<string> { d.Boss.SpeciesId }; want.AddRange(d.Boss.Group.Select(g => g.SpeciesId));
            var got = fc.Bosses.Select(b => b.SpeciesId).ToList();
            if (fc.FloorNumber != d.Floors || !fc.OnFinalFloor || fc.Config.MaxFloors != d.Floors || fc.Config.EndType.ToString() != d.EndType)
                Bad($"{d.Id}: 階 {fc.FloorNumber}/{d.Floors} {fc.Config.EndType}");
            else if (!got.OrderBy(x => x).SequenceEqual(want.OrderBy(x => x))) Bad($"{d.Id}: ボス {string.Join(",", got)} != {string.Join(",", want)}");
            else if (fc.Bosses[0].Stats.Level != d.Boss.Level) Bad($"{d.Id}: Lv {fc.Bosses[0].Stats.Level} != {d.Boss.Level}");
            Leave(host, s);

            // 2) 1階（階が1つだけのダンジョンは除く）: 野生は出現表の中だけ。野生で出ない種族は出ない
            if (d.Floors < 2) continue;
            var pool = db.WildPool(d.Id);
            if (pool.Count == 0) { Bad($"{d.Id}: 出現表が空"); continue; }
            s = Enter(host, hub, db, d, 1);
            foreach (var e in FloorOf(s).SpawnedEnemyList)
            {
                wildSeen++;
                if (e is not WildEntity || !pool.Contains(e.SpeciesId)
                    || db.PalPlan.Any(r => r.SpeciesId == e.SpeciesId && !r.Wild)) { wildBad++; Bad($"{d.Id}: 野生 {e.SpeciesId} が出現表外"); }
            }
            Leave(host, s);
        }
        Check("36本（35＋根もと）すべてで 生成→最終階到達→ボス種族・階数・レベルがデータどおり／野生は出現表の中だけ",
              bad == 0 && wildSeen > 0, $"本数={rules.Count} 野生={wildSeen} 不一致{bad} {first}");

        // ---- 終端の挙動 ----
        var d1 = db.Dungeon("d01_c1");
        var sc = Enter(host, hub, db, d1, d1.Floors); var fl = FloorOf(sc);
        bool protagonistOk = sc.GetNode<Player>(sc.PlayerPath).SpeciesId == "027B";
        foreach (var b in fl.Bosses.ToList()) b.Die();
        fl.RunTurnEndChecks();
        bool winOk = fl.IsCleared && !fl.DefeatedByBoss && hub.PendingResult is { DungeonId: "d01_c1", Defeated: false };
        hub.PendingResult = null; Leave(host, sc);

        sc = Enter(host, hub, db, d1, d1.Floors); fl = FloorOf(sc);
        sc.GetNode<Player>(sc.PlayerPath).Stats.TakeDamage(9999);
        fl.ResolvePlayerDefeat();
        bool loseNormal = !fl.IsCleared && hub.PendingResult == null;   // 通常ボスで負ければ GAME OVER（進行しない）
        Leave(host, sc);

        var d2 = db.Dungeon("d02_c2");
        sc = Enter(host, hub, db, d2, d2.Floors); fl = FloorOf(sc);
        sc.GetNode<Player>(sc.PlayerPath).Stats.TakeDamage(9999);
        fl.ResolvePlayerDefeat();
        bool loseOk = fl.IsCleared && fl.DefeatedByBoss && hub.PendingResult is { DungeonId: "d02_c2", Defeated: true };
        var pr = hub.PendingResult; hub.PendingResult = null; Leave(host, sc);
        var st4 = HeroState(); var f4 = new StoryFlow(db, st4, It(db, st4, new RecHost()));
        f4.EnterHub().GetAwaiter().GetResult();
        f4.OnDungeonResult(new DungeonResult { DungeonId = "d01_c1" }).GetAwaiter().GetResult();
        f4.OnDungeonResult(pr).GetAwaiter().GetResult();
        Check("終端: ボス撃破でクリア結果が渡る／通常ボスの全滅は進行しない／ヘルガルダ戦の全滅は defeat_ok で進行フラグが立つ／主人公の種族がダンジョンに出る",
              winOk && loseNormal && loseOk && protagonistOk && st4.HasFlag("c02_done") && st4.HasFlag("lost_d02_c2"),
              $"win={winOk} loseNormal={loseNormal} loseOk={loseOk} hero={protagonistOk}");

        // 初期レベル5: ストーリー開始時の主人公と相棒は Lv5、Lv5 の時点で技が0個にならない
        sc = Enter(host, hub, db, d1, 1);
        var pl = sc.GetNode<Player>(sc.PlayerPath); var allyL = FloorOf(sc).Allies.FirstOrDefault();
        var mm = new MoveManager(); int learnedAt5 = MoveKit.LearnFromLearnset(mm, "027B", 5);
        Check("初期レベル: ストーリー開始時の主人公・相棒は Lv5／Lv5 で習得技が1個以上",
              pl.Stats.Level == 5 && allyL != null && allyL.Stats.Level == 5 && learnedAt5 > 0 && pl.Moves.Slots.Count > 0,
              $"player={pl.Stats.Level} ally={allyL?.Stats.Level} learned={learnedAt5}");
        Leave(host, sc);

        // FreeDungeonNoBossFinalFloor: 最終階にポータルが置かれ、踏むとクリア
        var cfg = new DungeonConfig { MaxFloors = 2, EndType = DungeonEndType.FreeDungeonNoBossFinalFloor, StartFloor = 2 };
        hub.PendingDungeonConfig = cfg; hub.PendingDungeonId = "d01_c1";
        sc = GD.Load<PackedScene>("res://Scenes/DungeonScene.tscn").Instantiate<DungeonScene>(); host.AddChild(sc); fl = FloorOf(sc);
        bool hasPortal = fl.PortalPosition != null && fl.Bosses.Count == 0;
        if (hasPortal) { sc.GetNode<Player>(sc.PlayerPath).PlaceAt(fl.PortalPosition.Value); fl.RunTurnEndChecks(); }
        Check("FreeDungeonNoBossFinalFloor: 最終階に脱出ポータルがあり、踏むとクリアする", hasPortal && fl.IsCleared && !fl.DefeatedByBoss, $"portal={hasPortal} cleared={fl.IsCleared}");
        hub.PendingResult = null; Leave(host, sc);

        RunScenarioCodeTests(host, hub);

        FloorController.SuppressSceneChange = false;
        hub.PartyManager.SetStartingParty("", "");
    }

    // 選択肢を順番に返すホスト（既定は末尾の選択肢）。
    private sealed class ScriptHost : IStoryHost
    {
        public readonly Queue<int> Picks = new();
        public readonly List<string> Lines = new();
        public readonly List<string> Menus = new();
        public Task Say(SayLine l) { Lines.Add($"{l.DisplayName}:{l.Text}"); return Task.CompletedTask; }
        public Task Narration(string t) { Lines.Add("N:" + t); return Task.CompletedTask; }
        public Task<int> Choice(IReadOnlyList<string> o) { Menus.Add(string.Join("/", o)); return Task.FromResult(Picks.Count > 0 ? Picks.Dequeue() : o.Count - 1); }
        public Task<string> NameInput(string d, int m) => Task.FromResult(d);
        public Task Fade(string m, double s) => Task.CompletedTask;
        public Task Wait(double s) => Task.CompletedTask;
    }

    private static StoryFlow FlowOf(StoryDatabase db, StoryState st, IStoryHost h)
    {
        var it = new EventInterpreter(db, st, h) { SpeciesName = id => SpeciesDatabase.Instance.Get(id)?.DisplayName ?? id };
        return new StoryFlow(db, st, it) { SpeciesName = it.SpeciesName };
    }

    private static void Kill(FloorController fc, int n) { foreach (var b in fc.Bosses.Take(n).ToList()) b.Die(); fc.RunTurnEndChecks(); }

    // P5・P7・P8・P9 とその周辺（ダンジョン順・階到達・ChoicePath）。全イベントを含む DB で回す。
    private static void RunScenarioCodeTests(Node host, HubUpgradeManager hub)
    {
        void Check(string label, bool ok, string detail) => GD.Print($"[検証] {label}: {(ok ? "OK" : "NG")} ({detail})");
        var files = DirAccess.GetFilesAt("res://Data/story/events").Where(f => f.EndsWith(".json")).ToList();
        var db = Db(ReadRes, out var errs, files);
        Check("全イベントを含む StoryDatabase が参照切れ0で読める（requires_cleared・post_battle の参照を含む）", errs.Count == 0,
              errs.Count == 0 ? $"event{db.Events.Count}" : $"{errs.Count}件 {errs[0]}");

        // ---- (c) ダンジョン順 requires_cleared ----
        var pairs = new[] { ("d17_c12", "d16_c12"), ("d19_c13", "d18_c13"), ("d21_c14", "d20_c14"), ("d23_c15", "d22_c15"), ("d25_c16", "d24_c16"), ("d29_c19", "d28_c19") };
        var stO = HeroState();
        foreach (var (a, b) in pairs) { stO.UnlockedDungeons.Add(a); stO.UnlockedDungeons.Add(b); }
        var fo = FlowOf(db, stO, new RecHost());
        bool closed = pairs.All(p => !fo.AvailableDungeons().Any(d => d.Id == p.Item1) && fo.AvailableDungeons().Any(d => d.Id == p.Item2));
        foreach (var (a, b) in pairs) stO.ClearedDungeons.Add(b);
        bool opened = pairs.All(p => fo.AvailableDungeons().Any(d => d.Id == p.Item1));
        var bogus = Db(p => p == "dungeons.json" ? ReadRes(p).Replace("\"requires_cleared\": [\n      \"d16_c12\"", "\"requires_cleared\": [\n      \"nope\"") : ReadRes(p), out var bogusErrs, files);
        Check("requires_cleared: 前提のダンジョンをクリアするまで d17/d19/d21/d23/d25/d29 は選べない／クリアで開く／不正な参照は検証が弾く",
              closed && opened && bogusErrs.Any(x => x.Contains("requires_cleared")), $"closed={closed} opened={opened} err={bogusErrs.FirstOrDefault()}");

        // ---- (a) P5: 2体ボス・群れ戦の「全員撃破でクリア」 ----
        var d16 = db.Dungeon("d16_c12"); var d17 = db.Dungeon("d17_c12");
        var sc = Enter(host, hub, db, d16, d16.Floors); var fc = FloorOf(sc);
        bool two = fc.Bosses.Count == 2 && fc.Bosses.Select(x => x.SpeciesId).OrderBy(x => x).SequenceEqual(new[] { "195", "195B" });
        Kill(fc, 1); bool oneDown = !fc.IsCleared;
        Kill(fc, 2); bool bothDown = fc.IsCleared && hub.PendingResult is { DungeonId: "d16_c12", Defeated: false };
        hub.PendingResult = null; Leave(host, sc);
        Check("2体ボス（影の森: ベラノワール＋ベラルージュ）: 1体倒してもクリアせず、2体とも倒すとクリア", two && oneDown && bothDown, $"two={two} oneDown={oneDown} both={bothDown}");
        sc = Enter(host, hub, db, d17, d17.Floors); fc = FloorOf(sc);
        bool four = fc.Bosses.Count == 4 && fc.Bosses.Count(x => x.SpeciesId == "145") == 2 && fc.Bosses.Count(x => x.SpeciesId == "146") == 2;
        Kill(fc, 3); bool threeDown = !fc.IsCleared;
        Kill(fc, 4); bool allDown = fc.IsCleared && hub.PendingResult is { DungeonId: "d17_c12" };
        hub.PendingResult = null; Leave(host, sc);
        Check("群れ戦（ささやきの洞: ゼノベーダ2＋ゼノルーダ2）: 3体倒してもクリアせず、全員倒すとクリア", four && threeDown && allDown, $"four={four} three={threeDown} all={allDown}");

        // ---- (a) P5: 戦闘後共闘（スザク） ----
        var stC = HeroState(); var fC = FlowOf(db, stC, new RecHost());
        fC.OnDungeonResult(new DungeonResult { DungeonId = "d18_c13", Defeated = true }).GetAwaiter().GetResult();
        bool lostNo = stC.CoopSpecies == "";
        fC.OnDungeonResult(new DungeonResult { DungeonId = "d18_c13" }).GetAwaiter().GetResult();
        var cfg19 = DungeonDestinations.ConfigFor(db, db.Dungeon("d19_c13"), stC);
        bool setOk = stC.CoopSpecies == "122" && stC.CoopDungeon == "d19_c13" && cfg19.CoopGuests.SequenceEqual(new[] { "122" })
            && DungeonDestinations.ConfigFor(db, db.Dungeon("d20_c14"), stC).CoopGuests.Count == 0 && DungeonDestinations.ConfigFor(db, db.Dungeon("d19_c13")).CoopGuests.Count == 0;
        var d19 = db.Dungeon("d19_c13");
        sc = Enter(host, hub, db, d19, d19.Floors, stC); fc = FloorOf(sc);
        var ids = fc.Allies.Select(a => a.SpeciesId).ToList();
        Leave(host, sc);
        sc = Enter(host, hub, db, d19, d19.Floors); fc = FloorOf(sc);
        bool without = fc.Allies.Count == 1;
        Leave(host, sc);
        fC.OnDungeonResult(new DungeonResult { DungeonId = "d19_c13" }).GetAwaiter().GetResult();
        Check("戦闘後共闘: ボス撃破でスザクが次のダンジョン（d19）だけ同行し、d19 のクリアで離れる／敗北では加わらない",
              lostNo && setOk && ids.SequenceEqual(new[] { "004", "122" }) && without && stC.CoopSpecies == "" && stC.CoopDungeon == "",
              $"lostNo={lostNo} set={setOk} allies=[{string.Join(",", ids)}] without={without}");

        // ---- (b) dungeon_floor トリガー ----
        var seen = new List<(string, int)>();
        FloorController.FloorReachedHook = (d, f) => { seen.Add((d, f)); return Task.CompletedTask; };
        var x36 = db.Dungeon("x36");
        sc = Enter(host, hub, db, x36, 1); Leave(host, sc);
        sc = Enter(host, hub, db, d16, 3); Leave(host, sc);
        FloorController.FloorReachedHook = null;
        Check("階に着くと FloorController が (ダンジョンID, 階) で dungeon_floor を通知する", seen.SequenceEqual(new[] { ("x36", 1), ("d16_c12", 3) }), string.Join(",", seen));
        var stF = HeroState(); var hF = new RecHost(); var fF = FlowOf(db, stF, hF);
        bool pend = Enumerable.Range(36, 8).All(n => fF.FloorEventsPending($"x{n}", 1)) && !fF.FloorEventsPending("x36", 2) && !fF.FloorEventsPending("d16_c12", 1);
        foreach (var n in Enumerable.Range(36, 8)) fF.OnFloorReached($"x{n}", 1).GetAwaiter().GetResult();
        int linesAfter = hF.Lines.Count;
        fF.OnFloorReached("x36", 1).GetAwaiter().GetResult();
        Check("x36〜x43 の導入イベント（dungeon_floor・1階）が各1回だけ流れ、台詞が出る", pend && Enumerable.Range(36, 8).All(n => stF.FiredEvents.Contains($"x{n}_intro"))
              && hF.Lines.Any(l => l.Contains("根もとの伝説の間")) && hF.Lines.Count == linesAfter && !Enumerable.Range(36, 8).Any(n => fF.FloorEventsPending($"x{n}", 1)),
              $"{hF.Lines.Count}行");

        // ---- (g) ChoicePath ----
        string PathAfterC19(params string[] flags)
        {
            var s2 = HeroState(); foreach (var f in flags) s2.SetFlag(f);
            FlowOf(db, s2, new RecHost { ChoiceIndex = 0 }).CompleteChart(db.Chart("c19")).GetAwaiter().GetResult();
            return s2.ChoicePath;
        }
        Check("ChoicePath: c19 の完了で c19_path_tsugu（と c16/c17 の傾き）が 'tsugu' になる／何も無ければ空のまま",
              PathAfterC19("c19_path_tsugu") == "tsugu" && PathAfterC19("c16_lean_tsugu") == "tsugu" && PathAfterC19("c17_lean_tsugu") == "tsugu" && PathAfterC19() == "",
              "4通り");

        // ---- (d) P7: 補完の提示（挿入点）・スキップ・思い出 ----
        var offered = new List<string>();
        foreach (var (host_, sup) in new[] { ("c01", "c31"), ("c07", "c32"), ("c09", "c33"), ("c12", "c34") })
        {
            var s3 = HeroState(); var f3 = FlowOf(db, s3, new ScriptHost());
            f3.CompleteChart(db.Chart(host_)).GetAwaiter().GetResult();
            if (s3.HasFlag("offered_" + sup) && s3.SkippedSupplements.Contains(sup) && !s3.HasFlag("started_" + sup)) offered.Add(sup);
        }
        var s35 = HeroState(); var h35 = new ScriptHost(); var f35 = FlowOf(db, s35, h35);
        f35.It.Fire("dungeon_clear", new EventInterpreter.TriggerArgs(Dungeon: "d21_c14")).GetAwaiter().GetResult();   // c14_e04_root（c14_e04 の前方一致）
        bool c35 = s35.HasFlag("offered_c35") && s35.SkippedSupplements.Contains("c35") && !s35.HasFlag("started_c35") && s35.FiredEvents.Contains("c14_e04_root");
        var s35b = HeroState(); var h35b = new ScriptHost(); h35b.Picks.Enqueue(0); var f35b = FlowOf(db, s35b, h35b);
        f35b.It.Fire("dungeon_clear", new EventInterpreter.TriggerArgs(Dungeon: "d21_c14")).GetAwaiter().GetResult();
        Check("補完31〜35: 各挿入点（31=c01完了、32=c07完了、33=c09完了、34=c12完了、35=c14_e04 前方一致）で提示され、スキップできる／受ければ始まる",
              offered.SequenceEqual(new[] { "c31", "c32", "c33", "c34" }) && c35 && s35b.HasFlag("started_c35") && s35b.UnlockedDungeons.Contains("d35_s35") && !s35b.SkippedSupplements.Contains("c35"),
              $"offered=[{string.Join(",", offered)}] c35={c35}");

        var stM = HeroState(); stM.SkippedSupplements.UnionWith(new[] { "c35", "c31" }); stM.ResidentsArrived.Add("bushigaeru");
        var hM = new ScriptHost(); var fM = FlowOf(db, stM, hM);
        bool hiddenBefore = fM.MemoryEntries().Count == 0;
        fM.RunHubMenu().GetAwaiter().GetResult();
        bool noMemoryMenu = hM.Menus[0] == "住民と話す/閉じる";
        stM.PostClear = true;
        bool listed = fM.MemoryEntries().SequenceEqual(new[] { "c31", "c35" });
        hM.Menus.Clear(); hM.Lines.Clear();
        foreach (var pick in new[] { 0, 0, 1 }) hM.Picks.Enqueue(pick);   // 思い出 → c31 → 「そっとしておく」
        fM.RunHubMenu().GetAwaiter().GetResult();
        bool declined = !stM.MemoryCollected.Contains("c31") && fM.MemoryEntries().Count == 2 && !stM.HasFlag("memory_c31");
        hM.Menus.Clear(); hM.Lines.Clear();
        foreach (var pick in new[] { 0, 0, 0, 0, 1 }) hM.Picks.Enqueue(pick);   // 思い出 → c31 → 「思い出す」→ 思い出（c35 が残る）→ 戻る
        fM.RunHubMenu().GetAwaiter().GetResult();
        bool got = stM.MemoryCollected.SetEquals(new[] { "c31" }) && stM.HasFlag("memory_c31") && fM.MemoryEntries().SequenceEqual(new[] { "c35" })
            && stM.SkippedSupplements.Contains("c35") && hM.Lines.Any(l => l.Contains("思い出は"));
        bool again = !fM.RunMemory("c31").GetAwaiter().GetResult();
        Check("思い出: クリア前は出ない／クリア後にスキップした補完が並ぶ／「そっとしておく」では回収されず、「思い出す」で MemoryCollected に入る（残りは回収可能のまま）",
              hiddenBefore && noMemoryMenu && listed && declined && got && again, $"hidden={hiddenBefore} menu={noMemoryMenu} listed={listed} declined={declined} got={got}");

        // ---- 後日解放: スキップした補完ダンジョンをダンジョン選択に出し、入って完了すると回収済みになる ----
        var stDL = HeroState(); var hDL = new RecHost { ChoiceIndex = 1 }; var fDL = new StoryFlow(db, stDL, It(db, stDL, hDL));
        fDL.EnterHub().GetAwaiter().GetResult();
        fDL.OnDungeonResult(new DungeonResult { DungeonId = "d01_c1" }).GetAwaiter().GetResult();   // c31 をあとにする → c02
        bool skipListed = stDL.SkippedSupplements.Contains("c31") && fDL.AvailableDungeons().Any(d => d.Id == "d31_s31") && fDL.AvailableDungeons().Any(d => d.Id == "d02_c2")
            && !stDL.HasFlag("started_c31");
        var stDLj = StoryState.FromJson(stDL.ToJson());   // セーブ往復: スキップ状態が保たれ、ロード後も選べる
        var fLj = new StoryFlow(db, stDLj, It(db, stDLj, new RecHost()));
        bool saveRound = stDLj.SkippedSupplements.SetEquals(stDL.SkippedSupplements) && fLj.AvailableDungeons().Any(d => d.Id == "d31_s31");
        fDL.EnterSupplementDungeon("d31_s31").GetAwaiter().GetResult();
        bool started = stDL.HasFlag("started_c31") && stDL.UnlockedDungeons.Contains("d31_s31") && stDL.CurrentChart == "c02" && !fDL.IsSkippedSupplementDungeon(db.Dungeon("d31_s31"));
        fDL.OnDungeonResult(new DungeonResult { DungeonId = "d31_s31" }).GetAwaiter().GetResult();
        bool doneL = stDL.CompletedCharts.Contains("c31") && stDL.MemoryCollected.Contains("c31") && !stDL.SkippedSupplements.Contains("c31")
            && stDL.CurrentChart == "c02" && !stDL.CompletedCharts.Contains("c02");
        stDL.PostClear = true;
        bool noMem = !fDL.MemoryEntries().Contains("c31");
        var stLk = StoryState.FromJson(stDL.ToJson());
        bool collectedSaved = stLk.MemoryCollected.Contains("c31") && !stLk.SkippedSupplements.Contains("c31");
        Check("補完の後日解放: スキップ直後からダンジョン選択に出る／入ると始まる／完了で MemoryCollected に入り Skipped から外れ思い出に出ない／セーブ往復で保たれる",
              skipListed && saveRound && started && doneL && noMem && collectedSaved,
              $"list={skipListed} save={saveRound} started={started} done={doneL} noMem={noMem} saved={collectedSaved}");

        var s35L = HeroState(); s35L.SkippedSupplements.Add("c35");
        var f35L = new StoryFlow(db, s35L, It(db, s35L, new RecHost()));
        bool before14 = !f35L.AvailableDungeons().Any(d => d.Id == "d35_s35");
        s35L.CompletedCharts.Add("c14");
        bool after14 = f35L.AvailableDungeons().Any(d => d.Id == "d35_s35");
        f35L.EnterSupplementDungeon("d35_s35").GetAwaiter().GetResult();
        bool c14Intact = s35L.HasFlag("started_c35") && s35L.CurrentChart != "c35" && !s35L.CompletedCharts.Contains("c35");
        f35L.OnDungeonResult(new DungeonResult { DungeonId = "d35_s35" }).GetAwaiter().GetResult();
        Check("補完35: c14 完了前は出ず、完了後は入れる／本編の現在チャートを変えず、クリアで回収済み",
              before14 && after14 && c14Intact && s35L.MemoryCollected.Contains("c35") && !s35L.SkippedSupplements.Contains("c35"),
              $"before={before14} after={after14} intact={c14Intact}");

        var stA = HeroState(); var fA = new StoryFlow(db, stA, It(db, stA, new RecHost { ChoiceIndex = 0 }));
        fA.EnterHub().GetAwaiter().GetResult();
        fA.OnDungeonResult(new DungeonResult { DungeonId = "d01_c1" }).GetAwaiter().GetResult();
        fA.OnDungeonResult(new DungeonResult { DungeonId = "d31_s31" }).GetAwaiter().GetResult();
        Check("受けた補完は従来どおり進む（スキップ扱いにならず、完了しても MemoryCollected に入らない）",
              stA.CompletedCharts.Contains("c31") && !stA.SkippedSupplements.Contains("c31") && !stA.MemoryCollected.Contains("c31"), "");

        // ---- (e) P8: クリア後の追加枠と勧誘 ----
        var stP = HeroState(); stP.CompletedCharts.Add("c20"); foreach (var n in Enumerable.Range(36, 8)) stP.UnlockedDungeons.Add($"x{n}");
        var fP = FlowOf(db, stP, new RecHost());
        bool closedP = !fP.AvailableDungeons().Any(d => d.PostClear);
        stP.PostClear = true;
        bool openP = Enumerable.Range(36, 8).All(n => fP.AvailableDungeons().Any(d => d.Id == $"x{n}"));
        var nonwild = db.PalPlan.Where(r => r.Category == "nonwild").Select(r => r.SpeciesId).OrderBy(x => x).ToList();
        var x43 = db.Dungeon("x43");
        var want43 = new List<string> { x43.Boss.SpeciesId }; want43.AddRange(x43.Boss.Group.Select(g => g.SpeciesId));
        bool dataP = Enumerable.Range(36, 7).All(n => db.BossLevelFor(db.Dungeon($"x{n}")) == 70 && db.Dungeon($"x{n}").Boss.Level == 70)
            && db.BossLevelFor(x43) == 65 && want43.OrderBy(x => x).SequenceEqual(nonwild) && nonwild.Count == 9;
        Check("追加枠: PostClear 前は開かない／後は x36〜x43 が開く／伝説7体=Lv70・再挑戦回廊=野生で出ない9体 Lv65", closedP && openP && dataP, $"closed={closedP} open={openP} data={dataP}");

        sc = Enter(host, hub, db, x43, x43.Floors); fc = FloorOf(sc);
        bool nine = fc.Bosses.Count == 9 && fc.Bosses.All(b => b.Stats.Level == 65) && fc.Bosses.Select(b => b.SpeciesId).OrderBy(x => x).SequenceEqual(nonwild);
        Kill(fc, 8); bool eight = !fc.IsCleared; Kill(fc, 9); bool allNine = fc.IsCleared;
        hub.PendingResult = null; Leave(host, sc);
        Check("再挑戦回廊（x43）: 9体が Lv65 で同時に出て、全員倒すとクリア", nine && eight && allNine, $"nine={nine} eight={eight} all={allNine}");

        bool chance = new[] { "198", "200B", "203" }.All(x => fP.RecruitChance(x) == 30) && nonwild.All(x => fP.RecruitChance(x) == 20) && fP.RecruitChance("001") == 0;
        // 乱数は SelectionSeed 由来の固定列（SplitMix64）。シード14: 18,1,44,7,46,77,98,75,8 / シード20: 6,50,19,68,84,22,11,73,90 / シード1: 78,73,...
        int[] Seq(long seed, int n) { var s2 = new StoryState { SelectionSeed = seed }; var f2 = FlowOf(db, s2, new RecHost()); return Enumerable.Range(0, n).Select(_ => f2.NextRecruitRoll()).ToArray(); }
        bool seq = Seq(14, 9).SequenceEqual(new[] { 18, 1, 44, 7, 46, 77, 98, 75, 8 }) && Seq(20, 9).SequenceEqual(new[] { 6, 50, 19, 68, 84, 22, 11, 73, 90 })
            && Seq(1, 3).SequenceEqual(new[] { 78, 73, 53 });
        // 途中でセーブ→ロードしても続きが同じ
        var sR = new StoryState { SelectionSeed = 20 }; var fR = FlowOf(db, sR, new RecHost());
        var first = new[] { fR.NextRecruitRoll(), fR.NextRecruitRoll(), fR.NextRecruitRoll() };
        var sR2 = StoryState.FromJson(sR.ToJson()); var fR2 = FlowOf(db, sR2, new RecHost());
        bool resume = first.SequenceEqual(new[] { 6, 50, 19 }) && fR2.NextRecruitRoll() == 68 && sR2.RecruitRolls == 4;
        var big = new StoryState { SelectionSeed = 777 }; var fBig = FlowOf(db, big, new RecHost());
        int hits30 = 0, hits20 = 0; for (int i = 0; i < 10000; i++) { int r = fBig.NextRecruitRoll(); if (r < 30) hits30++; if (r < 20) hits20++; }
        Check("勧誘の確率: 伝説30%・ボスパル20%、乱数は固定シードの決定論的な列でセーブ→ロードしても続きが同じ（1万回で 30%±2 / 20%±2）",
              chance && seq && resume && Math.Abs(hits30 - 3000) < 200 && Math.Abs(hits20 - 2000) < 200, $"chance={chance} seq={seq} resume={resume} {hits30}/{hits20}");

        // 勝って勧誘（シード14: セイントールの1回目=18<30 で成功）。成功した種族はロスターに入り、住民として記録され、再戦で乱数を引かない
        var stW = HeroState(); stW.SelectionSeed = 14; var pmW = new PartyManager(); pmW.SetStartingParty("027B", "004");
        var fW = FlowOf(db, stW, new ScriptHost()); fW.OnRecruit = id => pmW.AddToRoster(id);
        fW.OnDungeonResult(new DungeonResult { DungeonId = "x36" }).GetAwaiter().GetResult();
        bool win = stW.RecruitedPals.SetEquals(new[] { "198" }) && pmW.RecruitedRoster.SequenceEqual(new[] { "198" }) && stW.RecruitRolls == 1 && pmW.ActiveParty.Contains("198");
        fW.OnDungeonResult(new DungeonResult { DungeonId = "x36" }).GetAwaiter().GetResult();
        bool noReroll = stW.RecruitRolls == 1 && pmW.RecruitedRoster.Count == 1;
        // 失敗（シード1: 78>=30）
        var stL = HeroState(); stL.SelectionSeed = 1; var fL = FlowOf(db, stL, new ScriptHost());
        fL.OnDungeonResult(new DungeonResult { DungeonId = "x36" }).GetAwaiter().GetResult();
        bool lose = stL.RecruitedPals.Count == 0 && stL.RecruitRolls == 1;
        // 再挑戦回廊: 9体を 192,145,146,189,191,195,195B,196,197 の順に20%で判定（シード20: 6,50,19,68,84,22,11,73,90 → 192・146・195B）
        var st9 = HeroState(); st9.SelectionSeed = 20; var got9 = new List<string>(); var f9 = FlowOf(db, st9, new ScriptHost()); f9.OnRecruit = got9.Add;
        f9.OnDungeonResult(new DungeonResult { DungeonId = "x43" }).GetAwaiter().GetResult();
        bool nineRec = got9.SequenceEqual(new[] { "192", "146", "195B" }) && st9.RecruitedPals.SetEquals(got9) && st9.RecruitRolls == 9;
        // 本編のボス（PostClear でない）・敗北では勧誘しない
        var stN = HeroState(); stN.SelectionSeed = 14; var fN = FlowOf(db, stN, new ScriptHost());
        fN.OnDungeonResult(new DungeonResult { DungeonId = "d16_c12" }).GetAwaiter().GetResult();
        fN.OnDungeonResult(new DungeonResult { DungeonId = "x37", Defeated = true }).GetAwaiter().GetResult();
        bool noMain = stN.RecruitRolls == 0 && stN.RecruitedPals.Count == 0;
        Check("勧誘（シード固定・厳密）: x36 は seed14 で成功（ロスター入り・再戦で乱数なし）、seed1 で失敗、x43 は seed20 で 192・146・195B、本編ボス・敗北は判定なし",
              win && noReroll && lose && nineRec && noMain, $"win={win} noReroll={noReroll} lose={lose} nine={nineRec}[{string.Join(",", got9)}] noMain={noMain}");

        var stS = HeroState(); stS.Days = 5; stS.Affinity = 7; stS.RecruitSeed = 99; stS.RecruitRolls = 3; stS.RecruitedPals.Add("198"); stS.CoopSpecies = "122"; stS.CoopDungeon = "d19_c13";
        var stS2 = StoryState.FromJson(stS.ToJson());
        Check("StoryState の新フィールド（日数・親密度・勧誘シード/回数・仲間・共闘）が JSON 往復で一致", stS.Fingerprint() == stS2.Fingerprint() && stS2.Days == 5 && stS2.RecruitedPals.Contains("198") && stS2.CoopDungeon == "d19_c13", "");

        // ---- (f) P9: 住民イベント c21〜c30 ----
        ChartDef Rc(int n) => db.Chart($"c{n}");
        var stU = HeroState(); var fU = FlowOf(db, stU, new RecHost());
        foreach (var c in new[] { "c05", "c08", "c09", "c11", "c12", "c14", "c20" }) stU.CompletedCharts.Add(c);
        bool u0 = !fU.ResidentUnlocked(Rc(21));
        stU.Days = 3; stU.Affinity = 9; bool u1 = !fU.ResidentUnlocked(Rc(21));
        stU.Days = 2; stU.Affinity = 10; bool u2 = !fU.ResidentUnlocked(Rc(21));
        stU.Days = 3; stU.Affinity = 10; bool u3 = fU.ResidentUnlocked(Rc(21)) && !fU.ResidentUnlocked(Rc(22));
        stU.Days = 29; stU.Affinity = 99; bool u4 = fU.ResidentUnlocked(Rc(29)) && !fU.ResidentUnlocked(Rc(30));
        stU.Days = 30; stU.Affinity = 100; bool u5 = Enumerable.Range(21, 10).All(n => fU.ResidentUnlocked(Rc(n)));
        var stV = HeroState(); stV.Days = 99; stV.Affinity = 999; bool u6 = Enumerable.Range(21, 10).All(n => !FlowOf(db, stV, new RecHost()).ResidentUnlocked(Rc(n)));
        Check("住民イベントの解放条件: 先行チャート＋日数＋親密度のすべてを満たしたときだけ開く（c21=3日/10, c29=27日/90, c30=30日/100）",
              u0 && u1 && u2 && u3 && u4 && u5 && u6, $"{u0}{u1}{u2}{u3}{u4}{u5}{u6}");

        var stE = HeroState(); var hE = new RecHost(); var fE = FlowOf(db, stE, hE);
        foreach (var n in Enumerable.Range(1, 20)) { stE.SetFlag($"started_c{n:00}"); stE.CompletedCharts.Add($"c{n:00}"); }
        stE.CurrentChart = "c20"; stE.Days = 3; stE.Affinity = 10;
        fE.EnterHub().GetAwaiter().GetResult();
        bool c21Once = stE.CompletedCharts.Contains("c21") && stE.HasFlag("c21_intro_seen") && !stE.CompletedCharts.Contains("c22") && hE.Lines.Any(l => l.Contains("七つの柱"));
        int lc = hE.Lines.Count; fE.EnterHub().GetAwaiter().GetResult();
        bool noRepeat = hE.Lines.Count == lc;
        stE.Days = 30; stE.Affinity = 100; fE.EnterHub().GetAwaiter().GetResult();
        bool allStubs = Enumerable.Range(21, 10).All(n => stE.CompletedCharts.Contains($"c{n}")
            && db.Events.Where(e => e.Id.StartsWith($"c{n}_") && e.Trigger == null).All(e => stE.FiredEvents.Contains(e.Id)));
        Check("住民イベントc21〜c30: 条件を満たした入場で1回ずつ流れ（c21→全て）、2回目の入場では繰り返さない", c21Once && noRepeat && allStubs, $"c21={c21Once} noRepeat={noRepeat} all={allStubs}");

        var stT = HeroState(); stT.ResidentsArrived.Add("bushigaeru"); var fT = FlowOf(db, stT, new RecHost());
        bool t1 = fT.TalkToResident("bushigaeru") && stT.Affinity == 1;
        bool t2 = !fT.TalkToResident("bushigaeru") && stT.Affinity == 1;      // 同じ日は+1まで
        bool t3 = !fT.TalkToResident("helgalda") && stT.Affinity == 1;        // まだ住民でない
        fT.OnDungeonResult(new DungeonResult { DungeonId = "d01_c1" }).GetAwaiter().GetResult();
        bool day = stT.Days == 1;
        bool t4 = fT.TalkToResident("bushigaeru") && stT.Affinity == 2;       // 日が進めばまた+1
        fT.OnDungeonResult(new DungeonResult { DungeonId = "d02_c2", Defeated = true }).GetAwaiter().GetResult();
        Check("日数はダンジョンから戻るたびに+1（敗北でも）、親密度は住民と話すと1日1人1回だけ+1", t1 && t2 && t3 && day && t4 && stT.Days == 2, $"days={stT.Days} aff={stT.Affinity}");
    }
}
