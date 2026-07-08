using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;
using MysteryDungeon.Dungeon;
using MysteryDungeon.UI;

namespace MysteryDungeon.Turn;

// Damage is computed by DamageCalculator (Phase 16's DamageContext
// pipeline) - see that class for the formula itself. TypeEffectiveness
// is still the real value from TypeChartManager (a system multiplier,
// not a "buff"); every buff field on the DamageContext stays at its
// default (no skill database exists yet to source real values from),
// so today's damage is exactly DamageCalculator's benchmark case scaled
// by type effectiveness alone.
public class AttackAction : IAction
{
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
            MessageLogger.Log($"{_attacker.ActorName} tried to use {move.Name}, but it has no PP left!", MessageLogger.IneffectiveColor);
            return;
        }

        _moveSlot.CurrentPp--;

        // Menu-invoked moves have no manual target (Phase 6: no
        // direction-picker for moves) - autoaim may still come up empty,
        // in which case the move just swings at nothing but still costs
        // the turn/PP, same as a normal miss.
        if (_defender == null)
        {
            MessageLogger.Log($"{_attacker.ActorName} used {move.Name}, but there was no target! It hit nothing but air.", MessageLogger.IneffectiveColor);
            return;
        }

        // Bump animation plays on every attempted attack (hit or miss) -
        // only "no PP"/"no target" above skip it, since nothing actually
        // happens in those cases.
        _attacker.PlayBumpAttack(_defender.GridPosition);

        // Accuracy>=100 skips the roll entirely - GD.Randf()'s [0,1]
        // range can return exactly 1.0, which would otherwise let a
        // "guaranteed hit" move miss on a razor-thin edge case.
        if (move.Accuracy < 100 && GD.Randf() * 100f >= move.Accuracy)
        {
            MessageLogger.Log($"{_attacker.ActorName} used {move.Name} on {_defender.ActorName}! It missed!", MessageLogger.IneffectiveColor);
            return;
        }

        var attackerStats = _attacker.Stats;
        var defenderStats = _defender.Stats;

        float typeMultiplier = TypeChartManager.GetMultiplier(move.Type, defenderStats.Type1, defenderStats.Type2);

        var damageContext = new DamageContext
        {
            BaseAtk = attackerStats.Attack,
            BaseDef = defenderStats.Defense,
            BasePower = move.Power,
            AttackElement = move.Type,
            DefenderElement = defenderStats.Type1,
            TypeEffectiveness = typeMultiplier,
            // AtkFlatBuff/AtkMultiplier/PowerFlatBuff/PowerMultiplier/
            // DefFlatBuff/DefMultiplier/ElementResistCut/PartyElementCut
            // all stay at DamageContext's own defaults (0 / 1.0) - no
            // skill database exists yet to source real buff values from.
        };

        int damage = DamageCalculator.Calculate(damageContext);

        defenderStats.TakeDamage(damage);
        _defender.PlayHitFlash();
        _defender.ShowDamagePopup(damage);
        MessageLogger.Log($"{_attacker.ActorName} used {move.Name} on {_defender.ActorName}! It hit for {damage} damage.");

        if (typeMultiplier > 1f)
            MessageLogger.Log("It's super effective!", MessageLogger.EffectiveColor);
        else if (typeMultiplier < 1f)
            MessageLogger.Log("It's not very effective...", MessageLogger.IneffectiveColor);

        if (!defenderStats.IsAlive)
        {
            MessageLogger.Log($"{_defender.ActorName} fainted!", MessageLogger.FaintColor);

            if (_attacker.Faction == Faction.Player && _defender.Faction == Faction.Enemy)
            {
                _floorController?.RunTracker.RecordKill(_defender.ActorName);

                if (_attacker is Player)
                {
                    int expGained = defenderStats.Level * 10;
                    MessageLogger.Log($"{_attacker.ActorName} gained {expGained} EXP for defeating {_defender.ActorName}.", MessageLogger.ProgressionColor);
                    attackerStats.AddExp(expGained);
                }

                MaterialDropTable.TryDrop(_floorController, _defender.GridPosition, _defender.ActorName);
            }

            _defender.Die();
        }
    }
}
