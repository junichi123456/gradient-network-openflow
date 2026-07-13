using System;
using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;
using MysteryDungeon.Dungeon;
using MysteryDungeon.UI;

namespace MysteryDungeon.Turn;

// Damage is computed by DamageCalculator (Phase 16's DamageContext
// pipeline) - see that class for the formula itself. TypeEffectiveness
// is still the real value from TypeChartManager (a system multiplier,
// not a "buff"). AtkMultiplier/DefMultiplier/PowerMultiplier are now fed
// by Phase 21's rank system (StatusEffectManager); the remaining buff
// fields (AtkFlatBuff/PowerFlatBuff/DefFlatBuff/ElementResistCut/
// PartyElementCut) stay at DamageContext's own defaults - no skill
// database exists yet to source those specific values from.
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

        // Phase 21: IsGuaranteedHit ("必中") bypasses the roll AND the
        // defender's evasion rank entirely; otherwise both the
        // attacker's accuracy rank and the defender's evasion rank
        // multiply onto the move's base Accuracy before the roll.
        bool hits = move.IsGuaranteedHit
            || GD.Randf() * 100f < move.Accuracy * _attacker.StatusEffects.GetAccuracyMultiplier() * _defender.StatusEffects.GetEvasionMultiplier();

        if (!hits)
        {
            MessageLogger.Log($"{_attacker.ActorName} used {move.Name} on {_defender.ActorName}! It missed!", MessageLogger.IneffectiveColor);
            return;
        }

        // Phase 21: a pure Status-category move (e.g. poison_fog) never
        // deals damage - it only ever carries a RankEffect and/or an
        // AilmentEffect, applied below and shared with the secondary-
        // effect path damaging moves use further down.
        if (move.Category == MoveCategory.Status)
        {
            MessageLogger.Log($"{_attacker.ActorName} used {move.Name}!");
            ApplyRankEffectIfAny(move, defenderAlive: true);
            ApplyAilmentEffectIfAny(move, defenderAlive: true);
            return;
        }

        var attackerStats = _attacker.Stats;
        var defenderStats = _defender.Stats;

        float typeMultiplier = TypeChartManager.GetMultiplier(move.Type, defenderStats.Type1, defenderStats.Type2);

        // Crit roll (after the hit is confirmed - a miss can't crit).
        // rank 5 gives chance 1.0, and GD.Randf() is [0,1), so "< 1.0"
        // is always true there = guaranteed crit.
        bool isCrit = GD.Randf() < _attacker.StatusEffects.GetCritChance();

        // The three rank multipliers. On a crit, each is clamped to
        // "at least neutral for the attacker" so DISADVANTAGEOUS rank
        // corrections are ignored (treated as 1.0), while advantageous
        // ones still apply (confirmed rule): Atk/Power keep their upside
        // (Max with 1.0), Def keeps its downside for the attacker (Min
        // with 1.0, since a lower defense multiplier means more damage).
        float atkMul = _attacker.StatusEffects.GetAtkMultiplier();
        float defMul = _defender.StatusEffects.GetDefMultiplier();
        float powerMul = _attacker.StatusEffects.GetElementPowerMultiplier(move.Type);
        if (isCrit)
        {
            atkMul = Mathf.Max(1f, atkMul);
            defMul = Mathf.Min(1f, defMul);
            powerMul = Mathf.Max(1f, powerMul);
        }

        var damageContext = new DamageContext
        {
            BaseAtk = attackerStats.Attack,
            BaseDef = defenderStats.Defense,
            BasePower = move.Power,
            AttackElement = move.Type,
            DefenderElement = defenderStats.Type1,
            TypeEffectiveness = typeMultiplier,
            AtkMultiplier = atkMul,
            DefMultiplier = defMul,
            PowerMultiplier = powerMul,
            CritMultiplier = isCrit ? 1.5f : 1.0f,
            // AtkFlatBuff/PowerFlatBuff/DefFlatBuff/ElementResistCut/
            // PartyElementCut stay at DamageContext's own defaults - no
            // skill database exists yet to source those from.
        };

        int damage = DamageCalculator.Calculate(damageContext);

        // Burn's contact-damage penalty ("接触技の与ダメージ*50%",
        // confirmed as a x0.5 output halving): applied as a flat post-hoc
        // adjustment to the final integer damage, outside DamageCalculator
        // entirely (Phase 16's pipeline stays untouched) - this is a
        // damage-OUTPUT penalty, not a stat-based modifier like the rank
        // multipliers above. IsContact defaults false, so none of the
        // current 97 (all ranged) moves trigger this yet.
        if (_attacker.StatusEffects.Ailment == AilmentType.Burn && move.IsContact)
            damage = Mathf.Max(1, Mathf.FloorToInt(damage * 0.5f));

        defenderStats.TakeDamage(damage);

        // Rank decay's "10 turns since last damage" clock resets on
        // REAL damage only (dealt or received) - confirmed explicitly
        // NOT to include DoT ticks (see Entity.ResolveStatusTick, which
        // never touches this).
        _attacker.StatusEffects.ResetDamageTimer();
        _defender.StatusEffects.ResetDamageTimer();

        _defender.PlayHitFlash();
        _defender.ShowDamagePopup(damage);
        MessageLogger.Log($"{_attacker.ActorName} used {move.Name} on {_defender.ActorName}! It hit for {damage} damage.");

        if (isCrit)
            MessageLogger.Log("A critical hit!", MessageLogger.EffectiveColor);

        if (typeMultiplier > 1f)
            MessageLogger.Log("It's super effective!", MessageLogger.EffectiveColor);
        else if (typeMultiplier < 1f)
            MessageLogger.Log("It's not very effective...", MessageLogger.IneffectiveColor);

        bool defenderAliveAfterDamage = defenderStats.IsAlive;

        // Phase 21 secondary effects: a damaging move can ALSO carry a
        // RankEffect/AilmentEffect (e.g. a future "10% chance to poison"
        // move) - applied after damage resolves. Guarded by
        // defenderAliveAfterDamage so a defeated target isn't afflicted
        // post-mortem; a Self-targeted effect always applies regardless
        // (the attacker is still alive here by construction). None of
        // the current 97 moves set either field, so this is inert today.
        ApplyRankEffectIfAny(move, defenderAliveAfterDamage);
        ApplyAilmentEffectIfAny(move, defenderAliveAfterDamage);

        if (!defenderAliveAfterDamage)
        {
            MessageLogger.Log($"{_defender.ActorName} fainted!", MessageLogger.FaintColor);

            // Phase 18-A defeat detection point - fired before Die() so
            // the victim node is still fully readable. EXP distribution
            // hangs off this notification (see ExperienceSystem).
            _floorController?.Experience?.NotifyDefeated(_defender, _attacker);

            // EXP is handled by NotifyDefeated above (Phase 18-A: full
            // amount to every living party member, PMD-style) - only
            // kill-tracking and drops remain faction-gated here.
            if (_attacker.Faction == Faction.Player && _defender.Faction == Faction.Enemy)
            {
                _floorController?.RunTracker.RecordKill(_defender.ActorName);
                MaterialDropTable.TryDrop(_floorController, _defender.GridPosition, _defender.ActorName);
            }

            _defender.Die();
        }
    }

    // Shared by the pure-Status branch and a damaging move's optional
    // secondary effect. defenderAlive gates Enemy-targeted effects only -
    // a Self-targeted effect (e.g. the attacker powering up) still lands
    // even if the defender was just defeated.
    private void ApplyRankEffectIfAny(MoveData move, bool defenderAlive)
    {
        if (move.RankEffectStat == RankStat.None || move.RankEffectDelta == 0) return;
        if (move.RankEffectTarget == StatusTarget.Enemy && !defenderAlive) return;

        var target = move.RankEffectTarget == StatusTarget.Self ? _attacker : _defender;
        var moveElement = Enum.TryParse<Element>(move.Type, out var parsed) ? parsed : Element.Neutral;
        target.StatusEffects.ApplyRankDelta(move.RankEffectStat, move.RankEffectDelta, moveElement);

        string direction = move.RankEffectDelta > 0 ? "rose" : "fell";
        MessageLogger.Log($"{target.ActorName}'s {move.RankEffectStat} {direction}!", MessageLogger.NeutralColor);
    }

    private void ApplyAilmentEffectIfAny(MoveData move, bool defenderAlive)
    {
        if (move.AilmentEffect == AilmentType.None) return;
        if (move.AilmentTarget == StatusTarget.Enemy && !defenderAlive) return;
        if (GD.Randf() * 100f >= move.AilmentChance) return; // chance roll failed - silently no effect

        var target = move.AilmentTarget == StatusTarget.Self ? _attacker : _defender;
        if (target.StatusEffects.TryApplyAilment(move.AilmentEffect))
            MessageLogger.Log($"{target.ActorName} was afflicted with {move.AilmentEffect}!", MessageLogger.IneffectiveColor);
        else
            MessageLogger.Log($"{target.ActorName} is unaffected - already under a status condition.", MessageLogger.NeutralColor);
    }
}
