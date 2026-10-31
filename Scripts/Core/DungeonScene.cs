using Godot;
using MysteryDungeon.Grid;
using MysteryDungeon.Turn;
using MysteryDungeon.Entities;
using MysteryDungeon.Dungeon;
using MysteryDungeon.UI;
using MysteryDungeon.Combat;
using MysteryDungeon.Hub;

namespace MysteryDungeon.Core;

// Composition root for the dungeon scene. Wires the Player up to
// GridManager/TurnManager, hands off the whole floor lifecycle
// (generation, object/enemy placement, stairs transition, cleanup) to
// FloorController, then wires the read-only MinimapUI overlay last (it
// needs FloorController's floor-1 dimensions to size itself).
//
// DungeonId/MaxFloors below are also used as this scene's standalone
// defaults - launching DungeonScene.tscn directly (no Hub, no
// HubUpgradeManager selection) still produces a playable dungeon. When
// arriving from the Hub's Dungeon Gate, the pending selection on
// HubUpgradeManager takes priority and is consumed (cleared) here so a
// later standalone run doesn't accidentally reuse a stale selection.
public partial class DungeonScene : Node2D
{
    [Export] public NodePath GridManagerPath { get; set; }
    [Export] public NodePath TurnManagerPath { get; set; }
    [Export] public NodePath PlayerPath { get; set; }
    [Export] public NodePath FloorControllerPath { get; set; }
    [Export] public NodePath MinimapPath { get; set; }
    [Export] public NodePath HudPath { get; set; }
    [Export] public NodePath MenuPath { get; set; }

    [Export] public string DungeonId { get; set; } = "beach_cave";

    // 3 floors keeps a full normal-floor -> normal-floor -> boss-floor
    // playthrough short enough to exercise/verify end-to-end; a real
    // dungeon's own DungeonConfig would come from its own data source.
    [Export] public int MaxFloors { get; set; } = 3;

    public override void _Ready()
    {
        TypeChartManager.Load();
        MoveDatabase.Load();
        ItemDatabase.Load();
        TraitDatabase.Load();
        EcologyDatabase.Load();

        var grid = GetNode<GridManager>(GridManagerPath);
        var turnManager = GetNode<TurnManager>(TurnManagerPath);
        var player = GetNode<Player>(PlayerPath);
        var floorController = GetNode<FloorController>(FloorControllerPath);
        var minimap = GetNode<MinimapUI>(MinimapPath);
        var hud = GetNode<HUD>(HudPath);
        var menu = GetNode<MenuUI>(MenuPath);

        player.Grid = grid;
        player.TurnManager = turnManager;
        player.FloorController = floorController;
        player.MenuUI = menu;

        var hub = HubUpgradeManager.Instance;

        // Carries back whatever non-material items survived the last
        // Hub visit (see HubUpgradeManager.SaveInventory, called from
        // FloorController.HandleDungeonCleared). Empty/no-op on a
        // fresh game - the snapshot starts empty.
        hub?.RestoreInventory(player.Inventory);

        string dungeonId = DungeonId;
        var dungeonConfig = new DungeonConfig { MaxFloors = MaxFloors, EndType = DungeonEndType.FreeDungeonBoss };

        if (hub != null && hub.PendingDungeonConfig != null)
        {
            dungeonConfig = hub.PendingDungeonConfig;
            dungeonId = hub.PendingDungeonId ?? dungeonId;

            // Consumed on read, so a later standalone launch of this
            // scene (or a Hub session that never visits the Dungeon
            // Gate again) doesn't silently reuse a stale selection.
            hub.PendingDungeonConfig = null;
            hub.PendingDungeonId = null;
        }

        floorController.Initialize(grid, turnManager, player, dungeonId, dungeonConfig);
        minimap.Initialize(grid, turnManager, player, floorController);
        hud.Initialize(player, turnManager, floorController);
        menu.Initialize(player, turnManager, floorController);

        GD.Print("=== Dungeon Scene Ready ===");
        GD.Print($"[Dungeon] Party roster: {floorController.PartyManager.RecruitedRoster.Count} recruited, {floorController.PartyManager.ActiveParty.Count} active.");
        GD.Print($"[Dungeon] dungeonId={dungeonId}, MaxFloors={dungeonConfig.MaxFloors}, EndType={dungeonConfig.EndType}");
        GD.Print("Arrow keys: move / bump into an enemy to attack / Enter or Space: wait (footstep) / Tab: open menu");
    }
}
