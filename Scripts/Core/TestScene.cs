using Godot;
using MysteryDungeon.Grid;
using MysteryDungeon.Turn;
using MysteryDungeon.Entities;
using MysteryDungeon.Dungeon;
using MysteryDungeon.UI;
using MysteryDungeon.Combat;

namespace MysteryDungeon.Core;

// Composition root for the verification scene. Wires the Player up to
// GridManager/TurnManager, hands off the whole floor lifecycle
// (generation, object/enemy placement, stairs transition, cleanup) to
// FloorController, then wires the read-only MinimapUI overlay last (it
// needs FloorController's floor-1 dimensions to size itself).
public partial class TestScene : Node2D
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

        var dungeonConfig = new DungeonConfig { MaxFloors = MaxFloors, EndType = DungeonEndType.FreeDungeonBoss };

        floorController.Initialize(grid, turnManager, player, DungeonId, dungeonConfig);
        minimap.Initialize(grid, turnManager, player, floorController);
        hud.Initialize(player, turnManager, floorController);
        menu.Initialize(player, turnManager, floorController);

        GD.Print("=== Phase 8 Test Scene Ready ===");
        GD.Print("Arrow keys: move / bump into an enemy to attack / Enter or Space: wait (footstep) / Tab: open menu");
    }
}
