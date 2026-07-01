using Godot;
using MysteryDungeon.Grid;
using MysteryDungeon.Turn;

namespace MysteryDungeon.Entities;

// Base actor: a grid position plus a placeholder ColorRect visual.
// ActorName/Speed/DebugColor are exported so each concrete scene
// (Player/DummyNPC/FastNPC) configures its own identity from the
// Godot editor Inspector rather than hard-coding it in script.
public partial class Entity : Node2D, ITurnActor
{
    [Export] public string ActorName { get; set; } = "Entity";
    [Export] public int Speed { get; set; } = 100;
    [Export] public Color DebugColor { get; set; } = Colors.White;

    // Assigned by the composition root (TestScene) after instancing.
    public GridManager Grid { get; set; }

    public Vector2I GridPosition { get; private set; }
    public bool IsAlive { get; protected set; } = true;

    // Combat/survival stats component (HP, Attack/Defense, types,
    // hunger). Reuses a hand-placed "Stats" child node if the scene
    // defines one, otherwise creates a default-valued one - so
    // dynamically spawned entities (FloorController.SpawnEnemyAt)
    // always have a valid Stats reference with no scene setup needed.
    public EntityStats Stats { get; private set; }

    // Up to 4 learned moves. Same auto-attach pattern as Stats.
    public MoveManager Moves { get; private set; }

    public override void _Ready()
    {
        Stats = GetNodeOrNull<EntityStats>("Stats");
        if (Stats == null)
        {
            Stats = new EntityStats { Name = "Stats" };
            AddChild(Stats);
        }

        Moves = GetNodeOrNull<MoveManager>("Moves");
        if (Moves == null)
        {
            Moves = new MoveManager { Name = "Moves" };
            AddChild(Moves);
        }

        var visual = new ColorRect
        {
            Color = DebugColor,
            Size = new Vector2(28, 28),
            Position = new Vector2(-14, -14),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(visual);
    }

    public void PlaceAt(Vector2I gridPos)
    {
        GridPosition = gridPos;
        if (Grid != null) Position = Grid.GridToWorld(gridPos);
    }

    public void MoveTo(Vector2I targetPos)
    {
        GridPosition = targetPos;
        if (Grid != null) Position = Grid.GridToWorld(targetPos);
    }

    public void Wait()
    {
        // Footstep: consumes a turn without changing position.
    }

    // Called when Stats.CurrentHp reaches 0. NPCs are removed from the
    // scene entirely; Player overrides this to trigger game-over instead
    // (see Player.Die()) - AttackAction just calls defender.Die()
    // uniformly and lets polymorphism pick the right behavior.
    public virtual void Die()
    {
        if (!IsAlive) return; // guards against double-invocation
        IsAlive = false;
        QueueFree();
    }

    public virtual IAction DecideAction() => new WaitAction(this);
}
