using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;

namespace MysteryDungeon.Turn;

// Consumes one unit of a carried item and applies its effect to the
// user (Hp/Belly restore). Player-only for now, since InventoryManager
// is only ever attached to Player.
public class UseItemAction : IAction
{
    public ITurnActor Actor { get; }

    private readonly Player _user;
    private readonly string _itemId;

    public UseItemAction(Player user, string itemId)
    {
        Actor = user;
        _user = user;
        _itemId = itemId;
    }

    public void Execute(int turnNumber)
    {
        var data = ItemDatabase.Get(_itemId);
        if (data == null || !_user.Inventory.HasItem(_itemId))
        {
            GD.Print($"[Item] {_user.ActorName} has no {(data?.Name ?? _itemId)} to use.");
            return;
        }

        // Only consume the item when it actually does something - trying
        // to "use" a Throwable (or any effect-less item) directly must
        // not silently burn a unit of it.
        switch (data.EffectTarget)
        {
            case ItemEffectTarget.Hp:
                _user.Stats.Heal(data.EffectValue);
                GD.Print($"[Item] {_user.ActorName} used {data.Name}. HP +{data.EffectValue} ({_user.Stats.CurrentHp}/{_user.Stats.MaxHp})");
                _user.Inventory.RemoveItem(_itemId);
                break;
            case ItemEffectTarget.Belly:
                _user.Stats.Belly = Mathf.Min(_user.Stats.MaxBelly, _user.Stats.Belly + data.EffectValue);
                GD.Print($"[Item] {_user.ActorName} used {data.Name}. Belly +{data.EffectValue} ({_user.Stats.Belly}/{_user.Stats.MaxBelly})");
                _user.Inventory.RemoveItem(_itemId);
                break;
            default:
                GD.Print($"[Item] {data.Name} has no effect when used directly. It was not consumed.");
                break;
        }
    }
}
