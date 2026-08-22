using System.Collections.Generic;
using System.Linq;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;
using MysteryDungeon.Species;

namespace MysteryDungeon.Battle;

// 構築段階（6匹を選定する時点）で確定する1匹ぶんの登録内容。
// 技4つと持ち物はここで決まりきっており、選出フェーズ（6匹→4匹）では
// もう触らない。対戦中の変更もできない。
//
// **構築画面の間だけは書き換わる。**画面が種族・技・持ち物を差し替えるので
// set を開けてある。対戦が始まったあとにこれを触る経路は無い
// （BattlePal は _Ready で一度読むだけ）。
public sealed class BattleEntry
{
    public string SpeciesId { get; set; }

    // 持ち込む技。上限は MoveManager.MaxMoves = 4。
    // learnset 内であれば習得レベルは問わない。
    public IReadOnlyList<string> MoveIds { get; set; } = new List<string>();

    // 持ち物。1匹1つまでで、チーム内で重複できない。null = 持たせない。
    public string ItemId { get; set; }

    public SpeciesData Species => SpeciesDatabase.Instance?.Get(SpeciesId);

    // この種族が覚えられる技の一覧。**習得レベルは見ない**——対人戦では
    // レベルキャップを無視して learnset 全部から選べる（§6）。
    // 同じ技が複数のレベルに現れる learnset があるので重複は畳む。
    public List<MoveData> Learnable()
    {
        var acc = new List<MoveData>();
        var seen = new HashSet<string>();
        foreach (var row in Species?.Learnset ?? new List<LearnsetEntry>())
        {
            if (!seen.Add(row.MoveId)) continue;
            var m = MoveDatabase.Get(row.MoveId);
            if (m != null) acc.Add(m);
        }
        return acc;
    }

    // その技を最初に覚えるレベル。表示用（選べるかどうかには影響しない）。
    public int LearnLevel(string moveId)
    {
        int best = int.MaxValue;
        foreach (var row in Species?.Learnset ?? new List<LearnsetEntry>())
            if (row.MoveId == moveId && row.Level < best) best = row.Level;
        return best == int.MaxValue ? 0 : best;
    }

    // 技枠の入れ替え。既に入っていれば外し、空きがあれば入れる。
    // 戻り値は「盤面が変わったか」。
    public bool ToggleMove(string moveId)
    {
        var list = MoveIds.ToList();
        if (list.Remove(moveId)) { MoveIds = list; return true; }
        if (list.Count >= MoveManager.MaxMoves) return false;
        if (!Learnable().Any(m => m.Id == moveId)) return false;
        list.Add(moveId);
        MoveIds = list;
        return true;
    }
}

// 対戦用に登録した6匹。構築時の制約（同一種族禁止・持ち物重複禁止・
// 技はlearnset内の4つまで）をここで機械検証する。
public sealed class BattleTeam
{
    public const int RosterSize = 6;      // 登録する匹数
    public const int SelectionSize = 4;   // 対戦に出す匹数

    private readonly List<BattleEntry> _entries;

    public IReadOnlyList<BattleEntry> Entries => _entries;

    public BattleTeam(IEnumerable<BattleEntry> entries)
    {
        _entries = entries.ToList();
    }

    // ---- 構築画面からの編集 ----

    // 枠の種族を差し替える。技は持ち越せない（learnset が変わるので
    // そのままでは learnset 外になる）ので、既定の4技を入れ直す。
    // 同一種族が既に居る枠には入れない。
    public bool SetSpecies(int slot, string speciesId)
    {
        if (slot < 0 || slot >= _entries.Count) return false;
        if (SpeciesDatabase.Instance?.Get(speciesId) == null) return false;
        if (_entries.Where((_, i) => i != slot).Any(e => e.SpeciesId == speciesId)) return false;

        _entries[slot].SpeciesId = speciesId;
        _entries[slot].MoveIds = DefaultLoadout.PickMoves(
            SpeciesDatabase.Instance?.Get(speciesId), MoveManager.MaxMoves);
        return true;
    }

    // 持ち物を持たせる。チーム内で重複できないので、他の枠が持っていたら
    // その枠から外す（持ち替え）。null で外す。
    public bool SetItem(int slot, string itemId)
    {
        if (slot < 0 || slot >= _entries.Count) return false;
        if (!string.IsNullOrEmpty(itemId) && ItemDatabase.Get(itemId) == null) return false;

        if (!string.IsNullOrEmpty(itemId))
            foreach (var other in _entries.Where(e => e != _entries[slot] && e.ItemId == itemId))
                other.ItemId = null;

        _entries[slot].ItemId = itemId;
        return true;
    }

    // その持ち物を持っている枠。構築画面が「誰が持っているか」を出すのに使う。
    public BattleEntry HolderOf(string itemId) =>
        string.IsNullOrEmpty(itemId) ? null : _entries.FirstOrDefault(e => e.ItemId == itemId);

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

        // タグ:伝説を持つパルは1つの構築に1体まで。
        int legendaryCount = Entries.Count(e => e.Species?.IsLegendary == true);
        if (legendaryCount > 1)
            errors.Add($"「伝説」タグは1体まで（現在{legendaryCount}体）");

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
