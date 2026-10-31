using System;
using System.Collections.Generic;
using System.Linq;

namespace MysteryDungeon.Story;

// 振り分けの純ロジック（基本仕様 §6）。UI 非依存。
public static class SelectionLogic
{
    // 軸ごとの回答文字（"E"/"I", "S"/"N", "T"/"F", "J"/"P"）4つから 16 タイプ名。
    public static string TypeFromAnswers(IReadOnlyList<string> letters)
    {
        if (letters.Count != 4) throw new ArgumentException("回答は4つ必要");
        return string.Concat(letters);
    }

    public static string ProtagonistSpecies(StoryDatabase db, string type)
        => db.Selection.TypeToSpecies.TryGetValue(type, out var s) ? s : throw new StoryException($"タイプ '{type}' の主人公種族が無い");

    // 主人公と同じ属性を除いた残り属性からランダムに3属性、各属性から1体ずつ。
    // primaryType: 種族ID → 第1属性。seed で再現可能。
    public static List<string> CompanionCandidates(StoryDatabase db, string protagonistSpecies,
        Func<string, string> primaryType, long seed)
    {
        var rng = new Random(unchecked((int)seed ^ (int)(seed >> 32)));
        string heroType = primaryType(protagonistSpecies);
        var pool = db.Selection.TypeToSpecies.Values.Distinct().Where(s => s != protagonistSpecies)
            .OrderBy(s => s, StringComparer.Ordinal).ToList();
        var byType = pool.Where(s => primaryType(s) != heroType)
            .GroupBy(primaryType).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        if (byType.Count < 3) throw new StoryException("相棒候補の属性が3つ未満");

        // Fisher-Yates で属性順を決め、先頭3つから各1体。
        var order = Enumerable.Range(0, byType.Count).ToList();
        for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
        return order.Take(3).Select(i => { var g = byType[i].ToList(); return g[rng.Next(g.Count)]; }).ToList();
    }

    // 選ばれなかった14体（チャート2〜5の住民枠。登場順の決定は P6 以降）。
    public static List<string> UnchosenResidents(StoryDatabase db, string protagonist, string partner)
        => db.Selection.TypeToSpecies.Values.Distinct().Where(s => s != protagonist && s != partner)
            .OrderBy(s => s, StringComparer.Ordinal).ToList();

    public static void ApplyAnswers(StoryState state, StoryDatabase db, IReadOnlyList<string> letters, long seed)
    {
        string type = TypeFromAnswers(letters);
        state.MbtiType = type;
        state.Protagonist = ProtagonistSpecies(db, type);
        state.SelectionSeed = seed;
    }

    public static void ApplyPartner(StoryState state, string partnerSpecies) => state.Partner = partnerSpecies;
}
