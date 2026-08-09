using System.Collections.Generic;
using System.Linq;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;
using MysteryDungeon.Species;

namespace MysteryDungeon.Battle;

// 構築段階（6匹を選定する時点）で確定する1匹ぶんの登録内容。
// 技4つと持ち物はここで決まりきっており、選出フェーズ（6匹→4匹）では
// もう触らない。対戦中の変更もできない。
public sealed class BattleEntry
{
    public string SpeciesId { get; init; }

    // 持ち込む技。上限は MoveManager.MaxMoves = 4。
    // learnset 内であれば習得レベルは問わない。
    public IReadOnlyList<string> MoveIds { get; init; } = new List<string>();

    // 持ち物。1匹1つまでで、チーム内で重複できない。null = 持たせない。
    public string ItemId { get; init; }

    public SpeciesData Species => SpeciesDatabase.Instance?.Get(SpeciesId);
}

// 対戦用に登録した6匹。構築時の制約（同一種族禁止・持ち物重複禁止・
// 技はlearnset内の4つまで）をここで機械検証する。
public sealed class BattleTeam
{
    public const int RosterSize = 6;      // 登録する匹数
    public const int SelectionSize = 4;   // 対戦に出す匹数

    public IReadOnlyList<BattleEntry> Entries { get; }

    public BattleTeam(IEnumerable<BattleEntry> entries)
    {
        Entries = entries.ToList();
    }

    // 構築が規則を満たしているか。満たしていれば空のリストを返す。
    // 対戦受付に進む前に必ず通す想定。
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (Entries.Count != RosterSize)
            errors.Add($"登録は{RosterSize}匹ちょうど必要（現在{Entries.Count}匹）");

        // パーティ内に同一種族のパルは設定できない。
        foreach (var g in Entries.GroupBy(e => e.SpeciesId).Where(g => g.Count() > 1))
            errors.Add($"同一種族の重複: {g.Key} が{g.Count()}匹");

        // 持ち物の重複はできない。持たせない(null)は重複判定の対象外。
        foreach (var g in Entries.Where(e => !string.IsNullOrEmpty(e.ItemId))
                                 .GroupBy(e => e.ItemId).Where(g => g.Count() > 1))
            errors.Add($"持ち物の重複: {g.Key} が{g.Count()}個");

        foreach (var e in Entries)
        {
            var species = e.Species;
            if (species == null)
            {
                errors.Add($"存在しない種族: {e.SpeciesId}");
                continue;
            }

            string who = species.DisplayName;

            if (e.MoveIds.Count == 0)
                errors.Add($"{who}: 技が1つも設定されていない");
            if (e.MoveIds.Count > MoveManager.MaxMoves)
                errors.Add($"{who}: 技は{MoveManager.MaxMoves}つまで（現在{e.MoveIds.Count}）");

            foreach (var g in e.MoveIds.GroupBy(m => m).Where(g => g.Count() > 1))
                errors.Add($"{who}: 同じ技の重複 {g.Key}");

            // learnset 内なら習得レベルは問わない、が learnset 外は不可。
            var learnable = species.Learnset.Select(l => l.MoveId).ToHashSet();
            foreach (var mid in e.MoveIds)
            {
                if (MoveDatabase.Get(mid) == null) errors.Add($"{who}: 存在しない技 {mid}");
                else if (!learnable.Contains(mid)) errors.Add($"{who}: learnset外の技 {MoveDatabase.Get(mid).Name}");
            }

            if (!string.IsNullOrEmpty(e.ItemId) && ItemDatabase.Get(e.ItemId) == null)
                errors.Add($"{who}: 存在しない持ち物 {e.ItemId}");
        }

        return errors;
    }

    // 選出フェーズが時間切れになったときの自動選出。
    // 「登録順から昇順に自動で選出される」ので先頭から4匹。
    public List<BattleEntry> AutoSelect() => Entries.Take(SelectionSize).ToList();

    // 手動選出の妥当性。登録した6匹の中からちょうど4匹であること。
    public List<string> ValidateSelection(IReadOnlyList<BattleEntry> selection)
    {
        var errors = new List<string>();

        if (selection.Count != SelectionSize)
            errors.Add($"選出は{SelectionSize}匹ちょうど必要（現在{selection.Count}匹）");

        foreach (var e in selection)
            if (!Entries.Contains(e))
                errors.Add($"登録外の個体が選出されている: {e.SpeciesId}");

        if (selection.Distinct().Count() != selection.Count)
            errors.Add("同じ個体が重複して選出されている");

        return errors;
    }
}
