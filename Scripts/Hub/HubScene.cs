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
    [Export] public NodePath DungeonPortalPath { get; set; }
    [Export] public NodePath PartySetupUIPath { get; set; }
    [Export] public NodePath DungeonSelectUIPath { get; set; }

    private StoryFlow _flow;
    private StoryDialogueUI _storyUi;
    private HubPlayer _hubPlayer;
    private bool _menuBusy;
    private int _residentNpcs;

    public override void _Ready()
    {
        var player = GetNode<HubPlayer>(PlayerPath);
        var shopLogLabel = GetNodeOrNull<Label>(ShopLogLabelPath);
        var portal = GetNodeOrNull<Area2D>(DungeonPortalPath);
        var partySetupUI = GetNode<PartySetupUI>(PartySetupUIPath);
        var dungeonSelectUI = GetNode<DungeonSelectUI>(DungeonSelectUIPath);

        partySetupUI.Initialize(player);
        dungeonSelectUI.Initialize(player);

        if (portal != null)
        {
            portal.BodyEntered += body =>
            {
                if (body is HubPlayer) GetTree().ChangeSceneToFile("res://Scenes/DungeonScene.tscn");
            };
        }

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
        _flow = flow; _storyUi = ui; _hubPlayer = player;
        if (hub.PendingResult != null)
        {
            var result = hub.PendingResult;
            hub.PendingResult = null;
            await flow.OnDungeonResult(result);
        }
        await flow.EnterHub();
        ui.Close();
        hub.Save();
        player.InputEnabled = true;
        ShowRecruitedResidents(hub);

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
            AddChild(new HubPalNpc { RequiredLevel = 1, DebugColor = Colors.Gold, Position = new Vector2(260 + 36 * _residentNpcs, 430) });
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
