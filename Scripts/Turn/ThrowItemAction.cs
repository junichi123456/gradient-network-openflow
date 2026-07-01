using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;
using MysteryDungeon.Grid;
using MysteryDungeon.Dungeon;

namespace MysteryDungeon.Turn;

// Traces a straight line up to MaxRange tiles from the thrower in
// `direction`. Water/Lava/Chasm are not walkable but are still
// projectile-passable (GridManager.IsProjectilePassable), so the item
// flies over them; only a Wall stops it, one tile short of the wall.
// A trap tile along the path is triggered (log only - no trap-effect
// system exists yet). Hitting an entity ends the flight immediately and
// deals a flat Item-power * TypeMultiplier hit, independent of the
// thrower's own stats; otherwise the item lands and FloorController
// handles the drop/scatter/destroy rules for wherever it stopped.
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

        var origin = _thrower.GridPosition;
        var landingPos = origin;
        Entity hitEntity = null;

        for (int step = 1; step <= MaxRange; step++)
        {
            var candidate = origin + _direction * step;

            if (!_grid.IsProjectilePassable(candidate))
                break; // stop one tile short of the wall - landingPos keeps the previous step

            landingPos = candidate;

            if (_floorController.Objects.Get(candidate) == MapObjectType.Trap)
                GD.Print($"[Item] {data.Name} triggered a trap at ({candidate.X}, {candidate.Y})!");

            hitEntity = _floorController.GetEnemyAt(candidate);
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
