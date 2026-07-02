using Godot;

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

    public override void _Ready()
    {
        var player = GetNode<HubPlayer>(PlayerPath);
        var shopLogLabel = GetNodeOrNull<Label>(ShopLogLabelPath);
        var portal = GetNodeOrNull<Area2D>(DungeonPortalPath);

        if (portal != null)
        {
            portal.BodyEntered += body =>
            {
                if (body is HubPlayer) GetTree().ChangeSceneToFile("res://Scenes/TestScene.tscn");
            };
        }

        RefreshShopLog(shopLogLabel);
        if (HubUpgradeManager.Instance != null)
            HubUpgradeManager.Instance.FacilityUpgraded += (_, _) => RefreshShopLog(shopLogLabel);

        GD.Print("=== Hub Scene Ready ===");
        GD.Print($"[Hub] Player spawned at {player.Position}.");
        GD.Print("Arrow keys: move freely / walk into a facility + Enter to upgrade it / walk into the portal to re-enter a dungeon");

        var hub = HubUpgradeManager.Instance;
        if (hub != null)
            GD.Print($"[Hub] pal_workbench Lv.{hub.GetFacilityLevel("pal_workbench")}, pal_bed Lv.{hub.GetFacilityLevel("pal_bed")}");
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
