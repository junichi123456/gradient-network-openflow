using System;
using Godot;

namespace MysteryDungeon.Combat;

// Damage pipeline, Pokemon-style (spec 2026-11-02-pokemon-style-damage):
//   base  = floor(floor(floor(Lv*2/5)+2) * Power * Atk / Def / 50) + 2
//   final = base * random(0.85..1.00) * type * STAB * crit(1.5) * dragon
//           * (1-elementResist) * (1-partyElementCut), floored once;
//   a landed hit (multiplier product > 0 and power > 0) deals at least 1.
// Atk/Def/Power are the "effective" values (flat buffs first, then
// multipliers). Level is the ATTACKER's. The random factor is supplied by
// AttackAction (one GD.Randf() draw per damaging hit); benchmarks, checks
// and NPC estimates leave RandomFactor at 1.0.
//
// Benchmarks (random 1.0): Lv50, A=D=75, Power 35 -> 17 (HP130 ~7.6 hits;
// 0.85 -> 14). Lv5, A=D=12, Power 25 -> 4 (HP22 ~5.5 hits). Across Lv5-Lv100
// a fight settles in roughly 5-8 hits.
public static class DamageCalculator
{
    // Step 2's zero-division failsafe target - EffDef can't legally be
    // 0 (that would divide by (EffAtk+EffDef)=0), so a debuff stack that
    // zeroes both sides gets a nominal 1 defense instead of a NaN.
    private const float MinEffectiveDefense = 1f;

    public const float MinRandomFactor = 0.85f;
    public const int RandomSteps = 16; // (85 + 0..15) / 100

    // Maps a uniform [0,1) draw to the 16-step factor 0.85..1.00.
    // Test hook: when set, the roll is still drawn (RNG stream unchanged)
    // but the factor is replaced. Regression sets 1.0 for fixed-value checks.
    public static float? ForcedRandomFactor;

    public static float RandomFactorFromRoll(float roll01)
    {
        if (ForcedRandomFactor.HasValue) return ForcedRandomFactor.Value;
        int k = Mathf.Clamp((int)(roll01 * RandomSteps), 0, RandomSteps - 1);
        return (85 + k) / 100f;
    }

    public static int Calculate(DamageContext ctx)
    {
        // Flat buffs apply before percentage multipliers, never negative.
        float effAtk = Mathf.Max(0f, (ctx.BaseAtk + ctx.AtkFlatBuff) * ctx.AtkMultiplier);
        float effDef = Mathf.Max(0f, (ctx.BaseDef + ctx.DefFlatBuff) * ctx.DefMultiplier);
        float effPower = Mathf.Max(0f, (ctx.BasePower + ctx.PowerFlatBuff) * ctx.PowerMultiplier);

        if (effPower <= 0f) return 0;

        // Zero-division failsafe: a zero defense becomes a nominal 1.
        if (effDef <= 0f)
            effDef = MinEffectiveDefense;

        // Pokemon-style base (integer floors per stage; the Power/Atk/Def
        // product is evaluated in double with a tiny epsilon so exact
        // quotients such as 57750/75 never floor one too low).
        int levelTerm = (ctx.Level * 2) / 5 + 2;
        double inner = Math.Floor((double)levelTerm * effPower * effAtk / effDef + 1e-6);
        int baseDamage = (int)Math.Floor(inner / 50.0) + 2;

        double mult = (double)ctx.TypeEffectiveness
            * ctx.StabMultiplier
            * ctx.CritMultiplier
            * ctx.DragonMultiplier
            * (1f - ctx.ElementResistCut)
            * (1f - ctx.PartyElementCut);

        int dmg = (int)Math.Floor(baseDamage * (double)ctx.RandomFactor * mult + 1e-6);

        // A landed, damage-capable hit deals at least 1.
        if (mult > 0.0 && effPower > 0f)
            dmg = Math.Max(1, dmg);
        return Math.Max(0, dmg);
    }
}
