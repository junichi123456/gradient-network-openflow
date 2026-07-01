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

    [Export] public string DungeonId { get; set; } = "beach_cave";

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

        player.Grid = grid;
        player.TurnManager = turnManager;
        player.FloorController = floorController;

        floorController.Initialize(grid, turnManager, player, DungeonId);
        minimap.Initialize(grid, turnManager, player, floorController);

        GD.Print("=== Phase 4 Test Scene Ready ===");
        GD.Print("Arrow keys: move / bump into an enemy to attack / Enter or Space: wait (footstep)");
    }
}
