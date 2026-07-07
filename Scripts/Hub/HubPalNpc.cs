using Godot;
using MysteryDungeon.Visuals;

namespace MysteryDungeon.Hub;

// Placeholder NPC pal - purely decorative for Phase 11 (no AI/wander).
// Shown or hidden based on HubUpgradeManager's facility levels, so the
// hub visibly fills up as the player invests materials into upgrades.
public partial class HubPalNpc : Node2D
{
    [Export] public string RequiredFacilityId { get; set; } = "pal_bed";
    [Export] public int RequiredLevel { get; set; } = 2;
    [Export] public Color DebugColor { get; set; } = Colors.HotPink;

    private const float VisualSize = 24f;

    public override void _Ready()
    {
        // A pal NPC is a character, not a static prop - feet-anchored
        // like HubPlayer, for correct Y-Sort ordering against it.
        var visual = new Sprite2D
        {
            Texture = SpriteTextureLibrary.GetTexture("", DebugColor, (int)VisualSize),
            Centered = true,
        };
        // See HubPlayer._Ready(): Offset must track the actual loaded
        // texture's height, not the VisualSize placeholder constant.
        visual.Offset = new Vector2(0, -visual.Texture.GetHeight() / 2f);
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
