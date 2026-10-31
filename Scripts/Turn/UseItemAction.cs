using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;
using MysteryDungeon.UI;

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
            MessageLogger.Log($"{_user.ActorName} has no {(data?.Name ?? _itemId)} to use.", MessageLogger.IneffectiveColor);
            return;
        }

        // Only consume the item when it actually does something - trying
        // to "use" a Throwable (or any effect-less item) directly must
        // not silently burn a unit of it.
        switch (data.EffectTarget)
        {
            case ItemEffectTarget.Hp:
                // VineBound (ツタまみれ) blocks ALL recovery paths, items
                // included (status-redesign §4-3) - the item is still
                // consumed (matches the existing "used at full HP still
                // consumes" precedent below), it just does nothing.
                if (_user.StatusEffects.IsVineBound)
                {
                    MessageLogger.Log($"{_user.ActorName} used {data.Name}, but the vines block all recovery!", MessageLogger.IneffectiveColor);
                }
                else
                {
                    _user.Stats.Heal(data.EffectValue);
                    MessageLogger.Log($"{_user.ActorName} used {data.Name}. HP +{data.EffectValue} ({_user.Stats.CurrentHp}/{_user.Stats.MaxHp})", MessageLogger.HealColor);
                }
                _user.Inventory.RemoveItem(_itemId);
                break;
            case ItemEffectTarget.Belly:
                _user.Stats.Belly = Mathf.Min(_user.Stats.MaxBelly, _user.Stats.Belly + data.EffectValue);
                MessageLogger.Log($"{_user.ActorName} used {data.Name}. Belly +{data.EffectValue} ({_user.Stats.Belly}/{_user.Stats.MaxBelly})", MessageLogger.HealColor);
                _user.Inventory.RemoveItem(_itemId);
                break;
            default:
                MessageLogger.Log($"{data.Name} has no effect when used directly. It was not consumed.", MessageLogger.IneffectiveColor);
                break;
        }
    }
}
