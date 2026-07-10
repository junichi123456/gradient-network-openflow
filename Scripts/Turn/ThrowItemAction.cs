using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;
using MysteryDungeon.Grid;
using MysteryDungeon.Dungeon;
using MysteryDungeon.UI;

namespace MysteryDungeon.Turn;

// Traces a straight line up to MaxRange tiles from the thrower in
// `direction`. Water/Lava/Chasm are not walkable but are still
// projectile-passable (GridManager.IsProjectilePassable), so the item
// flies over them; only a Wall stops it, one tile short of the wall. If
// `direction` is diagonal, each step also has to clear
// GridManager.CanCutCorner - a Wall on either shoulder stops the flight
// the same way a Wall dead ahead would (Water/Lava/Chasm shoulders never
// block it). A trap tile along the path is triggered (log only - no
// trap-effect system exists yet). Hitting an entity ends the flight
// immediately and deals a flat Item-power * TypeMultiplier hit,
// independent of the thrower's own stats; otherwise the item lands and
// FloorController handles the drop/scatter/destroy rules for wherever
// it stopped.
public class ThrowItemAction : IAction
{
    private const int MaxRange = 12;

    public ITurnActor Actor { get; }

    private readonly Player _thrower;
    private readonly Vector2I _direction;
    private readonly string _itemId;
    private readonly GridManager _grid;
    private readonly FloorController _floorController;

    public ThrowItemAction(Player thrower, Vector2I direction, string itemId, GridManager grid, FloorController floorController)
    {
        Actor = thrower;
        _thrower = thrower;
        _direction = direction;
        _itemId = itemId;
        _grid = grid;
        _floorController = floorController;
    }

    public void Execute(int turnNumber)
    {
        var data = ItemDatabase.Get(_itemId);
        if (data == null || !_thrower.Inventory.HasItem(_itemId))
        {
            MessageLogger.Log($"{_thrower.ActorName} has no {(data?.Name ?? _itemId)} to throw.", MessageLogger.IneffectiveColor);
            return;
        }

        _thrower.Inventory.RemoveItem(_itemId);

        bool isDiagonal = Mathf.Abs(_direction.X) == 1 && Mathf.Abs(_direction.Y) == 1;
        var current = _thrower.GridPosition;
        var landingPos = current;
        Entity hitEntity = null;

        for (int step = 1; step <= MaxRange; step++)
        {
            var candidate = current + _direction;

            if (!_grid.IsProjectilePassable(candidate))
                break; // stop one tile short of the wall - landingPos keeps the previous step

            if (isDiagonal && !_grid.CanCutCorner(current, candidate))
            {
                MessageLogger.Log($"{data.Name} was blocked by a wall at the corner and could not cut through!", MessageLogger.IneffectiveColor);
                break; // stop one tile short - a Wall shoulder blocks the diagonal cut
            }

            current = candidate;
            landingPos = current;

            if (_floorController.Objects.Get(current) == MapObjectType.Trap)
                MessageLogger.Log($"{data.Name} triggered a trap!", MessageLogger.FaintColor);

            hitEntity = _floorController.GetEnemyAt(current);
            if (hitEntity != null) break;
        }

        if (hitEntity != null)
        {
            float typeMultiplier = string.IsNullOrEmpty(data.ElementType)
                ? 1f
                : TypeChartManager.GetMultiplier(data.ElementType, hitEntity.Stats.Type1, hitEntity.Stats.Type2);

            int damage = Mathf.Max(1, Mathf.RoundToInt(data.EffectValue * typeMultiplier));
            hitEntity.Stats.TakeDamage(damage);
            hitEntity.PlayHitFlash();
            hitEntity.ShowDamagePopup(damage);
            MessageLogger.Log($"{_thrower.ActorName} threw {data.Name} and hit {hitEntity.ActorName} for {damage} damage.");

            if (typeMultiplier > 1f)
                MessageLogger.Log("It's super effective!", MessageLogger.EffectiveColor);
            else if (typeMultiplier < 1f)
                MessageLogger.Log("It's not very effective...", MessageLogger.IneffectiveColor);

            if (!hitEntity.Stats.IsAlive)
            {
                MessageLogger.Log($"{hitEntity.ActorName} fainted!", MessageLogger.FaintColor);

                // Phase 18-A defeat detection point (same contract as
                // AttackAction's - see ExperienceSystem.NotifyDefeated).
                _floorController?.Experience?.NotifyDefeated(hitEntity, _thrower);

                // Same kill bookkeeping as AttackAction: RunTracker +
                // drops here; EXP comes from NotifyDefeated above
                // (Phase 18-A party-wide distribution).
                if (_thrower.Faction == Faction.Player && hitEntity.Faction == Faction.Enemy)
                {
                    _floorController.RunTracker.RecordKill(hitEntity.ActorName);
                    MaterialDropTable.TryDrop(_floorController, hitEntity.GridPosition, hitEntity.ActorName);
                }

                hitEntity.Die();
            }
        }
        else
        {
            MessageLogger.Log($"{_thrower.ActorName} threw {data.Name}, but it flew off and landed somewhere.");
            _floorController.DropItem(landingPos, _itemId);
        }
    }
}
