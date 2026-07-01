using Godot;
using MysteryDungeon.Grid;
using MysteryDungeon.Turn;
using MysteryDungeon.Entities;
using MysteryDungeon.Dungeon;

namespace MysteryDungeon.Core;

// Composition root for the verification scene: generates a dungeon
// floor into GridManager, wires each entity's dependencies (Grid
// reference, TurnManager reference, scheduler registration) and spawns
// them into rooms produced by that generation.
public partial class TestScene : Node2D
{
    [Export] public NodePath GridManagerPath { get; set; }
    [Export] public NodePath TurnManagerPath { get; set; }
    [Export] public NodePath PlayerPath { get; set; }
    [Export] public NodePath DummyNpcPath { get; set; }
    [Export] public NodePath FastNpcPath { get; set; }

    [Export] public string DungeonId { get; set; } = "beach_cave";

    public override void _Ready()
    {
        var grid = GetNode<GridManager>(GridManagerPath);
        var turnManager = GetNode<TurnManager>(TurnManagerPath);
        var player = GetNode<Player>(PlayerPath);
        var dummyNpc = GetNode<DummyNPC>(DummyNpcPath);
        var fastNpc = GetNode<FastNPC>(FastNpcPath);

        var rule = DungeonRuleLoader.Load(DungeonId);
        ulong seed = GD.Randi();
        var result = new DungeonGenerator().Generate(grid, rule, seed);

        player.Grid = grid;
        dummyNpc.Grid = grid;
        fastNpc.Grid = grid;

        player.TurnManager = turnManager;

        SpawnInRoom(player, result, 0);
        SpawnInRoom(dummyNpc, result, 1);
        SpawnInRoom(fastNpc, result, 2);

        turnManager.RegisterActor(dummyNpc);
        turnManager.RegisterActor(fastNpc);

        GD.Print("=== Phase 2 Step 1 Test Scene Ready ===");
        GD.Print($"Rooms generated: {result.Rooms.Count}");
        GD.Print("Arrow keys: move / Enter or Space: wait (footstep)");
    }

    // Places `entity` at the center of result.Rooms[index], wrapping
    // around (via modulo) if fewer rooms were generated than entities
    // to place - keeps the scene runnable even on a small/sparse map.
    private static void SpawnInRoom(Entity entity, DungeonGenerationResult result, int index)
    {
        if (result.Rooms.Count == 0)
        {
            GD.PushError("[TestScene] DungeonGenerator produced no rooms - cannot spawn entities.");
            entity.PlaceAt(Vector2I.Zero);
            return;
        }

        var room = result.Rooms[index % result.Rooms.Count];
        var center = room.Position + room.Size / 2;
        entity.PlaceAt(center);
    }
}
