using Godot;
using MysteryDungeon.Grid;
using MysteryDungeon.Turn;
using MysteryDungeon.Entities;

namespace MysteryDungeon.Core;

// Composition root for the Phase 1 verification scene: looks up the
// hand-placed nodes, wires their dependencies (Grid reference, TurnManager
// reference, scheduler registration) and places entities on the grid.
public partial class TestScene : Node2D
{
    [Export] public NodePath GridManagerPath { get; set; }
    [Export] public NodePath TurnManagerPath { get; set; }
    [Export] public NodePath PlayerPath { get; set; }
    [Export] public NodePath DummyNpcPath { get; set; }
    [Export] public NodePath FastNpcPath { get; set; }

    public override void _Ready()
    {
        var grid = GetNode<GridManager>(GridManagerPath);
        var turnManager = GetNode<TurnManager>(TurnManagerPath);
        var player = GetNode<Player>(PlayerPath);
        var dummyNpc = GetNode<DummyNPC>(DummyNpcPath);
        var fastNpc = GetNode<FastNPC>(FastNpcPath);

        player.Grid = grid;
        dummyNpc.Grid = grid;
        fastNpc.Grid = grid;

        player.TurnManager = turnManager;

        player.PlaceAt(new Vector2I(2, 2));
        dummyNpc.PlaceAt(new Vector2I(10, 3));
        fastNpc.PlaceAt(new Vector2I(5, 7));

        turnManager.RegisterActor(dummyNpc);
        turnManager.RegisterActor(fastNpc);

        GD.Print("=== Phase 1 Test Scene Ready ===");
        GD.Print("Arrow keys: move / Enter or Space: wait (footstep)");
    }
}
