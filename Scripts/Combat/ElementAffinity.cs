using System;
using System.Collections.Generic;

namespace MysteryDungeon.Combat;

// learnset generation §1: the "weakness of the weakness" chain used to
// pick which off-type moves a species may learn.
//
//   弱点属性(X) = every Y whose move is super-effective (2.0x) INTO X
//   補完属性(X) = 弱点属性(弱点属性(X))  - a two-step chain
//
// Derived from Data/type_chart.json at call time rather than hardcoded, so
// the pair stays correct if the chart is ever rebalanced. Pure and
// side-effect free: every result is a function of the loaded chart alone.
//
// Direction matters and is easy to invert by accident. GetMultiplier's
// first argument is the ATTACK type, so "Y is a weakness OF X" is
// GetMultiplier(Y, X) == 2.0 - the attack coming IN at X, not X's own
// offence. The current chart yields exactly one weakness and therefore
// exactly one complement per element, but nothing here assumes that:
// both return sets, so a rebalanced chart with two-way weaknesses needs
// no change on this side.
public static class ElementAffinity
{
    private static readonly Element[] AllElements = (Element[])Enum.GetValues(typeof(Element));

    // The 2.0x comparison is exact, and safe for the same reason
    // AttackAction's 式 check is: Data/type_chart.json holds only
    // {0.5, 1.0, 2.0}, all exact powers of two, and this reads a single
    // cell (no product at all). If the chart ever gains a non-power-of-two
    // tier, switch to an epsilon compare here and there together.
    private const float SuperEffective = 2.0f;

    // Every element whose attacks hit `defender` for 2.0x.
    public static List<Element> Weaknesses(Element defender)
    {
        var result = new List<Element>();
        foreach (var attacker in AllElements)
            if (TypeChartManager.GetMultiplier(attacker.ToString(), defender.ToString()) == SuperEffective)
                result.Add(attacker);
        return result;
    }

    // 補完属性: the weaknesses of `element`'s own weaknesses. Deduplicated
    // and returned in Element-enum order so the result is deterministic
    // regardless of chart iteration order.
    //
    // `element` itself is NOT filtered out - on a chart with a mutual
    // weakness pair (A beats B, B beats A) an element would legitimately
    // be its own complement. The current chart produces no such case.
    public static List<Element> Complements(Element element)
    {
        var seen = new HashSet<Element>();
        foreach (var weakness in Weaknesses(element))
            foreach (var second in Weaknesses(weakness))
                seen.Add(second);

        var result = new List<Element>();
        foreach (var e in AllElements)
            if (seen.Contains(e)) result.Add(e);
        return result;
    }

    // A dual-typed species carries two independent chains, one per Type -
    // "複属性種族は、Types[0]/Types[1]それぞれで独立に弱点属性・補完属性を
    // 持つ（2系統になる）". Union of both, deduplicated, enum-ordered.
    public static List<Element> ComplementsFor(IEnumerable<Element> ownTypes)
    {
        var seen = new HashSet<Element>();
        foreach (var t in ownTypes)
            foreach (var c in Complements(t)) seen.Add(c);

        var result = new List<Element>();
        foreach (var e in AllElements)
            if (seen.Contains(e)) result.Add(e);
        return result;
    }
}
