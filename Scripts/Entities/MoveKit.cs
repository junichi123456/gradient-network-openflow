using System.Linq;
using MysteryDungeon.Species;

namespace MysteryDungeon.Entities;

// 種族の learnset から、そのレベルで覚えている技を最大4つ覚えさせる（先頭から重複なし）。
public static class MoveKit
{
    public static int LearnFromLearnset(MoveManager moves, string speciesId, int level)
    {
        var sp = SpeciesDatabase.Instance?.Get(speciesId);
        if (sp == null) return 0;
        int n = 0;
        foreach (var id in sp.Learnset.Where(l => l.Level <= level).Select(l => l.MoveId).Distinct())
        {
            if (n >= MoveManager.MaxMoves) break;
            if (moves.Learn(id)) n++;
        }
        return n;
    }
}
