using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;

namespace MysteryDungeon.Turn;

// Simplified Phase 4 damage formula: base Power x type-effectiveness
// multiplier (no level/Attack-Defense curve yet - that arrives once a
// fuller combat formula is needed). Applies to both Player and AI
// attackers/defenders uniformly.
public class AttackAction : IAction
{
    public ITurnActor Actor { get; }

    private readonly Entity _attacker;
    private readonly Entity _defender;
    private readonly MoveSlot _moveSlot;

    public AttackAction(Entity attacker, Entity defender, MoveSlot moveSlot)
    {
        Actor = attacker;
        _attacker = attacker;
        _defender = defender;
        _moveSlot = moveSlot;
    }

    public void Execute(int turnNumber)
    {
        var move = _moveSlot.Data;
        if (_moveSlot.CurrentPp > 0) _moveSlot.CurrentPp--;

        if (GD.Randf() * 100f >= move.Accuracy)
        {
            GD.Print($"[Combat] {_attacker.ActorName} used {move.Name} on {_defender.ActorName}! It missed!");
            return;
        }

        float multiplier = TypeChartManager.GetMultiplier(move.Type, _defender.Stats.Type1, _defender.Stats.Type2);
        int damage = Mathf.Max(1, Mathf.RoundToInt(move.Power * multiplier));

        _defender.Stats.TakeDamage(damage);
        GD.Print($"[Combat] {_attacker.ActorName} used {move.Name} on {_defender.ActorName}! It hit for {damage} damage.");

        if (multiplier > 1f)
            GD.Print("[Combat] It's super effective!");
        else if (multiplier == 0f)
            GD.Print($"[Combat] It doesn't affect {_defender.ActorName}...");
        else if (multiplier < 1f)
            GD.Print("[Combat] It's not very effective...");

        if (!_defender.Stats.IsAlive)
        {
            GD.Print($"[Combat] {_defender.ActorName} fainted!");
            _defender.Die();
        }
    }
}
