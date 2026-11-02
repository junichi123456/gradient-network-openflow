using Godot;

namespace MysteryDungeon.Combat;

// Phase 16 damage pipeline - replaces the old Palworld-datamined formula
// (0.8 * sqrt(Level+1) * (Atk/Def) * Power * STAB * TypeMultiplier *
// Random(0.9-1.1)). Level scaling is unnecessary now (the new formula
// scales naturally from Atk/Def growth alone), and damage is fully
// deterministic (no random roll) so it stays benchmarkable. STAB
// returned as a proper Step-4 multiplier (DamageContext.StabMultiplier,
// wired by AttackAction) in the multitype/STAB phase - every benchmark
// below is unaffected since a dummy context leaves it at the 1.0 default.
//
// Benchmark: Atk=70, Def=70, Power=20, no buffs -> damage must be
// exactly 8 after the flat final +1 (Calculate itself returns the
// pre-bonus 7; ApplyFlatFinalBonus adds 1). Formerly 7 without the bonus
// (a 70/70/70 pair needed 10 hits; at 8 per hit it is about 9). See DungeonScene's temporary verification harness during
// Phase 16 development for the proof.
//
// Second, HAD-integrated benchmark (Phase 16 adjustment): species
// values 70-70-70 at Lv50 with breakthrough 0 give real stats
// HP130/Atk75/Def75 (see EntityStats' HAD formula); the standard
// Power-35 move (gap_neutral_s_35 プレーンバレット) then deals exactly 14 per hit with the
// flat final +1 (13 before the bonus), so a mirror match settles on the
// 10th hit (130 / 14 = 9.29, i.e. 10 hits, same as the old 13 x 10 = 130).
// The "mirror match takes ~10 hits" property is kept at the standard
// point (130 / 14 -> 10 hits); across the legal species-value band every
// per-hit figure simply rises by 1 (floor 60-60-60: 12, ceiling
// 140-140-135: 26) and HP also scales with the species values, so the hit
// count shifts by at most one. It is nearly level-invariant thanks to
// the HAD formula's flat +5/+10 terms.
public static class DamageCalculator
{
    // Step 2's zero-division failsafe target - EffDef can't legally be
    // 0 (that would divide by (EffAtk+EffDef)=0), so a debuff stack that
    // zeroes both sides gets a nominal 1 defense instead of a NaN.
    private const float MinEffectiveDefense = 1f;

    public static int Calculate(DamageContext ctx)
    {
        // Step 1: flat buffs apply before percentage multipliers, and
        // never go negative (a stacked debuff can zero a stat, not
        // invert it into a heal).
        float effAtk = Mathf.Max(0f, (ctx.BaseAtk + ctx.AtkFlatBuff) * ctx.AtkMultiplier);
        float effDef = Mathf.Max(0f, (ctx.BaseDef + ctx.DefFlatBuff) * ctx.DefMultiplier);
        float effPower = Mathf.Max(0f, (ctx.BasePower + ctx.PowerFlatBuff) * ctx.PowerMultiplier);

        // Step 2: EffAtk and EffDef are each already >= 0 from Step 1,
        // so their sum can only be <= 0 when both are exactly 0 - the
        // one case that would divide by zero in Step 3.
        if (effAtk + effDef <= 0f)
            effDef = MinEffectiveDefense;

        // Step 3: the 10-turn-benchmark core (division and Power scaling
        // stay in one hybrid step, per spec).
        float baseCalc = (effAtk * effAtk) / (effAtk + effDef);
        float rawDamage = baseCalc * (effPower / 100f);

        // Step 4: every multiplicative modifier applies independently,
        // after the base/raw damage is fully resolved. CritMultiplier
        // (default 1.0) joins the same list so a crit stays inside the
        // single final floor below - benchmarks are unchanged because
        // their contexts leave it at 1.0.
        float finalDamageFloat = rawDamage
            * ctx.TypeEffectiveness
            * ctx.StabMultiplier
            * ctx.CritMultiplier
            * ctx.DragonMultiplier
            * (1f - ctx.ElementResistCut)
            * (1f - ctx.PartyElementCut);

        // Step 5: integer cast happens exactly once, at the very end.
        // No Max(1) floor any more: the guaranteed minimum of 1 now comes
        // from the flat final +1 (ApplyFlatFinalBonus), added by the caller
        // after EVERY later modifier. Calculate itself returns the
        // pre-bonus value (0 is possible for a truncated hit).
        return Mathf.FloorToInt(finalDamageFloat);
    }

    // Constant added to the very last damage figure of every landed hit
    // (after all multipliers, traits, items, weather, field and defender
    // reductions, immediately before HP is subtracted). Per-hit for
    // multi-hit moves. Single place to tune.
    public const int FlatFinalDamageBonus = 1;

    // Applies the flat +1. canDamage=false (type immunity, power 0, ...)
    // keeps the damage at 0 - the bonus only guarantees 1 where damage
    // was possible at all.
    public static int ApplyFlatFinalBonus(int damage, bool canDamage = true)
        => canDamage ? Mathf.Max(0, damage) + FlatFinalDamageBonus : 0;
}
