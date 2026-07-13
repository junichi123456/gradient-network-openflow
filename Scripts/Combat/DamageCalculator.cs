using Godot;

namespace MysteryDungeon.Combat;

// Phase 16 damage pipeline - replaces the old Palworld-datamined formula
// (0.8 * sqrt(Level+1) * (Atk/Def) * Power * STAB * TypeMultiplier *
// Random(0.9-1.1)). Level scaling is unnecessary now (the new formula
// scales naturally from Atk/Def growth alone), STAB is dropped from the
// calculator itself (reintroduce later via AtkMultiplier/PowerMultiplier
// from a skill database), and damage is fully deterministic (no random
// roll) so it stays benchmarkable.
//
// Benchmark: Atk=70, Def=70, Power=20, no buffs -> damage must be
// exactly 7 (Player and enemy trading 70/70/70 stats settles in exactly
// 10 hits). See DungeonScene's temporary verification harness during
// Phase 16 development for the proof.
//
// Second, HAD-integrated benchmark (Phase 16 adjustment): species
// values 70-70-70 at Lv50 with breakthrough 0 give real stats
// HP130/Atk75/Def75 (see EntityStats' HAD formula); the standard
// Power-35 move (power_shot) then deals exactly 13 per hit, so a
// mirror match settles on exactly the 10th hit (13 x 10 = 130). This
// "mirror match takes ~10 hits" property holds across the whole legal
// species-value band (60-60-60 floor: 11 per hit, 11 hits; 140-140-135
// ceiling: 25 per hit, 8 hits) and is nearly level-invariant thanks to
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
            * ctx.CritMultiplier
            * (1f - ctx.ElementResistCut)
            * (1f - ctx.PartyElementCut);

        // Step 5: integer cast happens exactly once, at the very end.
        int finalDamage = Mathf.FloorToInt(finalDamageFloat);
        return Mathf.Max(1, finalDamage);
    }
}
