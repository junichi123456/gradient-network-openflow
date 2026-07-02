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

    // Which side this entity fights for. Defaults to Enemy so
    // DummyNPC/FastNPC/HostileEntity need no extra code; Player and
    // AllyEntity override to Player in their own _Ready().
    [Export] public Faction Faction { get; set; } = Faction.Enemy;

    // Assigned by the composition root (DungeonScene) after instancing.
    public GridManager Grid { get; set; }

    public Vector2I GridPosition { get; private set; }
    public bool IsAlive { get; protected set; } = true;

    // The tile this entity stood on before its most recent MoveTo (null
    // until it has moved at least once). AllyEntity's Follow state walks
    // toward its TargetToFollow's PreviousPosition each turn - a "conga
    // line" that needs no shared queue and no A* (see AllyEntity).
    public Vector2I? PreviousPosition { get; private set; }

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
        PreviousPosition = GridPosition;
        GridPosition = targetPos;
        if (Grid != null) Position = Grid.GridToWorld(targetPos);
    }

    public void Wait()
    {
        // Footstep: consumes a turn without changing position.
    }

    // Entity-aware walkability: unlike GridManager.IsWalkable (a plain
    // Floor-only check), this consults Stats.CanTraverse so a Hover/
    // Fire/Water-Ice mover correctly treats its own hazard tiles as
    // walkable.
    public bool CanWalkTo(Vector2I pos) =>
        Grid != null && Grid.InBounds(pos) && Stats.CanTraverse(Grid.GetTile(pos).Terrain);

    // CanWalkTo plus, for a diagonal step only, the Wall-only corner-
    // cutting rule (GridManager.CanCutCorner). Used for direct
    // (non-pathfinding) movement attempts - Player's manual input and
    // HostileEntity/AllyEntity's non-chase movement. A*-driven chase
    // movement gets the same corner-cutting guarantee for free from how
    // AStarPathfinder builds its grid (see AStarPathfinder).
    public bool CanMoveTo(Vector2I target)
    {
        if (!CanWalkTo(target)) return false;

        var delta = target - GridPosition;
        if (Mathf.Abs(delta.X) == 1 && Mathf.Abs(delta.Y) == 1)
            return Grid.CanCutCorner(GridPosition, target);

        return true;
    }

    // Melee reach: all 8 surrounding tiles, EXCEPT a diagonal whose
    // corner is blocked by a Wall shoulder (an attack can't bend around
    // a wall corner any more than a step or a thrown item can). Shared
    // by every attacker - Player's bump attack, HostileEntity, and
    // AllyEntity - so the rule stays symmetric across factions.
    public bool CanAttackAdjacent(Vector2I targetPos)
    {
        var diff = (targetPos - GridPosition).Abs();
        if (diff.X > 1 || diff.Y > 1 || (diff.X == 0 && diff.Y == 0)) return false;

        if (diff.X == 1 && diff.Y == 1)
            return Grid == null || Grid.CanCutCorner(GridPosition, targetPos);

        return true;
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
