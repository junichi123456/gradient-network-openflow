using System;
using Godot;
using MysteryDungeon.Grid;
using MysteryDungeon.Turn;
using MysteryDungeon.Visuals;

namespace MysteryDungeon.Entities;

// Base actor: a grid position plus a Sprite2D visual (see
// SpriteTextureLibrary for the real-art-or-placeholder loading policy).
// ActorName/Speed/DebugColor/SpriteId are exported so each concrete
// scene (Player/DummyNPC/FastNPC) configures its own identity from the
// Godot editor Inspector rather than hard-coding it in script.
public partial class Entity : Node2D, ITurnActor
{
    [Export] public string ActorName { get; set; } = "Entity";
    [Export] public int Speed { get; set; } = 100;
    [Export] public Color DebugColor { get; set; } = Colors.White;

    // Empty until real MagicaVoxel-rendered art exists - see
    // SpriteTextureLibrary. Setting this to e.g. "player_idle" and
    // dropping res://Assets/Sprites/player_idle.png in is the entire
    // swap, no script changes required.
    [Export] public string SpriteId { get; set; } = "";

    // Lets a species render larger than its 1-tile footprint (e.g. 1.3
    // or 1.5 for a bigger pal) without touching occupancy/movement/
    // attack logic at all - those are all GridPosition-based (integer
    // tile coordinates) and never look at this. Purely a Sprite2D.Scale
    // multiplier; see _Ready() for why this doesn't break the feet
    // anchor Y-Sort depends on.
    [Export] public float VisualScale { get; set; } = 1.0f;

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

    // Visual-only facing, driven by MoveTo/PlayBumpAttack (see
    // UpdateFacingDirection) and used to pick the matching 8-direction
    // sprite (SpriteTextureLibrary). Distinct from Player.
    // LastFacingDirection, which reflects raw held input (including
    // diagonal_lock's turn-without-moving) for autoaim purposes - mixing
    // the two would risk that unrelated feature.
    public Vector2I FacingDirection { get; private set; } = new Vector2I(0, 1);

    // Combat/survival stats component (HP, Attack/Defense, types,
    // hunger). Reuses a hand-placed "Stats" child node if the scene
    // defines one, otherwise creates a default-valued one - so
    // dynamically spawned entities (FloorController.SpawnEnemyAt)
    // always have a valid Stats reference with no scene setup needed.
    public EntityStats Stats { get; private set; }

    // Up to 4 learned moves. Same auto-attach pattern as Stats.
    public MoveManager Moves { get; private set; }

    // The Sprite2D created below - kept so PlayHitFlash can tween its
    // Modulate without a GetNode lookup every hit.
    private Sprite2D _visual;

    // Native size of the generated fallback texture (see
    // SpriteTextureLibrary) and thus of the Sprite2D's rendered quad -
    // also drives the feet-anchor Offset below.
    private const float VisualSize = 28f;

    // Shared by MoveTo's move animation and PlayBumpAttack's nudge -
    // an entity only ever does one of those at a time, and Kill()ing
    // whichever is in flight before starting the other lets a second
    // action (e.g. a Speed=200 actor moving twice in one Tick) chain
    // smoothly from wherever the visual currently is instead of
    // fighting the previous tween or snapping.
    private Tween _visualMoveTween;

    private const double MoveAnimationDuration = 0.12;
    private const double BumpForwardDuration = 0.05;
    private const double BumpReturnDuration = 0.05;
    private const float BumpNudgeRatio = 0.4f;

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

        // Centered + an upward Offset puts the sprite's bottom edge (its
        // "feet") at this node's own origin instead of the sprite's
        // middle - required for Y-Sort to order overlapping characters
        // correctly (see DungeonScene.tscn/HubScene.tscn's
        // y_sort_enabled), since Y-Sort compares each node's origin, not
        // its visual bounds.
        //
        // VisualScale then scales the Sprite2D node itself, which scales
        // Offset right along with the texture (both are in the node's
        // own local space). Since Offset.y is exactly -halfHeight, the
        // bottom edge sits at local Y=0 BEFORE scaling - and scaling
        // around the node's own origin leaves a point already at 0
        // fixed, whatever the scale factor. So a bigger pal grows
        // upward from its feet instead of drifting off its tile, with
        // zero extra math: the feet anchor (and therefore Y-Sort) stays
        // correct at any VisualScale.
        _visual = new Sprite2D
        {
            Texture = SpriteTextureLibrary.GetTexture(SpriteId, FacingDirection, DebugColor, (int)VisualSize),
            Centered = true,
            Offset = new Vector2(0, -VisualSize / 2f),
            Scale = new Vector2(VisualScale, VisualScale),
        };
        AddChild(_visual);
    }

    // Re-resolves the Sprite2D's texture for the current FacingDirection
    // (see SpriteTextureLibrary's 3-tier fallback) - called only when
    // FacingDirection actually changes, so a run of steps in the same
    // direction doesn't redundantly re-lookup/re-cache every turn.
    private void UpdateSprite()
    {
        if (_visual == null) return;
        _visual.Texture = SpriteTextureLibrary.GetTexture(SpriteId, FacingDirection, DebugColor, (int)VisualSize);
    }

    // Recomputes FacingDirection from a move/attack target, normalizing
    // any adjacent-tile delta (including diagonals) to a unit Vector2I.
    // Must run BEFORE GridPosition is overwritten for a move (the delta
    // needs the pre-move position) - PlayBumpAttack never changes
    // GridPosition at all, so its call site isn't order-sensitive.
    private void UpdateFacingDirection(Vector2I targetPos)
    {
        var delta = targetPos - GridPosition;
        if (delta == Vector2I.Zero) return;

        var newDirection = new Vector2I(Math.Sign(delta.X), Math.Sign(delta.Y));
        if (newDirection == FacingDirection) return;

        FacingDirection = newDirection;
        UpdateSprite();
    }

    // Brief white flash on the entity's own visual, tweened back to its
    // normal tint - the "you got hit" feedback every attacker/thrown
    // item triggers on its target (see AttackAction/ThrowItemAction).
    // Sprite2D has no Color property (that was ColorRect-specific), so
    // this tweens Modulate instead.
    public void PlayHitFlash()
    {
        if (_visual == null) return;

        var original = _visual.Modulate;
        var tween = CreateTween();
        tween.TweenProperty(_visual, "modulate", Colors.White, 0.08);
        tween.TweenProperty(_visual, "modulate", original, 0.12);
    }

    // Floating "-N" damage number that rises and fades out - spawned as
    // a child so it inherits this entity's position, then frees itself
    // once the tween finishes.
    public void ShowDamagePopup(int amount)
    {
        var label = new Label
        {
            Text = $"-{amount}",
            Modulate = new Color(1f, 0.9f, 0.2f),
            Position = new Vector2(-8, -26),
            ZIndex = 100,
        };
        AddChild(label);

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(label, "position", label.Position + new Vector2(0, -20), 0.6);
        tween.TweenProperty(label, "modulate:a", 0f, 0.6);
        tween.Finished += () =>
        {
            if (GodotObject.IsInstanceValid(label)) label.QueueFree();
        };
    }

    // Instant placement (initial spawn, floor regeneration, forced
    // teleport) - deliberately NOT animated. There's no meaningful
    // "previous visual position" to interpolate from across a floor
    // reset, so this snaps the same way it always has; only MoveTo
    // (an actual step taken during play) animates.
    public void PlaceAt(Vector2I gridPos)
    {
        GridPosition = gridPos;
        _visualMoveTween?.Kill(); // don't let a stale tween fight this snap
        if (Grid != null) Position = Grid.GridToWorld(gridPos);
    }

    // Logical GridPosition/PreviousPosition update instantly - the turn
    // engine, AI, and occupancy checks all read GridPosition and must
    // see the new value immediately, before this call even returns.
    // Only the visual Position trails behind, via AnimateVisualTo.
    public void MoveTo(Vector2I targetPos)
    {
        UpdateFacingDirection(targetPos); // needs the pre-move GridPosition
        PreviousPosition = GridPosition;
        GridPosition = targetPos;
        AnimateVisualTo(targetPos, MoveAnimationDuration);
    }

    private void AnimateVisualTo(Vector2I gridPos, double duration)
    {
        if (Grid == null) return;

        _visualMoveTween?.Kill();
        _visualMoveTween = CreateTween();
        _visualMoveTween.TweenProperty(this, "position", Grid.GridToWorld(gridPos), duration);
    }

    // "Body slam" bump animation for an adjacent attack: nudge partway
    // toward the target and back, purely cosmetic (GridPosition never
    // changes for an attack). `home` is derived from the current
    // GridPosition rather than whatever Position happens to be right
    // now, so this always returns to the mathematically correct resting
    // spot even if a previous animation was interrupted mid-flight.
    public void PlayBumpAttack(Vector2I towardGridPos)
    {
        UpdateFacingDirection(towardGridPos); // GridPosition never changes for an attack, so order doesn't matter here

        if (Grid == null) return;

        var home = Grid.GridToWorld(GridPosition);
        var nudge = home.Lerp(Grid.GridToWorld(towardGridPos), BumpNudgeRatio);

        _visualMoveTween?.Kill();
        _visualMoveTween = CreateTween();
        _visualMoveTween.TweenProperty(this, "position", nudge, BumpForwardDuration);
        _visualMoveTween.TweenProperty(this, "position", home, BumpReturnDuration);
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
