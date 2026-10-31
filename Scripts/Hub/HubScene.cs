using Godot;
using System.Linq;
using System.Threading.Tasks;
using MysteryDungeon.Species;
using MysteryDungeon.Story;

namespace MysteryDungeon.Hub;

// Composition root for the non-grid, real-time Hub scene. Deliberately
// wires up nothing from the Turn/Grid/Combat systems (no TurnManager,
// no MenuUI, no UseItemAction/ThrowItemAction) - item use is disabled
// in the hub simply because there is no code path here that could ever
// invoke it, not because of a runtime guard.
public partial class HubScene : Node2D
{
    [Export] public NodePath PlayerPath { get; set; }
    [Export] public NodePath ShopLogLabelPath { get; set; }
    [Export] public NodePath HubListUIPath { get; set; }
    [Export] public NodePath PointsLabelPath { get; set; }
    [Export] public NodePath PartySetupUIPath { get; set; }
    [Export] public NodePath DungeonSelectUIPath { get; set; }

    private StoryFlow _flow;
    private StoryDialogueUI _storyUi;
    private HubPlayer _hubPlayer;
    private bool _menuBusy;
    private int _residentNpcs;
    private HubMapDef _map;
    private HubListUI _listUi;
    private Label _pointsLabel;
    private bool _busy;
    private readonly System.Collections.Generic.Dictionary<string, Node2D> _plaza = new();

    // 今、広場に立っている施設ID（テスト・確認用）。
    public System.Collections.Generic.IReadOnlyCollection<string> PlazaFacilityIds => _plaza.Keys;

    public override void _Ready()
    {
        var player = GetNode<HubPlayer>(PlayerPath);
        var shopLogLabel = GetNodeOrNull<Label>(ShopLogLabelPath);
        _listUi = GetNode<HubListUI>(HubListUIPath);
        _pointsLabel = GetNodeOrNull<Label>(PointsLabelPath);
        _hubPlayer = player;
        var partySetupUI = GetNode<PartySetupUI>(PartySetupUIPath);
        var dungeonSelectUI = GetNode<DungeonSelectUI>(DungeonSelectUIPath);

        partySetupUI.Initialize(player);
        dungeonSelectUI.Initialize(player);
        _listUi.Initialize(player);
        _listUi.Closed += () => { HubUpgradeManager.Instance?.Save(); _busy = false; RefreshPoints(); };

        // 十字型4画面の世界を hub_map.json から組み立てる。
        _map = HubData.Map;
        var geo = new HubGeometry(_map);
        HubWorld.BuildStatic(this, _map, geo, player);
        HubWorld.BuildFixtures(this, _map, EnterBattle, IsBlocked, AfterBedAccept);
        var spawn = new Vector2(_map.Spawn[0], _map.Spawn[1]);
        var hub0 = HubUpgradeManager.Instance;
        if (hub0 != null && hub0.ReturnSpawn == "battle")
        {
            var f = _map.Fixtures.Find(x => x.Kind == "battle");
            if (f?.ReturnPosition != null) spawn = new Vector2(f.ReturnPosition[0], f.ReturnPosition[1]);
            hub0.ReturnSpawn = "";
        }
        player.Position = spawn;
        RefreshPlaza();
        RefreshPoints();

        RefreshShopLog(shopLogLabel);
        if (HubUpgradeManager.Instance != null)
            HubUpgradeManager.Instance.FacilityUpgraded += (_, _) => RefreshShopLog(shopLogLabel);

        GD.Print("=== Hub Scene Ready ===");
        GD.Print($"[Hub] Player spawned at {player.Position}.");
        GD.Print("Arrow keys: move freely / walk into a facility + Enter to upgrade it / Pal Box or Dungeon Gate + Enter to manage party or depart");

        var hub = HubUpgradeManager.Instance;
        if (hub != null)
        {
            GD.Print($"[Hub] pal_workbench Lv.{hub.GetFacilityLevel("pal_workbench")}, pal_bed Lv.{hub.GetFacilityLevel("pal_bed")}");
            GD.Print($"[Hub] Party roster: {hub.PartyManager.RecruitedRoster.Count} recruited, {hub.PartyManager.ActiveParty.Count} active.");
        }

        _ = StartStoryIfNeeded(player);
    }

    // 初回: セーブがあれば読み込み、無ければ「はじめから」（夢の4問→相棒選び）。
    // どちらでも、ここで StoryFlow を回す: ダンジョンから戻った結果の処理 → チャートの開始。
    private async Task StartStoryIfNeeded(HubPlayer player)
    {
        var hub = HubUpgradeManager.Instance;
        var db = StoryDatabase.Instance;
        if (hub == null || db == null) return;

        if (!hub.StoryInitialized)
        {
            hub.StoryInitialized = true;
            if (SaveService.Exists())
            {
                var r = SaveService.Load();
                if (r.Status == LoadStatus.Ok) hub.ImportState(r.Data);
                else
                {
                    GD.PushWarning($"[Story] {r.Message}");
                    if (r.Status == LoadStatus.Incompatible) return;   // 上書きせずに通常起動
                }
            }
            if (hub.Story.Protagonist == "")
            {
                player.InputEnabled = false;
                var sel = new SelectionScene();
                AddChild(sel);
                await sel.StartNewGame(hub.Story, db);
                hub.PartyManager.SetStartingParty(hub.Story.Protagonist, hub.Story.Partner);
            }
        }
        if (hub.Story.Protagonist == "") return;

        player.InputEnabled = false;
        var ui = new StoryDialogueUI();
        AddChild(ui);
        var it = new EventInterpreter(db, hub.Story, ui)
        {
            SpeciesName = id => SpeciesDatabase.Instance.Get(id)?.DisplayName ?? id,
            PrimaryType = id => SpeciesDatabase.Instance.Get(id)?.Types[0].ToString() ?? "",
        };
        it.Callbacks.GiveItem = hub.GrantItem;
        var flow = new StoryFlow(db, hub.Story, it)
        {
            SpeciesName = it.SpeciesName,
            // 勧誘成功: 既存のロスター（PartyManager）へ加える。
            OnRecruit = id => { if (!hub.PartyManager.RecruitedRoster.Contains(id)) hub.PartyManager.AddToRoster(id); },
        };
        _flow = flow; _storyUi = ui;
        GetNode<DungeonSelectUI>(DungeonSelectUIPath).BeforeDepart = async dest =>
        {
            await flow.EnterSupplementDungeon(dest.Id);
            ui.Close();
            hub.Save();
        };
        if (hub.PendingResult != null)
        {
            var result = hub.PendingResult;
            hub.PendingResult = null;
            // クリア報酬（敗北は0）。式は Data/story/rewards.json。
            int pts = HubData.Rewards.ClearReward(db, db.Dungeon(result.DungeonId), result.Defeated);
            hub.AddPoints(pts);
            await flow.OnDungeonResult(result);
            if (pts > 0) await ui.Narration($"{pts}{_map.Currency}を手に入れた。");
            RefreshPoints();
        }
        await flow.EnterHub();
        ui.Close();
        hub.Save();
        player.InputEnabled = true;
        ShowRecruitedResidents(hub);
        RefreshPlaza();

        string fight = flow.TakePendingFight();
        if (fight != null) EnterScenarioFight(db, hub, fight);
    }

    // M キー: ハコニワのメニュー（思い出／住民と話す）。会話窓の選択肢で開く。
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.M } && _flow != null && !_menuBusy && _hubPlayer.InputEnabled)
            _ = OpenStoryMenu();
    }

    private async Task OpenStoryMenu()
    {
        _menuBusy = true; _hubPlayer.InputEnabled = false;
        try { await _flow.RunHubMenu(); }
        finally
        {
            _storyUi.Close();
            HubUpgradeManager.Instance?.Save();
            _hubPlayer.InputEnabled = true; _menuBusy = false;
        }
    }

    // 仲間になった伝説・ボスパルは、ハコニワの住民として現れる。
    private void ShowRecruitedResidents(HubUpgradeManager hub)
    {
        foreach (var _ in hub.Story.RecruitedPals.OrderBy(x => x, System.StringComparer.Ordinal).Skip(_residentNpcs))
        {
            // 勧誘で増えた仲間は、中央の広場（建物のない中ほど）に立つ。
            AddChild(new HubPalNpc { RequiredLevel = 1, DebugColor = Colors.Gold, Position = new Vector2(480 + 36 * (_residentNpcs % 8), 330 + 40 * (_residentNpcs / 8)) });
            _residentNpcs++;
        }
    }

    // start_scenario_fight: 隠しダンジョン（ヘルガルダ戦・世界樹の根もと）へ直行する。
    private void EnterScenarioFight(StoryDatabase db, HubUpgradeManager hub, string dungeonId)
    {
        var dest = DungeonDestinations.From(db, db.Dungeon(dungeonId), hub.Story);
        hub.PendingDungeonConfig = dest.Config;
        hub.PendingDungeonId = dest.DungeonRuleId;
        GetTree().ChangeSceneToFile("res://Scenes/DungeonScene.tscn");
    }

    private bool IsBlocked() => _busy || _hubPlayer == null || !_hubPlayer.InputEnabled;

    private void RefreshPoints()
    {
        var hub = HubUpgradeManager.Instance;
        if (_pointsLabel != null && hub != null) _pointsLabel.Text = $"{_map.Currency}: {hub.Points}";
    }

    // 寝床で Enter: 強化を試みたあと、セーブする。
    private void AfterBedAccept(HubFixtureDef f)
    {
        HubUpgradeManager.Instance?.Save();
        GD.Print("[Hub] セーブしました。");
        if (_pointsLabel != null) _pointsLabel.Text = $"{_map.Currency}: {HubUpgradeManager.Instance?.Points ?? 0}   (セーブしました)";
    }

    // 西の対戦広場: 既存の対戦フロー（BattleFlowScene）へ。戻りは BattleFlowScene の F10。
    private void EnterBattle(HubFixtureDef f)
    {
        var hub = HubUpgradeManager.Instance;
        if (hub != null) { hub.ReturnSpawn = "battle"; hub.Save(); }
        GetTree().ChangeSceneToFile(f.Scene);
    }

    // 完了済みチャートから広場の施設を作り直す（セーブ・ロード後も同じ状態になる）。
    public void RefreshPlaza()
    {
        var done = HubUpgradeManager.Instance?.Story.CompletedCharts ?? new System.Collections.Generic.HashSet<string>();
        var want = _map.VisibleFacilities(done);
        foreach (var id in _plaza.Keys.Where(k => want.All(f => f.Id != k)).ToList())
        {
            _plaza[id].QueueFree(); _plaza.Remove(id);
        }
        foreach (var f in want)
        {
            if (_plaza.ContainsKey(f.Id)) continue;
            var node = HubWorld.BuildPlazaFacility(f, f2 => _ = UseFacility(f2), IsBlocked);
            AddChild(node); _plaza[f.Id] = node;
        }
    }

    // 施設の主の挨拶（会話窓）のあと、施設の一覧UIを開く。
    private async Task UseFacility(HubFacilityDef f)
    {
        var hub = HubUpgradeManager.Instance;
        if (hub == null || _busy) return;
        _busy = true; _hubPlayer.InputEnabled = false;
        var db = StoryDatabase.Instance;
        if (_storyUi != null && db != null)
        {
            foreach (var g in f.Greeting)
            {
                var ch = db.Character(g.Who);
                await _storyUi.Say(new SayLine(g.Who, ch?.DisplayName ?? g.Who, ch?.SpeciesId ?? "", g.Text, ""));
            }
            _storyUi.Close();
        }
        _listUi.Open(f.Name, HubFacilityMenus.TabsFor(f, hub, hub.Story.CompletedCharts), () => HubFacilityMenus.Header(hub));
    }

    private void RefreshShopLog(Label label)
    {
        if (label == null) return;

        int level = HubUpgradeManager.Instance?.GetFacilityLevel("pal_workbench") ?? 1;
        label.Text = level switch
        {
            1 => "[パル作業台] 簡素な道具しかありません。",
            2 => "[パル作業台] 木製の道具が並ぶようになりました。",
            _ => "[パル作業台] 上質な道具が揃っています！",
        };
    }
}
