using System;
using System.Collections.Generic;
using System.Linq;
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
    private static DungeonScene Enter(Node host, HubUpgradeManager hub, StoryDatabase db, DungeonDef d, int startFloor)
    {
        var dest = DungeonDestinations.From(db, d);
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
        }
        bool mainDone = Enumerable.Range(1, 20).All(c => st3.CompletedCharts.Contains($"c{c:00}"));
        Check("チャート1〜20が連続して進み、20の完了で PostClear になる／それまで追加枠は開かない",
              extrasClosed && mainDone && st3.PostClear && guard < 80
              && Enumerable.Range(36, 7).All(n => f3.AvailableDungeons().Any(d => d.Id == $"x{n}")) && f3.AvailableDungeons().Any(d => d.Id == "x43")
              && !f3.AvailableDungeons().Any(d => d.Id == "x_root_base"), $"回数={guard} 完了={st3.CompletedCharts.Count}");

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

        // FreeDungeonNoBossFinalFloor: 最終階にポータルが置かれ、踏むとクリア
        var cfg = new DungeonConfig { MaxFloors = 2, EndType = DungeonEndType.FreeDungeonNoBossFinalFloor, StartFloor = 2 };
        hub.PendingDungeonConfig = cfg; hub.PendingDungeonId = "d01_c1";
        sc = GD.Load<PackedScene>("res://Scenes/DungeonScene.tscn").Instantiate<DungeonScene>(); host.AddChild(sc); fl = FloorOf(sc);
        bool hasPortal = fl.PortalPosition != null && fl.Bosses.Count == 0;
        if (hasPortal) { sc.GetNode<Player>(sc.PlayerPath).PlaceAt(fl.PortalPosition.Value); fl.RunTurnEndChecks(); }
        Check("FreeDungeonNoBossFinalFloor: 最終階に脱出ポータルがあり、踏むとクリアする", hasPortal && fl.IsCleared && !fl.DefeatedByBoss, $"portal={hasPortal} cleared={fl.IsCleared}");
        hub.PendingResult = null; Leave(host, sc);

        FloorController.SuppressSceneChange = false;
        hub.PartyManager.SetStartingParty("", "");
    }
}
