using Godot;
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

    // セーブがあれば読み込み、無ければ「はじめから」（夢の4問→相棒選び→チャート1の水面イベント）。
    // タイトル画面は無いので、ハコニワ起動直後にここで分岐する。
    private async Task StartStoryIfNeeded(HubPlayer player)
    {
        var hub = HubUpgradeManager.Instance;
        var db = StoryDatabase.Instance;
        if (hub == null || db == null || hub.StoryInitialized) return;
        hub.StoryInitialized = true;

        if (SaveService.Exists())
        {
            var r = SaveService.Load();
            if (r.Status == LoadStatus.Ok) { hub.ImportState(r.Data); return; }
            GD.PushWarning($"[Story] {r.Message}");
            if (r.Status == LoadStatus.Incompatible) return;   // 上書きせずに通常起動
        }
        if (hub.Story.Protagonist != "") return;

        player.InputEnabled = false;
        var sel = new SelectionScene();
        AddChild(sel);
        await sel.StartNewGame(hub.Story, db);
        hub.PartyManager.SetStartingParty(hub.Story.Protagonist, hub.Story.Partner);

        var ui = new StoryDialogueUI();
        AddChild(ui);
        var it = new EventInterpreter(db, hub.Story, ui)
            { SpeciesName = id => SpeciesDatabase.Instance.Get(id)?.DisplayName ?? id };
        hub.Story.CurrentChart = "c01";
        await it.Fire("chart_start", new EventInterpreter.TriggerArgs(Chart: "c01"));
        ui.Close();
        hub.Save();
        player.InputEnabled = true;
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
