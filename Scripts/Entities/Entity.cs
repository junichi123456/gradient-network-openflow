using System;
using Godot;
using MysteryDungeon.Grid;
using MysteryDungeon.Species;
using MysteryDungeon.Turn;
using MysteryDungeon.UI;
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

    // Phase 20: which SpeciesData to pull species constants from (see
    // SpeciesDatabase). A concrete entity sets this before base._Ready();
    // _Ready then fills SpriteId/VisualScale + Stats' Base*/BaseExpYield/
    // Types from the DB. Empty = "no species" (a bare Entity / a
    // hand-spawned test instance) - keeps its inline defaults, no DB
    // lookup.
    public string SpeciesId { get; set; } = "";

    // Normally derived from the species (SpriteKey) in _Ready. Left
    // public/settable for the rare non-species visual (Hub props set it
    // directly). See SpriteTextureLibrary for the texture resolution.
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

    // Phase 21: rank buffs/debuffs and ailments (poison/toxic/burn/
    // paralyze/freeze/stun). Same auto-attach pattern as Stats/Moves.
    public StatusEffectManager StatusEffects { get; private set; }

    // The Sprite2D created below - kept so PlayHitFlash can tween its
    // Modulate without a GetNode lookup every hit.
    private Sprite2D _visual;

    // Native size of the generated fallback texture (see
    // SpriteTextureLibrary) and thus of the Sprite2D's rendered quad -
    // also drives the feet-anchor Offset below.
    private const float VisualSize = 28f;

    // Matches GridManager.TileSize (32 in every scene in this project).
    // Grid isn't assigned yet when _Ready() runs - composition roots set
    // Entity.Grid right after AddChild(entity), and AddChild is exactly
    // what triggers _Ready() - so the feet-anchor math below can't read
    // Grid.TileSize directly at sprite-creation time.
    private const float TileWorldSize = 32f;

    // Shared by MoveTo's move animation and PlayBumpAttack's nudge -
    // an entity only ever does one of those at a time, and Kill()ing
    // whichever is in flight before starting the other lets a second
    // action (e.g. a Speed=200 actor moving twice in one Tick) chain
    // smoothly from wherever the visual currently is instead of
    // fighting the previous tween or snapping.
    private Tween _visualMoveTween;

    // 潜航's per-action-cycle accumulation drain while standing in Water
    // (Data/ecology.json: "水源にいる間、状態異常の蓄積値が毎ターン-100").
    private const int WaterAccumulationDecayPerTurn = 100;

    // オーバーヒール: extra 6% of MaxHp on top of any move-driven heal.
    private const float OverHealBonusRate = 0.06f;

    // こんとんのしゅくふく: HP paid per action for the party-wide Atk aura.
    private const float KontonHpCostRate = 0.15f;

    // Lava terrain damage per action-cycle, as a fraction of MaxHp, for a
    // Pal standing on lava without the aptitude for it.
    private const float LavaDamageRate = 0.20f;

    private const double MoveAnimationDuration = 0.12;
    private const double BumpForwardDuration = 0.05;
    private const double BumpReturnDuration = 0.05;
    private const float BumpNudgeRatio = 0.4f;

    public override void _Ready()
    {
        // Phase 20: resolve species constants up front. The visual half
        // (SpriteId/VisualScale) has to be set BEFORE the Sprite2D is
        // built at the bottom of this method; the stat half is applied
        // right after Stats is created below. Require() fails loud on an
        // undefined SpeciesId (a spawn-wiring bug), rather than letting a
        // zero-stat entity spawn silently.
        SpeciesData species = null;
        if (!string.IsNullOrEmpty(SpeciesId))
        {
            species = SpeciesDatabase.Instance.Require(SpeciesId);
            SpriteId = species.SpriteKey;
            VisualScale = species.DefaultVisualScale;
        }

        Stats = GetNodeOrNull<EntityStats>("Stats");
        if (Stats == null)
        {
            Stats = new EntityStats { Name = "Stats" };
            AddChild(Stats);
        }

        // Species constants land before the subclass sets its own spawn
        // Level (which happens after base._Ready returns), so the Level
        // setter's CurrentHp sync still runs last and lands on the right
        // MaxHp.
        if (species != null)
        {
            Stats.SpeciesId = SpeciesId;
            Stats.FillFromSpecies(species);
        }

        Moves = GetNodeOrNull<MoveManager>("Moves");
        if (Moves == null)
        {
            Moves = new MoveManager { Name = "Moves" };
            AddChild(Moves);
        }

        StatusEffects = GetNodeOrNull<StatusEffectManager>("StatusEffects");
        if (StatusEffects == null)
        {
            StatusEffects = new StatusEffectManager { Name = "StatusEffects" };
            AddChild(StatusEffects);
        }

        // trait_catalog_v2 stage 1: バッテリー permanently occupies the
        // Ailment slot (帯電/Paralyze) via the mutual-exclusion rule, with
        // Paralyze's own movement-lock effect suppressed for its holder -
        // see StatusEffectManager.MarkAsBattery.
        ApplyTraitFlags();

        // Centered + an upward Offset puts the sprite's bottom edge (its
        // "feet") at this node's own origin instead of the sprite's
        // middle - required for Y-Sort to order overlapping characters
        // correctly (see DungeonScene.tscn/HubScene.tscn's
        // y_sort_enabled), since Y-Sort compares each node's origin, not
        // its visual bounds. Offset.y must be -halfHeight of the ACTUAL
        // loaded texture, not the VisualSize placeholder constant - a
        // real (non-square, non-28px) sprite sized off VisualSize would
        // put its bottom edge well away from local Y=0, which is exactly
        // what made a real 960x800 asset appear to float with its
        // vertical center (not its feet) sitting on the tile.
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
            Scale = new Vector2(VisualScale, VisualScale),
        };
        _visual.Offset = ComputeFeetOffset(_visual.Texture);
        AddChild(_visual);
    }

    // Latches the permanent, trait-driven flags onto StatusEffects. These
    // are resolved once from Stats.Trait rather than re-checked per event,
    // because StatusEffectManager owns no back-reference to its Stats.
    //
    // Called from _Ready() after FillFromSpecies has populated Stats.Trait.
    // Public and idempotent so anything that assigns Trait AFTER spawn
    // (test harnesses, a future trait-swap item) can re-latch instead of
    // silently ending up with a trait whose flags never fired - the failure
    // mode is invisible, since the trait id itself still reads correctly.
    public void ApplyTraitFlags()
    {
        // バッテリー permanently occupies the Ailment slot (帯電/Paralyze)
        // with Paralyze's own movement-lock suppressed for its holder -
        // see StatusEffectManager.MarkAsBattery.
        if (Stats.Trait == "battery")
            StatusEffects.MarkAsBattery();

        if (Stats.Trait == "blitz_beat")
            StatusEffects.MarkBlocksEvasionDrop();

        if (Stats.Trait == "over_heal")
            StatusEffects.MarkHealBonusRate(OverHealBonusRate);
    }

    // Bottom edge at local Y=0 (see _Ready()'s comment) is "feet at this
    // tile's CENTER" - GridToWorld() places Position there, not at the
    // tile's bottom edge. That leaves a half-tile gap of visible floor
    // between a character's feet and, say, a wall tile directly below
    // it. Nudging the offset down by half a tile (converted into the
    // sprite's own pre-Scale local space, so it still lands as exactly
    // half a tile in world space after Scale is applied) moves the feet
    // to the tile's bottom edge instead, closing that gap - purely
    // cosmetic, GridPosition/Position (and therefore Y-Sort/occupancy)
    // are untouched.
    private Vector2 ComputeFeetOffset(Texture2D texture)
    {
        float scale = Mathf.Max(VisualScale, 0.0001f); // guard against a stray 0 producing Infinity/NaN
        float halfTileInLocalSpace = (TileWorldSize / 2f) / scale;
        return new Vector2(0, -texture.GetHeight() / 2f + halfTileInLocalSpace);
    }

    // Re-resolves the Sprite2D's texture for the current FacingDirection
    // (see SpriteTextureLibrary's 3-tier fallback) - called only when
    // FacingDirection actually changes, so a run of steps in the same
    // direction doesn't redundantly re-lookup/re-cache every turn.
    // Offset is recomputed too in case a differently-sized image loads
    // for the new direction (shouldn't happen for one species' own art,
    // but defensive against a mismatched drop-in).
    private void UpdateSprite()
    {
        if (_visual == null) return;
        _visual.Texture = SpriteTextureLibrary.GetTexture(SpriteId, FacingDirection, DebugColor, (int)VisualSize);
        _visual.Offset = ComputeFeetOffset(_visual.Texture);
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

    // Floating "+N EXP" number, the gain-side sibling of
    // ShowDamagePopup - green instead of yellow, and spawned slightly
    // lower so a simultaneous damage popup on the same entity (hit
    // then leveled from a party kill the same turn) doesn't overlap.
    // Same fire-and-forget tween contract: purely visual, no await in
    // the turn loop.
    public void ShowExpPopup(long amount)
    {
        var label = new Label
        {
            Text = $"+{amount} EXP",
            Modulate = new Color(0.45f, 1f, 0.5f),
            Position = new Vector2(-14, -12),
            ZIndex = 100,
        };
        AddChild(label);

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(label, "position", label.Position + new Vector2(0, -20), 0.7);
        tween.TweenProperty(label, "modulate:a", 0f, 0.7);
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

    // Phase 21 before-action hook: TurnScheduler (for AI) and Player's
    // input handler (for the player) call this before deciding/executing
    // an action each action-cycle. True = Frozen or Stunned - the whole
    // cycle is skipped (no DecideAction/Execute call at all this cycle).
    // Paralyze never triggers this (it only blocks movement, not the
    // action itself - see IsMovementBlocked).
    public bool IsActionLocked() => StatusEffects.TryConsumeActionLock();

    // Paralyze-only check: callers use this to swap a chosen MoveAction/
    // SwapAction for a WaitAction while leaving AttackAction untouched
    // (see FilterActionForStatus, TurnScheduler.Tick, TurnManager.
    // SubmitPlayerAction).
    public bool IsMovementBlocked => StatusEffects.IsMovementLocked;

    // ITurnActor.FilterActionForStatus: the single place Paralyze's
    // movement lock actually takes effect, shared by both the AI path
    // (TurnScheduler.Tick, after DecideAction()) and the player path
    // (TurnManager.SubmitPlayerAction, on whatever action Player._Ready's
    // input handler already built) - neither call site needs its own
    // paralyze-awareness.
    public IAction FilterActionForStatus(IAction action)
    {
        if (IsMovementBlocked && (action is MoveAction || action is SwapAction))
            return new WaitAction(this);
        return action;
    }

    // Phase 21 after-action hook: called once per action-cycle for every
    // entity, whether or not that cycle's action was actually skipped by
    // IsActionLocked() (see StatusEffectManager.AdvanceTurn - a Stunned-
    // and-Poisoned entity still takes poison damage on a skipped cycle).
    // Applies DoT damage (never below 1 HP, matching the project's
    // poison-can't-kill convention) with the same visual/log feedback
    // AttackAction gives a normal hit, and advances Paralyze's clear
    // check + rank decay internally.
    public void ResolveStatusTick()
    {
        // 潜航 (trait_catalog_v2 §6 stage 4): standing in Water drains this
        // entity's accumulation trackers each action-cycle. Applied BEFORE
        // AdvanceTurn so a tracker drained to 0 this cycle can't also be
        // pushed over the threshold by the same cycle's other effects.
        // Terrain is read here rather than inside StatusEffectManager,
        // which owns no grid reference by design.
        if (Grid != null
            && Stats.HasEcologyHook("water_accumulation_decay")
            && Grid.InBounds(GridPosition)
            && Grid.GetTile(GridPosition).Terrain == TerrainType.Water)
            StatusEffects.DecayAccumulation(WaterAccumulationDecayPerTurn);

        // Lava terrain damage (地形ダメージ): a Pal standing on Lava without
        // the aptitude for it takes 20% of MaxHp per action-cycle and is set
        // ablaze. This is the effect ecology グライド's terrain_damage_immune
        // hook was declared for in stage 4 but had nothing to attach to -
        // it is now live, and that hook exempts its holders.
        //
        // "適正のない" is read as Stats.CanTraverse(Lava) being false, which
        // already unifies every source of lava aptitude (Fire typing,
        // PartnerSkill Hover/Glide, ecology 飛行/放熱器官) behind one check.
        // Normal movement cannot step onto lava without that aptitude, so in
        // practice this fires on forced relocation onto it.
        //
        // Unlike DoT this is NOT clamped away from 0: it is terrain, not a
        // status ailment, and the spec gives no survival guarantee.
        if (Grid != null && Stats.IsAlive
            && Grid.InBounds(GridPosition)
            && Grid.GetTile(GridPosition).Terrain == TerrainType.Lava
            && !Stats.CanTraverse(TerrainType.Lava)
            && !Stats.HasEcologyHook("terrain_damage_immune"))
        {
            int burn = Mathf.Max(1, Mathf.FloorToInt(Stats.MaxHp * LavaDamageRate));
            Stats.TakeDamage(burn);
            StatusEffects.TryApplyAilment(Combat.AilmentType.Burn);
            PlayHitFlash();
            ShowDamagePopup(burn);
            MessageLogger.Log($"{ActorName} is scorched by the lava! ({burn} damage)", MessageLogger.IneffectiveColor);
            if (!Stats.IsAlive)
            {
                Die();
                return;
            }
        }

        // こんとんのしゅくふく (stage 9 §1): the holder pays 15% of MaxHp per
        // kill outright, matching the project's standing convention that
        // per-turn self-damage (poison/toxic/burn) leaves the holder at 1.
        if (Stats.Trait == "konton_no_shukufuku" && Stats.IsAlive)
        {
            int cost = Mathf.Max(1, Mathf.FloorToInt(Stats.MaxHp * KontonHpCostRate));
            int hpBeforeCost = Stats.CurrentHp;
            Stats.CurrentHp = Mathf.Max(1, Stats.CurrentHp - cost);
            int paid = hpBeforeCost - Stats.CurrentHp;
            if (paid > 0)
            {
                PlayHitFlash();
                ShowDamagePopup(paid);
                MessageLogger.Log($"{ActorName} pays {paid} HP to sustain the blessing of chaos.", MessageLogger.IneffectiveColor);
            }
        }

        int damage = StatusEffects.AdvanceTurn(Stats.MaxHp);
        if (damage <= 0) return;

        int before = Stats.CurrentHp;
        Stats.CurrentHp = Mathf.Max(1, Stats.CurrentHp - damage);
        int actualLoss = before - Stats.CurrentHp;
        if (actualLoss <= 0) return;

        PlayHitFlash();
        ShowDamagePopup(actualLoss);
        MessageLogger.Log($"{ActorName} is hurt by {StatusEffects.Ailment}! ({actualLoss} damage)", MessageLogger.IneffectiveColor);
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
