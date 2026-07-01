using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;

namespace MysteryDungeon.Turn;

// Palworld-style damage formula (per FightingBread's v0.7.0 community
// datamining):
//
//   Damage = 0.8 * sqrt(Level + 1) * (Attack / Defense) * Power
//            * STAB * TypeMultiplier * Random(0.9-1.1)
//
// STAB (Same Type Attack Bonus, x1.2) applies when the move's type
// matches either of the attacker's own types. Applies to both Player
// and AI attackers/defenders uniformly.
public class AttackAction : IAction
{
    private const float BaseCoefficient = 0.8f;
    private const float StabMultiplier = 1.2f;
    private const float RandomMin = 0.9f;
    private const float RandomMax = 1.1f;

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

        var attackerStats = _attacker.Stats;
        var defenderStats = _defender.Stats;

        float typeMultiplier = TypeChartManager.GetMultiplier(move.Type, defenderStats.Type1, defenderStats.Type2);
        bool hasStab = move.Type == attackerStats.Type1 || move.Type == attackerStats.Type2;
        float stab = hasStab ? StabMultiplier : 1f;
        float randomRoll = (float)GD.RandRange(RandomMin, RandomMax);

        float rawDamage = BaseCoefficient
            * Mathf.Sqrt(attackerStats.Level + 1)
            * ((float)attackerStats.Attack / Mathf.Max(1, defenderStats.Defense))
            * move.Power
            * stab
            * typeMultiplier
            * randomRoll;

        int damage = Mathf.Max(1, Mathf.RoundToInt(rawDamage));

        defenderStats.TakeDamage(damage);
        GD.Print($"[Combat] {_attacker.ActorName} used {move.Name} on {_defender.ActorName}! It hit for {damage} damage.");

        if (typeMultiplier > 1f)
            GD.Print("[Combat] It's super effective!");
        else if (typeMultiplier < 1f)
            GD.Print("[Combat] It's not very effective...");

        if (!defenderStats.IsAlive)
        {
            GD.Print($"[Combat] {_defender.ActorName} fainted!");
            _defender.Die();
        }
    }
}
