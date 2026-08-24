using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Dungeon;
using MysteryDungeon.Entities;
using MysteryDungeon.UI;

namespace MysteryDungeon.Turn;

// Removes one unit of an item from the player's inventory and places it
// on the ground at their feet, reusing FloorController.DropItem's
// Phase 5 scatter/terrain-destruction rules.
public class DropItemAction : IAction
{
    public ITurnActor Actor { get; }

    private readonly Player _player;
    private readonly FloorController _floorController;
    private readonly string _itemId;

    public DropItemAction(Player player, FloorController floorController, string itemId)
    {
        Actor = player;
        _player = player;
        _floorController = floorController;
        _itemId = itemId;
    }

    public void Execute(int turnNumber)
    {
        var data = ItemDatabase.Get(_itemId);
        if (data == null || !_player.Inventory.RemoveItem(_itemId))
        {
            MessageLogger.Log($"{_player.ActorName} has no {(data?.Name ?? _itemId)} to drop.", MessageLogger.IneffectiveColor);
            return;
        }

        MessageLogger.Log($"{_player.ActorName} placed {data.Name} on the ground.");
        _floorController.DropItem(_player.GridPosition, _itemId);
    }
}
