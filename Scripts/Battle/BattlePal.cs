using System.Linq;
using Godot;
using MysteryDungeon.Entities;
using MysteryDungeon.Species;
using MysteryDungeon.Turn;

namespace MysteryDungeon.Battle;

// 対戦に出場する1匹。迷宮の AllyEntity / HostileEntity と違い AI を持たない
// ——対戦では両陣営とも全個体を人が操作するため、自動で行動を決める経路は
// 存在しない。行動は外から BattleScheduler へ提出される。
public partial class BattlePal : Entity
{
    // 対戦時はすべてのパルがレベル50に調整される。
    public const int BattleLevel = 50;

    public override void _Ready()
    {
        base._Ready();   // 種族解決 + Stats/Moves の生成

        ActorName = SpeciesDatabase.Instance?.Get(SpeciesId)?.DisplayName ?? SpeciesId;
        Stats.Level = BattleLevel;

        // 技構成は構築段階(6匹選定時)で確定済み。Entry があればそれを載せる。
        // Entry を渡さずに生成された場合（検証用の素置き）だけ、learnset の
        // 先頭から技枠を埋めるフォールバックに落ちる。
        if (Entry != null) ApplyLoadout(Entry);
        else LearnFromLearnset();
    }

    // 構築段階で確定した登録内容。AddChild する前に代入しておくと
    // _Ready がそのまま技と持ち物を載せる。
    public BattleEntry Entry { get; set; }

    // 構築時に決めた4技と持ち物を載せる。習得レベルは見ない
    // （learnset 内であることは BattleTeam.Validate が保証する）。
    public void ApplyLoadout(BattleEntry entry)
    {
        foreach (var mid in entry.MoveIds)
            Moves.Learn(mid);

        HeldItemId = entry.ItemId;   // Entity 側が効果解決に使う
    }

    // 登録内容を持たない個体のフォールバック。learnset から技枠が
    // 埋まるまで習得させる。習得レベルは見ない。
    public void LearnFromLearnset()
    {
        var species = SpeciesDatabase.Instance?.Get(SpeciesId);
        if (species?.Learnset == null) return;

        foreach (var row in species.Learnset)
            if (!Moves.Learn(row.MoveId)) break;   // 技枠が埋まったら終わり
    }

    // 対戦では合計種族値が行動順を決めるので、外から読めるようにしておく。
    public int Bst => BattleScheduler.Bst(this);

    // 対戦盤では自動行動しない。TurnScheduler に登録されることも無いので
    // 呼ばれない経路だが、Entity の契約として明示的に「何もしない」を返す。
    public override IAction DecideAction() => null;
}
