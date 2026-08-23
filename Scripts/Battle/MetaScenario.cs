using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;
using MysteryDungeon.Species;

namespace MysteryDungeon.Battle;

// §20「最も強い構築」100通りの総当たりから見つかった三すくみ
// （#66 → #53 → #30、docs/design/pvp_spec.md §21）を環境メタとして固定し、
// それに対抗する構築を組むための共有ロジック。
//
// 「強い構築100通り」の生成（種族値上位プール・DefaultLoadout・持ち物の
// 重複なし配布）は BattleMatchScene の --strongest と完全に同じ手順・同じ
// 乱数シードを使う——じゃないと3すくみの中身がずれる。ここに集約したのは、
// 対抗構築の生成（--meta モード）でも同じ手順が要るため。
public static class MetaScenario
{
    public const int StrongPoolSize = 60;       // §20と同じ、「強い構築」のプール
    public const int ChallengerPoolSize = 150;  // 対抗構築は相性重視でより広いプールから探す
    public const ulong Seed = 20260822UL;       // §20と同じ固定シード（3すくみの中身を再現するため）

    // 3すくみの内部ID（§21で判明した #66 → #53 → #30 の連鎖）。
    // 表示順はこの連鎖の順（0=66, 1=53, 2=30）。
    public static readonly int[] MetaIds = { 66, 53, 30 };

    // §20と同じ手順で「強い構築」100通りを再現する。同じシードなので
    // 100構築の中身（species/moves/items）は§20の結果と完全に一致する。
    public static List<BattleTeam> RegenerateHundred()
    {
        var rng = new RandomNumberGenerator();
        rng.Seed = Seed;

        var pool = StrongPool(StrongPoolSize);
        var teams = new List<BattleTeam>();
        var seen = new HashSet<string>();
        for (int guard = 0; teams.Count < 100 && guard < 100 * 200; guard++)
        {
            var t = StrongestTeam(pool, rng);
            var key = TeamKey(t);
            if (!seen.Add(key)) continue;
            teams.Add(t);
        }
        return teams;
    }

    public static List<string> StrongPool(int size) =>
        (SpeciesDatabase.Instance?.All.Values ?? Enumerable.Empty<SpeciesData>())
            .OrderByDescending(s => s.BaseHP + s.BaseAtk + s.BaseDef)
            .Take(size)
            .Select(s => s.SpeciesId)
            .ToList();

    private static string TeamKey(BattleTeam t) =>
        string.Join(",", t.Entries.Select(e => e.SpeciesId).OrderBy(x => x));

    // 種族値上位プールから6匹（重複なく、伝説は1体まで）、技はDefaultLoadout
    // （その種族にとって最も強い4つ）、持ち物は対戦用16種から重複なく必ず
    // 全員へ——という「強いと思える構築」を1つ組む（§20と同じ手順）。
    public static BattleTeam StrongestTeam(List<string> pool, RandomNumberGenerator rng)
    {
        var shuffled = new List<string>(pool);
        Shuffle(shuffled, rng);

        var speciesIds = new List<string>();
        bool hasLegendary = false;
        foreach (var id in shuffled)
        {
            if (speciesIds.Count >= BattleTeam.RosterSize) break;
            bool legendary = SpeciesDatabase.Instance?.Get(id)?.IsLegendary ?? false;
            if (legendary && hasLegendary) continue;
            speciesIds.Add(id);
            if (legendary) hasLegendary = true;
        }

        var heldItems = ItemDatabase.AllIds()
            .Where(id => ItemDatabase.Get(id)?.Type == ItemType.BattleHeld).ToList();
        Shuffle(heldItems, rng);
        var itemIds = heldItems.Take(BattleTeam.RosterSize).ToList();

        var entries = new List<BattleEntry>();
        for (int i = 0; i < speciesIds.Count; i++)
        {
            var sp = SpeciesDatabase.Instance?.Get(speciesIds[i]);
            entries.Add(new BattleEntry
            {
                SpeciesId = speciesIds[i],
                MoveIds = DefaultLoadout.PickMoves(sp, MoveManager.MaxMoves),
                ItemId = itemIds.ElementAtOrDefault(i),
            });
        }
        return new BattleTeam(entries);
    }

    // Fisher-Yates。RandomNumberGenerator は System.Random と違って
    // シャッフルの口を持たないので、ここで1つだけ用意する。
    public static void Shuffle<T>(IList<T> list, RandomNumberGenerator rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = (int)(rng.Randf() * (i + 1));
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ---- 対抗構築の探索 ----

    // 候補チームが相手チームに対してどれだけ有利かの見積もり。
    // NpcOpponent.SelectionScore の相性項（自分の最強打点×相手属性への相性
    // − 相手属性が自分に通る倍率×40）と同じ式を、6vs6の全組み合わせで
    // 足し合わせる——「選出時に見えている情報だけで測る」個体単位の相性を、
    // 構築単位（6匹総当たり）に拡張しただけで、評価の物差しは§16と共通。
    public static float AdvantageScore(BattleTeam candidate, BattleTeam metaTeam)
    {
        float total = 0f;
        foreach (var c in candidate.Entries)
        {
            var cTypes = c.Species?.Types;
            if (cTypes == null || cTypes.Count == 0) continue;
            var moves = c.MoveIds.Select(MoveDatabase.Get)
                .Where(m => m != null && m.Power > 0).ToList();

            foreach (var m in metaTeam.Entries)
            {
                var mTypes = m.Species?.Types;
                if (mTypes == null || mTypes.Count == 0) continue;

                float off = moves.Count == 0 ? 0f
                    : moves.Max(mv => mv.Power * TypeMultiplier(mv.Type, mTypes));
                float def = mTypes.Sum(mt => TypeMultiplier(mt.ToString(), cTypes));
                total += off - def * 40f;
            }
        }
        return total;
    }

    public static float TypeMultiplier(string atkType, List<Element> defTypes) =>
        TypeChartManager.GetMultiplier(atkType, defTypes.Select(t => t.ToString()).ToArray());

    // 3すくみの少なくとも2体に対して有利（AdvantageScore > 0）となる構築を
    // count 件探す。「どの2/3体に有利か」のカテゴリごとに均等に採用し、
    // 特定の組み合わせ（例: 66と53だけに強い構築ばかり）に偏らないようにする。
    public static List<(BattleTeam Team, HashSet<int> Beats, float TotalAdvantage)> GenerateChallengers(
        List<BattleTeam> metaTeams, int count)
    {
        var rng = new RandomNumberGenerator();
        rng.Seed = Seed + 1;   // 3すくみ生成とは別系統の乱数列（同じ列だと100構築の続きを食う形になり見通しが悪い）

        var pool = StrongPool(ChallengerPoolSize);
        var seen = new HashSet<string>();
        var candidates = new List<(BattleTeam Team, HashSet<int> Beats, float TotalAdvantage)>();

        int attempts = 0;
        int maxAttempts = count * 4000;
        while (candidates.Count < count * 40 && attempts < maxAttempts)
        {
            attempts++;
            var t = StrongestTeam(pool, rng);
            var key = TeamKey(t);
            if (!seen.Add(key)) continue;

            var beats = new HashSet<int>();
            float totalAdv = 0f;
            for (int mi = 0; mi < metaTeams.Count; mi++)
            {
                float adv = AdvantageScore(t, metaTeams[mi]);
                totalAdv += adv;
                if (adv > 0f) beats.Add(mi);
            }
            if (beats.Count >= 2)
                candidates.Add((t, beats, totalAdv));
        }

        var byCategory = candidates
            .GroupBy(c => string.Join("", c.Beats.OrderBy(x => x)))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.TotalAdvantage).ToList());

        var result = new List<(BattleTeam, HashSet<int>, float)>();
        var cursors = byCategory.Keys.ToDictionary(k => k, k => 0);
        var categoryKeys = byCategory.Keys.OrderBy(k => k).ToList();
        while (result.Count < count && categoryKeys.Count > 0)
        {
            bool progressed = false;
            foreach (var key in categoryKeys.ToList())
            {
                if (result.Count >= count) break;
                var list = byCategory[key];
                int idx = cursors[key];
                if (idx >= list.Count) { categoryKeys.Remove(key); continue; }
                result.Add((list[idx].Team, list[idx].Beats, list[idx].TotalAdvantage));
                cursors[key] = idx + 1;
                progressed = true;
            }
            if (!progressed) break;
        }
        return result;
    }

    // ---- メタ3構築の適応（§22） ----
    //
    // 5戦ぶんのループを終えるたびに、そのループの勝率が閾値を下回っていれば
    // 「弱点」を1匹（そのループで最も多く倒れた個体）選び、技か持ち物を
    // 1つだけ変える。個体（種族）は変えない——BattleEntry.MoveIds/ItemId は
    // set が開いているので、既存の BattleTeam を直接書き換えて次の試合に
    // 引き継ぐ（新しく組み直さない）。
    public const float AdaptWinRateThreshold = 0.6f;   // これ以上勝てていれば変えない

    private static readonly string[] SustainPriority =
        { "guard_tonic_50", "endure_charm", "guard_tonic_25", "regen_band" };

    // 1回のループ終了後に呼ぶ。変更したら人間可読の1行を返す（ログ用）、
    // 変更しなければ null。
    public static string AdaptAfterLoop(BattleTeam team, BattleTeam opponent,
        int loopWins, int loopSize, IReadOnlyDictionary<string, int> faintCounts)
    {
        if (loopSize == 0) return null;
        float winRate = (float)loopWins / loopSize;
        if (winRate >= AdaptWinRateThreshold) return null;

        string weakId = null;
        int worst = 0;
        foreach (var e in team.Entries)
        {
            int f = faintCounts.TryGetValue(e.SpeciesId, out var c) ? c : 0;
            if (f > worst) { worst = f; weakId = e.SpeciesId; }
        }
        if (weakId == null) return null;   // 誰も倒れていない＝変えようがない

        var entry = team.Entries.First(e => e.SpeciesId == weakId);
        var ownTypes = entry.Species?.Types;
        var oppTypes = opponent.Entries.Select(e => e.Species?.Types)
            .Where(t => t != null && t.Count > 0).ToList();
        if (ownTypes == null || ownTypes.Count == 0) return null;

        var itemChoice = ChooseDefensiveItem(entry, ownTypes, oppTypes, team);
        if (itemChoice != null)
        {
            int slot = team.Entries.ToList().IndexOf(entry);
            string old = entry.ItemId;
            team.SetItem(slot, itemChoice);
            return $"{entry.SpeciesId}: 持ち物 {(old ?? "なし")} → {itemChoice}";
        }

        var moveChoice = ChooseMoveSwap(entry, oppTypes);
        if (moveChoice != null)
        {
            entry.ToggleMove(moveChoice.Value.OldMove);
            entry.ToggleMove(moveChoice.Value.NewMove);
            return $"{entry.SpeciesId}: 技 {moveChoice.Value.OldMove} → {moveChoice.Value.NewMove}";
        }

        return null;
    }

    // 弱点を突かれている（相手のいずれかの属性が2倍以上通る）なら
    // weakness_shell を優先。既に何らかの耐久札を持っているなら変えない
    // （持ち物は1つしか持てないので、既に対策済みなら他を試す理由が無い）。
    private static string ChooseDefensiveItem(BattleEntry entry, List<Element> ownTypes,
        List<List<Element>> oppTypes, BattleTeam team)
    {
        var used = new HashSet<string>(team.Entries
            .Select(e => e.ItemId).Where(x => !string.IsNullOrEmpty(x)));
        var free = ItemDatabase.AllIds()
            .Where(id => ItemDatabase.Get(id)?.Type == ItemType.BattleHeld && !used.Contains(id))
            .ToList();
        if (free.Count == 0) return null;

        bool exploited = oppTypes.Any(ot => ot.Any(t => TypeMultiplier(t.ToString(), ownTypes) >= 2f));
        if (exploited && entry.ItemId != "weakness_shell" && free.Contains("weakness_shell"))
            return "weakness_shell";

        // weakness_shell も広義の耐久札——SustainPriority に無いという理由で
        // 直後のループにすぐ手放してしまう（weakness_shellを持たせた次のループで
        // 「耐久札を持っていない」と誤判定し guard_tonic 系へ差し替えてしまう）
        // 振動を防ぐため、判定には含める（優先候補としては出さない＝
        // 既に持っている場合の「持っている」判定にのみ使う）。
        bool alreadyDefensive = entry.ItemId == "weakness_shell" || SustainPriority.Contains(entry.ItemId);
        if (!alreadyDefensive)
            foreach (var cand in SustainPriority)
                if (free.Contains(cand)) return cand;

        return null;
    }

    // 現在の4技のうち相手への通りが最も悪い1つを、learnset内の未採用技で
    // 最も通る1つに差し替える——ただし十分な改善（15%以上）が無ければ
    // 変えない（拮抗した技同士を意味なく入れ替え続けるのを防ぐ）。
    private static (string OldMove, string NewMove)? ChooseMoveSwap(
        BattleEntry entry, List<List<Element>> oppTypes)
    {
        if (oppTypes.Count == 0) return null;
        var learnable = entry.Learnable().Where(m => m.Power > 0).ToList();
        if (learnable.Count == 0) return null;

        float Score(MoveData m) => (float)oppTypes.Average(ot => m.Power * TypeMultiplier(m.Type, ot));

        var current = entry.MoveIds.Select(MoveDatabase.Get).Where(m => m != null).ToList();
        if (current.Count == 0) return null;

        var worstMove = current.OrderBy(Score).First();
        var bestUnused = learnable.Where(m => !entry.MoveIds.Contains(m.Id))
            .OrderByDescending(Score).FirstOrDefault();
        if (bestUnused == null) return null;

        if (Score(bestUnused) > Score(worstMove) * 1.15f)
            return (worstMove.Id, bestUnused.Id);
        return null;
    }
}
