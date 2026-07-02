using Godot;

namespace MysteryDungeon.Hub;

// Placeholder NPC pal - purely decorative for Phase 11 (no AI/wander).
// Shown or hidden based on HubUpgradeManager's facility levels, so the
// hub visibly fills up as the player invests materials into upgrades.
public partial class HubPalNpc : Node2D
{
    [Export] public string RequiredFacilityId { get; set; } = "pal_bed";
    [Export] public int RequiredLevel { get; set; } = 2;
    [Export] public Color DebugColor { get; set; } = Colors.HotPink;

    public override void _Ready()
    {
        var visual = new ColorRect
        {
            Color = DebugColor,
            Size = new Vector2(24, 24),
            Position = new Vector2(-12, -12),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(visual);

        RefreshVisibility();

        if (HubUpgradeManager.Instance != null)
            HubUpgradeManager.Instance.FacilityUpgraded += OnFacilityUpgraded;
    }

    private void OnFacilityUpgraded(string facilityId, int newLevel)
    {
        if (facilityId == RequiredFacilityId) RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        int level = HubUpgradeManager.Instance?.GetFacilityLevel(RequiredFacilityId) ?? 1;
        bool wasVisible = Visible;
        Visible = level >= RequiredLevel;

        if (Visible && !wasVisible)
            GD.Print($"[Hub] A new pal has moved in near {RequiredFacilityId} (now Lv.{level})!");
    }
}
