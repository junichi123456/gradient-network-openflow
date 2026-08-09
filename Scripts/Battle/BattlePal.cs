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

        // 「learnset 内の技はレベルキャップに関係なく変更可能」なので、
        // 習得レベルは一切見ない。ただし技枠は MoveManager.MaxMoves = 4 なので、
        // learnset のうち4つだけが実際に載る。どの4つを持ち込むかは選出
        // フェーズの仕事で、ここは選択UIが入るまでの仮置き（先頭から4つ）。
        LearnFromLearnset();
    }

    // learnset から技枠が埋まるまで習得させる。習得レベルは見ない。
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
