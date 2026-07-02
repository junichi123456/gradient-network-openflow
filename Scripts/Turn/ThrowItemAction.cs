using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;
using MysteryDungeon.Grid;
using MysteryDungeon.Dungeon;

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
            GD.Print($"[Item] {_thrower.ActorName} has no {(data?.Name ?? _itemId)} to throw.");
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
                GD.Print($"[Item] {data.Name} was blocked by a wall at the corner near ({current.X}, {current.Y}) and could not cut through!");
                break; // stop one tile short - a Wall shoulder blocks the diagonal cut
            }

            current = candidate;
            landingPos = current;

            if (_floorController.Objects.Get(current) == MapObjectType.Trap)
                GD.Print($"[Item] {data.Name} triggered a trap at ({current.X}, {current.Y})!");

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
            GD.Print($"[Item] {_thrower.ActorName} threw {data.Name} and hit {hitEntity.ActorName} for {damage} damage.");

            if (typeMultiplier > 1f)
                GD.Print("[Item] It's super effective!");
            else if (typeMultiplier < 1f)
                GD.Print("[Item] It's not very effective...");

            if (!hitEntity.Stats.IsAlive)
            {
                GD.Print($"[Combat] {hitEntity.ActorName} fainted!");

                // Same kill bookkeeping as AttackAction: a thrown-item
                // kill counts for RunTracker recruitment and player EXP.
                if (_thrower.Faction == Faction.Player && hitEntity.Faction == Faction.Enemy)
                {
                    _floorController.RunTracker.RecordKill(hitEntity.ActorName);

                    int expGained = hitEntity.Stats.Level * 10;
                    GD.Print($"[Progression] {_thrower.ActorName} gained {expGained} EXP for defeating {hitEntity.ActorName}.");
                    _thrower.Stats.AddExp(expGained);
                }

                hitEntity.Die();
            }
        }
        else
        {
            GD.Print($"[Item] {_thrower.ActorName} threw {data.Name}, it flew to ({landingPos.X}, {landingPos.Y}) and landed.");
            _floorController.DropItem(landingPos, _itemId);
        }
    }
}
