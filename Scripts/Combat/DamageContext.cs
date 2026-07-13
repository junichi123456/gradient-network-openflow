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
    // above are still dummy defaults (see AttackAction).
    public float TypeEffectiveness { get; set; } = 1.0f;

    // System multiplier - critical hit (crit). Default 1.0 (no crit) so
    // every existing benchmark and the whole non-crit path is unchanged;
    // AttackAction sets this to 1.5 on a crit. The "ignore
    // disadvantageous rank corrections" half of the crit rule is handled
    // upstream in AttackAction (it clamps AtkMultiplier/DefMultiplier/
    // PowerMultiplier before they reach here) - this field is only the
    // final 1.5x damage boost.
    public float CritMultiplier { get; set; } = 1.0f;
}
