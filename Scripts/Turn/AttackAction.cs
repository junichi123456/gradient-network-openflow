using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;
using MysteryDungeon.Dungeon;

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
    private readonly FloorController _floorController;

    // floorController is optional and only used to record a Player-side
    // kill into RunTracker (see the death branch below) - HostileEntity's
    // attacks on the player never need it, since their Faction check
    // fails regardless.
    public AttackAction(Entity attacker, Entity defender, MoveSlot moveSlot, FloorController floorController = null)
    {
        Actor = attacker;
        _attacker = attacker;
        _defender = defender;
        _moveSlot = moveSlot;
        _floorController = floorController;
    }

    public void Execute(int turnNumber)
    {
        var move = _moveSlot.Data;

        // Out of PP = the move simply fails (turn is still consumed).
        // AI never reaches this branch - GetFirstAutoUsableMove skips
        // empty slots - but the player can still manually pick one.
        if (_moveSlot.CurrentPp <= 0)
        {
            GD.Print($"[Combat] {_attacker.ActorName} tried to use {move.Name}, but it has no PP left!");
            return;
        }

        _moveSlot.CurrentPp--;

        // Menu-invoked moves have no manual target (Phase 6: no
        // direction-picker for moves) - autoaim may still come up empty,
        // in which case the move just swings at nothing but still costs
        // the turn/PP, same as a normal miss.
        if (_defender == null)
        {
            GD.Print($"[Combat] {_attacker.ActorName} used {move.Name}, but there was no target! It hit nothing but air.");
            return;
        }

        // Accuracy>=100 skips the roll entirely - GD.Randf()'s [0,1]
        // range can return exactly 1.0, which would otherwise let a
        // "guaranteed hit" move miss on a razor-thin edge case.
        if (move.Accuracy < 100 && GD.Randf() * 100f >= move.Accuracy)
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

            if (_attacker.Faction == Faction.Player && _defender.Faction == Faction.Enemy)
            {
                _floorController?.RunTracker.RecordKill(_defender.ActorName);

                if (_attacker is Player)
                {
                    int expGained = defenderStats.Level * 10;
                    GD.Print($"[Progression] {_attacker.ActorName} gained {expGained} EXP for defeating {_defender.ActorName}.");
                    attackerStats.AddExp(expGained);
                }
            }

            _defender.Die();
        }
    }
}
