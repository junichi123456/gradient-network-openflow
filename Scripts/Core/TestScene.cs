using Godot;
using MysteryDungeon.Grid;
using MysteryDungeon.Turn;
using MysteryDungeon.Entities;
using MysteryDungeon.Dungeon;

namespace MysteryDungeon.Core;

// Composition root for the verification scene. Wires the Player up to
// GridManager/TurnManager, then hands off the whole floor lifecycle
// (generation, object/enemy placement, stairs transition, cleanup) to
// FloorController - TestScene itself no longer knows about any of that.
public partial class TestScene : Node2D
{
    [Export] public NodePath GridManagerPath { get; set; }
    [Export] public NodePath TurnManagerPath { get; set; }
    [Export] public NodePath PlayerPath { get; set; }
    [Export] public NodePath FloorControllerPath { get; set; }

    [Export] public string DungeonId { get; set; } = "beach_cave";

    public override void _Ready()
    {
        var grid = GetNode<GridManager>(GridManagerPath);
        var turnManager = GetNode<TurnManager>(TurnManagerPath);
        var player = GetNode<Player>(PlayerPath);
        var floorController = GetNode<FloorController>(FloorControllerPath);

        player.Grid = grid;
        player.TurnManager = turnManager;

        floorController.Initialize(grid, turnManager, player, DungeonId);

        GD.Print("=== Phase 2 Step 2 Test Scene Ready ===");
        GD.Print("Arrow keys: move / Enter or Space: wait (footstep)");
    }
}
