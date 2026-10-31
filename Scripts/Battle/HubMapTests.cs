using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Hub;
using MysteryDungeon.Species;
using MysteryDungeon.Story;

namespace MysteryDungeon.Battle;

// ハコニワ再設計（4画面・広場の施設・ポイントと売買）のヘッドレス検証。BattleTestScene から呼ぶ。
public static class HubMapTests
{
    private sealed class Host : IStoryHost
    {
        public readonly List<string> Lines = new();
        public Task Say(SayLine l) { Lines.Add($"{l.DisplayName}:{l.Text}"); return Task.CompletedTask; }
        public Task Narration(string t) { Lines.Add("N:" + t); return Task.CompletedTask; }
        public Task<int> Choice(IReadOnlyList<string> o) => Task.FromResult(o.Count - 1);
        public Task<string> NameInput(string d, int m) => Task.FromResult("テスト名");
        public Task Fade(string m, double s) => Task.CompletedTask;
        public Task Wait(double s) => Task.CompletedTask;
    }

    private static HubUpgradeManager NewHub() => new();
    private static HashSet<string> Charts(params string[] ids) => ids.ToHashSet();
    private static string Ids(IEnumerable<HubFacilityDef> l) => string.Join(",", l.Select(f => f.Id));

    public static void Run()
    {
        void Check(string label, bool ok, string detail) => GD.Print($"[検証] {label}: {(ok ? "OK" : "NG")} ({detail})");
        var map = HubData.Map; var geo = new HubGeometry(map); var half = HubWorld.PlayerHalf;
        var errs = new List<string>();
        var db = StoryBootstrap.LoadFromRes(errs);

        // ---- (1) 4画面の境界・通路・壁・カメラ ----
        Vector2 S(Vector2 p, Vector2 d) => geo.Slide(p, d, half);
        var spawn = new Vector2(map.Spawn[0], map.Spawn[1]);
        var toN = S(spawn, new Vector2(0, -900)); var toNwall = S(new Vector2(200, 360), new Vector2(0, -900));
        var toW = S(spawn, new Vector2(-1500, 0)); var toWwall = S(new Vector2(300, 100), new Vector2(-900, 0));
        var toS = S(spawn, new Vector2(0, 1300)); var toSwall = S(new Vector2(300, 360), new Vector2(0, 900));
        var toE = S(spawn, new Vector2(3000, 0)); var toEN = S(new Vector2(1000, -300), new Vector2(3000, 0));
        var nW = S(new Vector2(300, -300), new Vector2(-900, 0)); var wN = S(new Vector2(-640, 360), new Vector2(0, -900));
        Check("4画面: 中央の上端・左端・下端の通路から北/西/南へ歩いて通れる", geo.AreaAt(toN) == "north" && geo.AreaAt(toW) == "west" && geo.AreaAt(toS) == "south",
              $"北{toN} 西{toW} 南{toS}");
        Check("4画面: 通路以外の境界・東端・四隅の空き領域は壁でふさがれ、通り抜けられない",
              geo.AreaAt(toNwall) == "center" && geo.AreaAt(toWwall) == "center" && geo.AreaAt(toSwall) == "center"
              && geo.AreaAt(toE) == "center" && toE.X > 1200 && toEN.X > 1200 && geo.AreaAt(toEN) == "north"
              && nW.X > 0 && geo.AreaAt(wN) == "west" && wN.Y > 0,
              $"{toNwall} {toWwall} {toSwall} 東{toE} 北東{toEN} 北左{nW} 西上{wN}");
        var bounds = map.Areas.Select(a => a.Bounds).Aggregate((a, b) => a.Merge(b));
        var rootN = new Node2D(); var pl = new HubPlayer(); rootN.AddChild(pl);
        HubWorld.BuildStatic(rootN, map, geo, pl);
        var cam = pl.GetNode<Camera2D>("Camera2D"); var wallBody = rootN.GetNode<StaticBody2D>("Walls");
        Check("カメラの可動域は4画面の外接矩形、壁のコリジョンは幾何と同数",
              bounds == new Rect2(-1280, -720, 2560, 2160) && geo.Bounds == bounds && cam.LimitLeft == -1280 && cam.LimitTop == -720 && cam.LimitRight == 1280 && cam.LimitBottom == 1440
              && wallBody.GetChildCount() == geo.Walls.Count && geo.Walls.Count >= 10 && geo.AreaAt(spawn) == "center",
              $"limit {cam.LimitLeft},{cam.LimitTop},{cam.LimitRight},{cam.LimitBottom} 壁{wallBody.GetChildCount()}/{geo.Walls.Count}");
        rootN.Free();
        bool fixOk = map.Fixtures.All(f => map.Area(f.Area) != null && map.Area(f.Area).Bounds.HasPoint(f.Pos) && !geo.Blocked(f.Pos, new Vector2(40, 40)));
        var rects = map.Facilities.Where(f => f.Replaces == "").Concat(map.Facilities.Where(f => f.Replaces != "")).Select(f => new Rect2(f.Pos - new Vector2(70, 35), new Vector2(140, 70))).ToList();
        bool plazaOk = map.Facilities.All(f => f.Area == "center" && map.Area("center").Bounds.Grow(-100).HasPoint(f.Pos))
            && !map.Facilities.Any(f => new Rect2(f.Pos - new Vector2(70, 35), new Vector2(140, 70)).Intersects(new Rect2(spawn - new Vector2(150, 150), new Vector2(300, 300))));
        Check("設備・施設は自分の画面の中で壁に重ならず、広場の中央は空いている", fixOk && plazaOk, $"fix={fixOk} plaza={plazaOk}");

        // ---- (2) 施設の出現 ----
        var steps = new (string[] Done, string Want)[]
        {
            (new string[0], ""), (new[] { "c01", "c02" }, ""),
            (new[] { "c01", "c02", "c03" }, "warehouse"),
            (new[] { "c01", "c02", "c03", "c04" }, "warehouse,bushigaeru_shop"),
            (new[] { "c01", "c02", "c03", "c04", "c05" }, "warehouse,bushigaeru_shop,bulferno_diner"),
            (new[] { "c01", "c02", "c03", "c04", "c05", "c06" }, "warehouse,bushigaeru_shop,bulferno_diner,clinic"),
            (new[] { "c01", "c02", "c03", "c04", "c05", "c06", "c07", "c08", "c09", "c10", "c11" }, "warehouse,bushigaeru_shop,bulferno_diner,clinic"),
            (new[] { "c01", "c02", "c03", "c04", "c05", "c06", "c07", "c08", "c09", "c10", "c11", "c12" }, "warehouse,bushigaeru_shop,bulferno_diner,lab"),
        };
        var appear = new List<string>();
        foreach (var (done, want) in steps)
        {
            var st = new StoryState(); foreach (var c in done) st.CompletedCharts.Add(c);
            var a = NewHub(); a.ImportState(new SaveData { Story = st });
            var b = NewHub(); var r = SaveService.Parse(SaveService.Serialize(a.ExportState())); b.ImportState(r.Data);
            string now = Ids(map.VisibleFacilities(a.Story.CompletedCharts)), after = Ids(map.VisibleFacilities(b.Story.CompletedCharts));
            if (now != want || after != want) appear.Add($"{done.Length}: '{now}'/'{after}' != '{want}'");
        }
        Check("施設の出現: 2完了まで無し → 3で倉庫 → 4・5・6で順に増え → 12で診療所が研究所に置換。セーブ・ロード後も同じ",
              appear.Count == 0, appear.FirstOrDefault() ?? "8段階OK");
        string chartBad = string.Join(",", map.Facilities.Where(f => !(db.Chart(f.AppearsAfter)?.HubChanges.Contains(f.Id) ?? false)).Select(f => f.Id));
        var c11 = db.Chart("c11"); var c03 = db.Chart("c03");
        Check("hub_map の施設は charts.json の hub_changes と一致（c03=倉庫・c06=診療所・c12=研究所、c03 の clinic と c11 の lab は無い）",
              chartBad == "" && !c03.HubChanges.Contains("clinic") && !c11.HubChanges.Contains("lab") && db.Chart("c12").HubChanges.Contains("lab")
              && map.Facilities.All(f => db.Character(f.Npc.FirstOrDefault()?.Id ?? "") != null && f.Greeting.Count is >= 1 and <= 3 && f.Greeting.All(g => db.Character(g.Who) != null)),
              chartBad == "" ? "一致" : chartBad);
        int nodeBad = 0;
        foreach (var f in map.Facilities)
        {
            var n = HubWorld.BuildPlazaFacility(f, null, null);
            if (n.GetNodeOrNull<StaticBody2D>("Building") == null || n.GetNodeOrNull<HubSpot>("Spot") == null || n.GetChildren().OfType<Node2D>().Count(x => x.Name.ToString().StartsWith("Npc_")) != f.Npc.Count) nodeBad++;
            n.Free();
        }
        Check("広場の施設ノード（建物・Enter地点・NPC）が全施設で組み立てられる", nodeBad == 0, $"不備{nodeBad}/{map.Facilities.Count}");

        // ---- (3) 倉庫 ----
        var hw = NewHub();
        hw.GrantItem("baked_berries", 2); hw.GrantItem("wood", 3); hw.GrantItem("fire_bow_arrow", 4);
        var d1 = hw.Deposit("baked_berries", 1); var d2 = hw.Deposit("wood", 1); var d3 = hw.Deposit("baked_berries", 5);
        bool dep = d1.Ok && !d2.Ok && !d3.Ok && hw.CarriedCount("baked_berries") == 1 && hw.StoredCount("baked_berries") == 1 && hw.MaterialBank["wood"] == 3;
        var w1 = hw.Withdraw("baked_berries", 1); var w2 = hw.Withdraw("baked_berries", 1);
        bool wd = w1.Ok && !w2.Ok && hw.CarriedCount("baked_berries") == 2 && hw.StoredCount("baked_berries") == 0;
        hw.Deposit("fire_bow_arrow", 4);
        var hw2 = NewHub(); hw2.ImportState(SaveService.Parse(SaveService.Serialize(hw.ExportState())).Data);
        Check("倉庫: 預け入れ・引き出しの往復と保存（素材は預けられない／持ち物の不足・倉庫の不足は拒否）",
              dep && wd && hw2.StoredCount("fire_bow_arrow") == 4 && hw2.CarriedCount("baked_berries") == 2, $"dep={dep} wd={wd} saved={hw2.StoredCount("fire_bow_arrow")}");
        var hf = NewHub(); hf.GrantItem("baked_berries", InventoryManagerCap());
        var sdata = new SaveData(); sdata.Hub.Storage["low_grade_medical_supplies"] = 1; sdata.Hub.Storage["wood"] = 0;
        var full = NewHub(); full.GrantItem("baked_berries", InventoryManagerCap()); full.ImportState(new SaveData { Hub = sdata.Hub, Inventory = full.ExportState().Inventory });
        var wf = full.Withdraw("low_grade_medical_supplies", 1);
        Check("倉庫: 持ち物の上限を超える引き出しは拒否し、理由を返す（倉庫は減らない）", !wf.Ok && wf.Message.Contains("いっぱい") && full.StoredCount("low_grade_medical_supplies") == 1, wf.Message);

        // ---- (4) 通貨 ----
        var rw = HubData.Rewards;
        int r3 = rw.ClearReward(db, db.Dungeon("d03_c3"), false), r5 = rw.ClearReward(db, db.Dungeon("d07_c5"), false);
        int rs = rw.ClearReward(db, db.Dungeon("d34_s34"), false), rx = rw.ClearReward(db, db.Dungeon("x36"), false);
        var defOk = db.Dungeons.FirstOrDefault(d => d.Boss is { DefeatOk: true });
        Check("クリア報酬 30+10×チャート番号（補完は挿入先・追加枠は最終チャート）、敗北は0",
              r3 == 60 && r5 == 80 && rs == 150 && rx == 230 && rw.ClearReward(db, db.Dungeon("d03_c3"), true) == 0 && defOk != null && rw.ClearReward(db, defOk, true) == 0,
              $"c3={r3} c5={r5} 補完12={rs} 追加={rx} defeat_ok={defOk?.Id}");
        var pay = NewHub(); pay.GrantItem("baked_berries", 2); pay.GrantItem("fire_bow_arrow", 1); pay.GrantItem("wood", 1);
        var s1 = pay.Sell("nonfood", "baked_berries", 1, rw); var s2 = pay.Sell("food", "fire_bow_arrow", 1, rw);
        var s3 = pay.Sell("food", "baked_berries", 2, rw); var s4 = pay.Sell("", "wood", 1, rw); var s5 = pay.Sell("nonfood", "fire_bow_arrow", 1, rw);
        Check("売却: 食品は飯店だけ・食品以外は物産だけ・素材は不可。売値は買値の半分（切り捨て）",
              !s1.Ok && !s2.Ok && s3.Ok && !s4.Ok && s5.Ok && pay.Points == 15 * 2 + 15 && ItemDatabase.Get("fire_bow_arrow").Price / 2 == 15, $"points={pay.Points}");
        var buy = NewHub(); buy.AddPoints(70);
        var b1 = buy.Buy("food", "baked_berries", 3); var b2 = buy.Buy("food", "baked_berries", 2); var b3 = buy.Buy("nonfood", "baked_berries", 1); var b4 = buy.Buy("", "wood", 1);
        Check("購入: 残高が足りなければ拒否（残高は減らない）、買えれば残高が減り持ち物に入る。素材・別店の品は買えない",
              !b1.Ok && b2.Ok && !b3.Ok && !b4.Ok && buy.Points == 10 && buy.CarriedCount("baked_berries") == 2, $"points={buy.Points}");
        var old = JsonNode.Parse(SaveService.Serialize(hw.ExportState()));
        old["hub"].AsObject().Remove("points"); old["hub"].AsObject().Remove("storage");
        var legacy = SaveService.Parse(old.ToJsonString()); var hl = NewHub(); if (legacy.Status == LoadStatus.Ok) hl.ImportState(legacy.Data);
        buy.AddPoints(5); var hp = NewHub(); hp.ImportState(SaveService.Parse(SaveService.Serialize(buy.ExportState())).Data);
        var items = ItemDatabase.AllIds().Select(ItemDatabase.Get).ToList();
        Check("セーブ: ポイントが往復する／旧セーブ（points・storage 欠損、version 据え置き）は0・空で読める。全アイテムに price（素材のみ0）",
              hp.Points == 15 && legacy.Status == LoadStatus.Ok && hl.Points == 0 && hl.Storage.Count == 0 && SaveData.CurrentVersion == 1
              && items.Count >= 23 && items.All(i => (i.Type == ItemType.Material) == (i.Price == 0)) && items.Count(i => i.Type == ItemType.Material) == 4,
              $"points={hp.Points} legacy={legacy.Status} items={items.Count}");

        // ---- (5) 診療所・研究所 ----
        var hub = NewHub();
        var clinic = HubFacilityMenus.TabsFor(map.Facility("clinic"), hub, Charts("c06"));
        var medIds = clinic[0].Rows().Select(r => r.Id).OrderBy(x => x).ToList();
        var foodIds = HubFacilityMenus.ShopTabs("food", hub)[0].Rows().Select(r => r.Id).ToList();
        var nonfood = HubFacilityMenus.ShopTabs("nonfood", hub)[0].Rows().Select(r => ItemDatabase.Get(r.Id)).ToList();
        Check("診療所は薬だけを売買し（回復は無し）、飯店は食品だけ、物産は食品以外の取引可アイテム（薬・投げ物・装備品）",
              medIds.Count >= 3 && medIds.All(i => ItemDatabase.Get(i).IsMedicine) && clinic.Count == 2 && clinic.All(t => t.Title != "回復")
              && foodIds.Count >= 1 && foodIds.All(i => ItemDatabase.Get(i).IsFood)
              && nonfood.All(d => d.Tradable && !d.IsFood) && nonfood.Any(d => d.IsMedicine) && nonfood.Any(d => d.Type == ItemType.BattleHeld),
              $"薬{string.Join("/", medIds)} 食品{string.Join("/", foodIds)} 物産{nonfood.Count}種");
        var lab = HubFacilityMenus.TabsFor(map.Facility("lab"), hub, Charts("c12"));
        var an = lab.FirstOrDefault(t => t.Title == "分析");
        string noShard = an?.Act(null, 1) ?? "";
        hub.AddMaterial(HubUpgradeManager.ShardId, 1);
        string n12 = HubFacilityMenus.AnalysisTab(hub, Charts("c12")).Act(null, 1);
        string nAll = HubFacilityMenus.AnalysisTab(hub, Charts("c12", "c13", "c14", "c16")).Act(null, 1);
        Check("研究所: 診療所の機能はそのまま＋分析（かけら無しは不可、完了チャートに応じて4段階のメモ、かけらは消費しない）",
              lab.Count == 3 && lab[0].Title == "買う" && noShard.Contains("持っていない") && n12.Contains("パル由来") && !n12.Contains("ゼノ")
              && nAll.Contains("ゼノ系") && nAll.Contains("かけらの塊") && nAll.Contains("根と崩落") && hub.HasShard
              && HubData.NotesFor(Charts()).Count == 0 && HubData.NotesFor(Charts("c12", "c13", "c14", "c16")).Count == 4,
              $"tabs={lab.Count} 段階{HubData.NotesFor(Charts("c12", "c13")).Count}");

        // ---- (6) 西の入口・旧ポータル ----
        var bf = map.Fixtures.FirstOrDefault(f => f.Kind == "battle"); var gf = map.Fixtures.FirstOrDefault(f => f.Kind == "gate");
        string tscn = Godot.FileAccess.GetFileAsString("res://Scenes/HubScene.tscn"), cs = Godot.FileAccess.GetFileAsString("res://Scripts/Hub/HubScene.cs");
        Check("西の入口は常時開放で BattleFlowScene へ遷移・戻りは西の入口前。南端にダンジョンゲート。旧 DungeonPortal は削除済み",
              bf is { Area: "west", Scene: "res://Scenes/BattleFlowScene.tscn" } && Godot.FileAccess.FileExists(bf.Scene) && bf.ReturnPosition != null
              && map.Area("west").Bounds.HasPoint(new Vector2(bf.ReturnPosition[0], bf.ReturnPosition[1])) && gf is { Area: "south" } && gf.Pos.Y > 1200
              && !tscn.Contains("DungeonPortal") && !cs.Contains("DungeonPortal"),
              $"battle={bf?.Scene} gate={gf?.Pos}");

        // ---- (7) 追加した本文 ----
        var st7 = new StoryState { MbtiType = "ISTP", Protagonist = "027B", Partner = "004", PlayerName = "ミナト" };
        var host = new Host();
        var it = new EventInterpreter(db, st7, host) { SpeciesName = id => SpeciesDatabase.Instance.Get(id)?.DisplayName ?? id };
        var flow = new StoryFlow(db, st7, it);
        flow.EnterHub().GetAwaiter().GetResult();
        int guard = 0;
        string Expect(HashSet<string> c)
        {
            var l = new List<string>();
            if (c.Contains("c03")) l.Add("warehouse");
            if (c.Contains("c04")) l.Add("bushigaeru_shop");
            if (c.Contains("c05")) l.Add("bulferno_diner");
            if (c.Contains("c06")) l.Add(c.Contains("c12") ? "lab" : "clinic");
            return string.Join(",", l);
        }
        int bad7 = 0;
        while (!st7.PostClear && guard++ < 80)
        {
            var next = flow.AvailableDungeons().FirstOrDefault(d => !st7.ClearedDungeons.Contains(d.Id));
            if (next == null) break;
            flow.OnDungeonResult(new DungeonResult { DungeonId = next.Id }).GetAwaiter().GetResult();
            var fight = flow.TakePendingFight();
            if (fight != null) flow.OnDungeonResult(new DungeonResult { DungeonId = fight, Defeated = true }).GetAwaiter().GetResult();
            string vis = Ids(map.VisibleFacilities(st7.CompletedCharts));
            if (vis != Expect(st7.CompletedCharts)) bad7++;
        }
        string[] added = { "c03_e08_warehouse", "c04_e06_shop", "c05_e07_diner", "c06_e06_clinic", "c12_e07_lab" };
        int idx(string needle) => host.Lines.FindIndex(l => l.Contains(needle));
        int once(string needle) => host.Lines.Count(l => l.Contains(needle));
        Check("追加した本文（倉庫・物産・飯店・診療所・研究所）が一度ずつ流れ、チャート1〜20の通しが完走する（施設の出現は完了に追従）",
              added.All(st7.FiredEvents.Contains) && once("空いた石組みの倉") == 1 && once("板を並べただけの小さな店") == 1 && once("飯屋を開くっす") == 1
              && once("診療所、ということにしようかな") == 1 && once("ヴィオレッタ研究所」") == 1
              && idx("ヴィオレッタ研究所」") > idx("机の上で、二つのかけらが") && idx("飯屋を開くっす") > idx("ヘルガルダの群れの子らが")
              && Enumerable.Range(1, 20).All(c => st7.CompletedCharts.Contains($"c{c:00}")) && st7.PostClear && bad7 == 0
              && Ids(map.VisibleFacilities(st7.CompletedCharts)) == "warehouse,bushigaeru_shop,bulferno_diner,lab" && !host.Lines.Any(l => l.Contains("診療所を開くわ")),
              $"回数={guard} 不一致{bad7} 完了={st7.CompletedCharts.Count} 順序: 飯店{idx("飯屋を開くっす")}/研究所{idx("ヴィオレッタ研究所」")}");
    }

    private static int InventoryManagerCap() => MysteryDungeon.Entities.InventoryManager.MaxCapacity;
}
