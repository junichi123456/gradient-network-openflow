using System;

namespace MysteryDungeon.Progression;

// Phase 18-A growth curve: Medium-Fast (cubic), TotalExp(L) = L^3.
// Pure functions, no Godot dependency, no side effects - kept trivially
// unit-testable per spec. Checkpoints: Lv10 = 1,000 / Lv50 = 125,000 /
// Lv100 (cap) = 1,000,000; ExpToNext(L) = 3L^2 + 3L + 1 (ExpToNext(10)
// = 331, the "6 standard kills per level at Lv10" anchor).
public static class ExpCurve
{
    // Cumulative EXP required to BE `level`. Clamped below at Lv1 so a
    // stray 0/negative level can't produce a negative threshold.
    public static long TotalExpForLevel(int level)
    {
        long l = Math.Max(1, level);
        return l * l * l;
    }

    // EXP between `level` and `level + 1`. Callers guard the level cap
    // themselves (ExperienceSystem's threshold loop stops at LevelCap).
    public static long ExpToNext(int level) => TotalExpForLevel(level + 1) - TotalExpForLevel(level);
}
