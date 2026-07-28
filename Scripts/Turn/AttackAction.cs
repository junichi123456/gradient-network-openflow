using System;
using System.Collections.Generic;
using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;
using MysteryDungeon.Dungeon;
using MysteryDungeon.UI;

namespace MysteryDungeon.Turn;

// Damage is computed by DamageCalculator (Phase 16's DamageContext
// pipeline) - see that class for the formula itself. TypeEffectiveness
// is still the real value from TypeChartManager (a system multiplier,
// not a "buff"). AtkMultiplier/DefMultiplier/PowerMultiplier are fed by
// Phase 21's rank system; crit and the 300-move mechanics (recoil, self-
// stun, AoE ranges) layer on top.
//
// Range dispatch: Adjacent stays the single-target/bump path (unchanged
// - regression 0). Every other range routes through the AoE path, which
// resolves a target list (TargetResolver) and applies the SAME per-
// target strike to each, with friendly-fire (see the move-consumption
// proposal §4).
public class AttackAction : IAction
{
    public ITurnActor Actor { get; }

    private readonly Entity _attacker;
    private readonly Entity _defender;
    private readonly MoveSlot _moveSlot;
    private readonly FloorController _floorController;

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

        // Out of PP = the move simply fails (turn still consumed).
        if (_moveSlot.CurrentPp <= 0)
        {
            MessageLogger.Log($"{_attacker.ActorName} tried to use {move.Name}, but it has no PP left!", MessageLogger.IneffectiveColor);
            return;
        }

        _moveSlot.CurrentPp--;

        // Self-stun (大技の隙): applied at USE time, regardless of hit or
        // miss (§9-4). Reuses Phase 21's Stun (consumed at the start of
        // the attacker's next action-cycle).
        if (move.SelfStunNextTurn)
        {
            _attacker.StatusEffects.TryApplyAilment(AilmentType.Stun);
            MessageLogger.Log($"{_attacker.ActorName} must recharge after {move.Name}!", MessageLogger.IneffectiveColor);
        }

        // AoE needs the floor (actor enumeration) and the grid; without
        // them (shouldn't happen in-dungeon) fall back to the single path.
        bool canAoe = move.Range != MoveRange.Adjacent && _floorController != null && _attacker.Grid != null;
        if (canAoe)
            ExecuteAoe(move);
        else
            ExecuteSingle(move);

        // Self-destruct (メガトン自爆): the user faints once the move has
        // fully resolved, hit or miss (§ self_guaranteed_death). Applied
        // here - after both paths, after their recoil/drain - so the
        // damage the move dealt still lands first.
        ApplySelfDestruct(move);
    }

    // ---- Single-target / bump path (Adjacent) - Phase 6..21 behaviour ----
    private void ExecuteSingle(MoveData move)
    {
        // Menu-invoked moves may auto-aim to nothing - still costs the turn.
        if (_defender == null)
        {
            MessageLogger.Log($"{_attacker.ActorName} used {move.Name}, but there was no target! It hit nothing but air.", MessageLogger.IneffectiveColor);
            return;
        }

        _attacker.PlayBumpAttack(_defender.GridPosition);

        // Pure Status move: no damage, only rank/ailment effects.
        if (move.Category == MoveCategory.Status)
        {
            MessageLogger.Log($"{_attacker.ActorName} used {move.Name}!");
            ApplyRankEffectIfAny(move, defenderAlive: true);
            ApplyAilmentEffectIfAny(move, defenderAlive: true);
            return;
        }

        int damage = StrikeTarget(move, _defender);
        if (damage > 0)
        {
            bool alive = _defender.Stats.IsAlive;
            ApplyRankEffectIfAny(move, alive);
            ApplyAilmentEffectIfAny(move, alive);
            if (!alive) HandleFaint(_defender);
        }

        ApplyDrain(move, damage);
        ApplyRecoil(move, damage);
    }

    // ---- Multi-target path (Line/TwoTile/Area/Room/FullFloor) ----
    private void ExecuteAoe(MoveData move)
    {
        // Area centres on the primary defender's tile; without one, on the
        // tile the user faces. Room's corridor fallback uses the same aim.
        var aim = _defender != null ? _defender.GridPosition : _attacker.GridPosition + _attacker.FacingDirection;
        _attacker.PlayBumpAttack(aim);

        var targets = TargetResolver.Resolve(move.Range, _attacker, aim, _attacker.Grid, _floorController);
        if (targets.Count == 0)
        {
            MessageLogger.Log($"{_attacker.ActorName} used {move.Name}, but nothing was in range!", MessageLogger.IneffectiveColor);
            return; // §6: no damage, no recoil (self-stun already applied)
        }

        MessageLogger.Log($"{_attacker.ActorName} used {move.Name}!");

        // Self-targeted rank effect fires once, not per target (§4-2).
        ApplySelfRankOnce(move);

        int totalDamage = 0;
        // Snapshot the list - HandleFaint QueueFree's dead targets, and a
        // Room/FullFloor list can include entities that die mid-loop.
        foreach (var target in new List<Entity>(targets))
        {
            if (!GodotObject.IsInstanceValid(target) || !target.IsAlive) continue;

            if (move.Category == MoveCategory.Status)
            {
                ApplyAoeAilment(move, target);
                ApplyEnemyRankToTarget(move, target);
                continue;
            }

            int damage = StrikeTarget(move, target);
            if (damage <= 0) continue; // missed this target

            totalDamage += damage;
            if (target.Stats.IsAlive)
            {
                ApplyAoeAilment(move, target);
                ApplyEnemyRankToTarget(move, target);
            }
            else
            {
                HandleFaint(target);
            }
        }

        ApplyDrain(move, totalDamage);
        ApplyRecoil(move, totalDamage);
    }

    // Core per-target strike: hit roll -> crit -> Phase 16 damage -> burn
    // penalty -> apply. Returns damage dealt (0 = missed). Shared verbatim
    // by the single and AoE paths so both roll accuracy/crit/damage
    // identically. Does NOT apply secondary rank/ailment or death - the
    // caller sequences those (secondary effects run on a still-alive
    // target before death processing).
    private int StrikeTarget(MoveData move, Entity target)
    {
        // IsGuaranteedHit bypasses the roll and the target's evasion rank.
        bool hits = move.IsGuaranteedHit
            || GD.Randf() * 100f < move.Accuracy * _attacker.StatusEffects.GetAccuracyMultiplier() * target.StatusEffects.GetEvasionMultiplier();
        if (!hits)
        {
            MessageLogger.Log($"{_attacker.ActorName}'s {move.Name} missed {target.ActorName}!", MessageLogger.IneffectiveColor);
            return 0;
        }

        var defenderStats = target.Stats;
        float typeMultiplier = TypeChartManager.GetMultiplier(move.Type, defenderStats.Type1, defenderStats.Type2);

        // STAB (same-type attack bonus): x1.2 when the move's Type
        // matches either of the attacker's own Types. A move is always
        // single-typed and an attacker has at most 2 Types, so this is a
        // strict either/or - "both Types match" can't structurally occur
        // (multitype_stab_proposal §7-1), no double-counting to guard.
        var attackerStats = _attacker.Stats;
        bool stabApplies = move.Type == attackerStats.Type1
            || (!string.IsNullOrEmpty(attackerStats.Type2) && move.Type == attackerStats.Type2);
        float stabMultiplier = stabApplies ? 1.2f : 1.0f;

        // Crit rolled per target (§4-3), with the move's CritRankBonus.
        bool isCrit = GD.Randf() < _attacker.StatusEffects.GetCritChanceWithBonus(move.CritRankBonus);

        float atkMul = _attacker.StatusEffects.GetAtkMultiplier();
        float defMul = target.StatusEffects.GetDefMultiplier();
        float powerMul = _attacker.StatusEffects.GetElementPowerMultiplier(move.Type);
        if (isCrit)
        {
            atkMul = Mathf.Max(1f, atkMul);
            defMul = Mathf.Min(1f, defMul);
            powerMul = Mathf.Max(1f, powerMul);
        }

        var ctx = new DamageContext
        {
            BaseAtk = _attacker.Stats.Attack,
            BaseDef = defenderStats.Defense,
            BasePower = move.Power,
            AttackElement = move.Type,
            DefenderElement = defenderStats.Type1,
            TypeEffectiveness = typeMultiplier,
            StabMultiplier = stabMultiplier,
            AtkMultiplier = atkMul,
            DefMultiplier = defMul,
            PowerMultiplier = powerMul,
            CritMultiplier = isCrit ? 1.5f : 1.0f,
            DragonMultiplier = move.DragonMultiplier,
        };

        int damage = DamageCalculator.Calculate(ctx);

        // Burn's contact-damage penalty (x0.5 output halving), outside
        // DamageCalculator - a damage-output penalty, not a stat modifier.
        if (_attacker.StatusEffects.Ailment == AilmentType.Burn && move.IsContact)
            damage = Mathf.Max(1, Mathf.FloorToInt(damage * 0.5f));

        defenderStats.TakeDamage(damage);
        _attacker.StatusEffects.ResetDamageTimer();
        target.StatusEffects.ResetDamageTimer();

        target.PlayHitFlash();
        target.ShowDamagePopup(damage);
        MessageLogger.Log($"{_attacker.ActorName} used {move.Name} on {target.ActorName}! It hit for {damage} damage.");

        if (isCrit)
            MessageLogger.Log("A critical hit!", MessageLogger.EffectiveColor);
        if (typeMultiplier > 1f)
            MessageLogger.Log("It's super effective!", MessageLogger.EffectiveColor);
        else if (typeMultiplier < 1f)
            MessageLogger.Log("It's not very effective...", MessageLogger.IneffectiveColor);

        return damage;
    }

    // Death processing shared by both paths: EXP notification (before
    // Die() so the victim is still readable), then faction-gated kill
    // tracking + drops, then Die().
    private void HandleFaint(Entity victim)
    {
        MessageLogger.Log($"{victim.ActorName} fainted!", MessageLogger.FaintColor);

        _floorController?.Experience?.NotifyDefeated(victim, _attacker);

        if (_attacker.Faction == Faction.Player && victim.Faction == Faction.Enemy)
        {
            _floorController?.RunTracker.RecordKill(victim.SpeciesId);
            MaterialDropTable.TryDrop(_floorController, victim.GridPosition, victim.ActorName);
        }

        victim.Die();
    }

    // Self-inflicted recoil, shared by both paths. totalDamageDealt is the
    // sum across every target hit (§2/§4-3: recoil fires once, on the
    // combined damage). Self-KO -> normal death, no EXP (no attacker).
    private void ApplyRecoil(MoveData move, int totalDamageDealt)
    {
        if (move.RecoilHpPercent <= 0 || totalDamageDealt <= 0) return;

        int recoil = Mathf.FloorToInt(totalDamageDealt * move.RecoilHpPercent / 100f);
        if (recoil <= 0) return;

        _attacker.Stats.TakeDamage(recoil);
        MessageLogger.Log($"{_attacker.ActorName} is hit by recoil! ({recoil} damage)", MessageLogger.IneffectiveColor);

        if (!_attacker.Stats.IsAlive)
        {
            MessageLogger.Log($"{_attacker.ActorName} fainted from the recoil!", MessageLogger.FaintColor);
            _attacker.Die();
        }
    }

    // HP drain (DrainHalf kit): the user recovers DrainHpPercent of the
    // combined damage dealt, once - the healing sibling of ApplyRecoil.
    // Clamped to MaxHp by Stats.Heal; a dead attacker (self-KO'd by a
    // simultaneous mechanic) never heals.
    private void ApplyDrain(MoveData move, int totalDamageDealt)
    {
        if (move.DrainHpPercent <= 0 || totalDamageDealt <= 0) return;
        if (!_attacker.Stats.IsAlive) return;

        int heal = Mathf.FloorToInt(totalDamageDealt * move.DrainHpPercent / 100f);
        if (heal <= 0) return;

        _attacker.Stats.Heal(heal);
        MessageLogger.Log($"{_attacker.ActorName} drained {heal} HP!", MessageLogger.ProgressionColor);
    }

    // Self-destruct (メガトン自爆): the user always faints after the move
    // resolves. Routed through the same Die() path recoil self-KO uses,
    // so NPC removal / Player game-over both behave correctly.
    private void ApplySelfDestruct(MoveData move)
    {
        if (!move.SelfGuaranteedDeath) return;
        if (!_attacker.IsAlive) return; // already down (recoil/other) - don't double-mark

        _attacker.Stats.TakeDamage(_attacker.Stats.CurrentHp);
        MessageLogger.Log($"{_attacker.ActorName} self-destructed!", MessageLogger.FaintColor);
        _attacker.Die();
    }

    // ---- Single-target secondary-effect helpers (unchanged Phase 21) ----
    private void ApplyRankEffectIfAny(MoveData move, bool defenderAlive)
    {
        if (move.RankEffectStat == RankStat.None || move.RankEffectDelta == 0) return;
        if (move.RankEffectTarget == StatusTarget.Enemy && !defenderAlive) return;
        if (GD.Randf() >= move.RankEffectChance) return;

        var target = move.RankEffectTarget == StatusTarget.Self ? _attacker : _defender;
        ApplyRankTo(move, target);
    }

    private void ApplyAilmentEffectIfAny(MoveData move, bool defenderAlive)
    {
        if (move.AilmentEffect == AilmentType.None) return;
        if (move.AilmentTarget == StatusTarget.Enemy && !defenderAlive) return;
        if (GD.Randf() * 100f >= move.AilmentChance) return;

        var target = move.AilmentTarget == StatusTarget.Self ? _attacker : _defender;
        ApplyAilmentTo(target, move.AilmentEffect);
    }

    // ---- AoE secondary-effect helpers ----
    // Ailment lands on every hit target (§4-2 "状態異常は全対象へ通常判定"),
    // rolled per target - ignores AilmentTarget's Self/Enemy split, since
    // in AoE every hit actor IS a target (no current AoE move self-ailments).
    private void ApplyAoeAilment(MoveData move, Entity target)
    {
        if (move.AilmentEffect == AilmentType.None) return;
        if (GD.Randf() * 100f >= move.AilmentChance) return;
        ApplyAilmentTo(target, move.AilmentEffect);
    }

    // Enemy-targeted rank effect: only opposing-faction hit targets, per
    // target (§4-2 "Target=Enemy なら敵対勢力の被弾者のみ").
    private void ApplyEnemyRankToTarget(MoveData move, Entity target)
    {
        if (move.RankEffectStat == RankStat.None || move.RankEffectDelta == 0) return;
        if (move.RankEffectTarget != StatusTarget.Enemy) return;
        if (target.Faction == _attacker.Faction) return; // opposing only
        if (GD.Randf() >= move.RankEffectChance) return;
        ApplyRankTo(move, target);
    }

    // Self-targeted rank effect: the user, once (§4-2 "Target=Self は使用者に1回").
    private void ApplySelfRankOnce(MoveData move)
    {
        if (move.RankEffectStat == RankStat.None || move.RankEffectDelta == 0) return;
        if (move.RankEffectTarget != StatusTarget.Self) return;
        if (GD.Randf() >= move.RankEffectChance) return;
        ApplyRankTo(move, _attacker);
    }

    private void ApplyRankTo(MoveData move, Entity target)
    {
        var moveElement = Enum.TryParse<Element>(move.Type, out var parsed) ? parsed : Element.Neutral;
        target.StatusEffects.ApplyRankDelta(move.RankEffectStat, move.RankEffectDelta, moveElement);
        string direction = move.RankEffectDelta > 0 ? "rose" : "fell";
        MessageLogger.Log($"{target.ActorName}'s {move.RankEffectStat} {direction}!", MessageLogger.NeutralColor);
    }

    private void ApplyAilmentTo(Entity target, AilmentType ailment)
    {
        if (target.StatusEffects.TryApplyAilment(ailment))
            MessageLogger.Log($"{target.ActorName} was afflicted with {ailment}!", MessageLogger.IneffectiveColor);
        else
            MessageLogger.Log($"{target.ActorName} is unaffected - already under a status condition.", MessageLogger.NeutralColor);
    }
}
