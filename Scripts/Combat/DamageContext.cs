namespace MysteryDungeon.Combat;

// Everything DamageCalculator.Calculate needs, gathered in one place so
// the pipeline stays transparent and each buff source (individual
// passive, party skill, future skill database) has one obvious field to
// write into instead of being folded into an opaque running total.
public class DamageContext
{
    // Base stats. BasePower comes from the attacking move's own Power
    // (AttackAction wires move.Power in); power_shot's 35 is the
    // reference "standard attack" the balance benchmarks anchor on
    // (see DamageCalculator).
    public float BaseAtk { get; set; }
    public float BaseDef { get; set; }
    public float BasePower { get; set; }

    // Element info - not consumed by Calculate() itself (TypeEffectiveness
    // below already carries the resolved multiplier), kept here purely
    // for traceability/future use (e.g. a skill database deciding which
    // ElementResistCut applies based on AttackElement).
    public string AttackElement { get; set; }
    public string DefenderElement { get; set; }

    // Attacker buffs (individual passive / party skill) - all default to
    // "no effect" so a dummy context (no skill database yet) behaves
    // exactly like the benchmark's "buffs all off" case.
    public float AtkFlatBuff { get; set; }
    public float AtkMultiplier { get; set; } = 1.0f;
    public float PowerFlatBuff { get; set; }
    public float PowerMultiplier { get; set; } = 1.0f;

    // Defender buffs
    public float DefFlatBuff { get; set; }
    public float DefMultiplier { get; set; } = 1.0f;
    public float ElementResistCut { get; set; } // 0.0-1.0, individual passive
    public float PartyElementCut { get; set; }  // 0.0-1.0, party skill

    // System multiplier - real type-chart effectiveness (TypeChartManager),
    // not a "buff": always computed for real even while the buff fields
    // above are still dummy defaults (see AttackAction). Already
    // multi-type-aware: TypeChartManager.GetMultiplier multiplies across
    // the defender's Type1/Type2 (empty Type2 = neutral 1.0), so a
    // dual-typed defender's combined effectiveness lands here as one
    // resolved value with no change needed on this side.
    public float TypeEffectiveness { get; set; } = 1.0f;

    // System multiplier - STAB (same-type attack bonus): x1.2 when the
    // move's Type is one of the attacker's own Types. Default 1.0 so
    // every existing benchmark is unchanged; AttackAction sets this from
    // attacker.Stats.Type1/Type2. Not a rank multiplier (like
    // TypeEffectiveness/DragonMultiplier), so it is NOT touched by the
    // crit rule's "ignore disadvantageous rank corrections" clamp.
    public float StabMultiplier { get; set; } = 1.0f;

    // System multiplier - critical hit (crit). Default 1.0 (no crit) so
    // every existing benchmark and the whole non-crit path is unchanged;
    // AttackAction sets this to 1.5 on a crit. The "ignore
    // disadvantageous rank corrections" half of the crit rule is handled
    // upstream in AttackAction (it clamps AtkMultiplier/DefMultiplier/
    // PowerMultiplier before they reach here) - this field is only the
    // final 1.5x damage boost.
    public float CritMultiplier { get; set; } = 1.0f;

    // System multiplier - a move's own unconditional DragonMultiplier
    // (400-move import). Default 1.0 so every existing benchmark and the
    // whole non-dragon path is unchanged; AttackAction sets it from
    // move.DragonMultiplier. Joins the Step-4 chain like CritMultiplier.
    public float DragonMultiplier { get; set; } = 1.0f;
}
