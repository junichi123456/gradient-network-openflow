using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Entities;

namespace MysteryDungeon.Battle;

// 通信対戦でやり取りする内容。トランスポート（ENet/RPC）には依存せず、
// 「何を送るか」だけを定める。
//
// ■ ホスト権威型である理由
// 命中判定・急所・状態異常の抽選など、戦闘の乱数は35箇所に散っており、
// シード管理は行わない方針（§5-2）。したがって両者が同じ入力から同じ結果を
// 再現することはできず、ロックステップ方式は成立しない。
// 片側（ホスト）だけが BattleScheduler を回して結果を配る形にする。
// クライアントは入力を送り、返ってきた結果を描画するだけで、自分では
// ダメージを計算しない。

// 相手に開示してよい範囲。§1「開示されるのは種族のみであり、その技や
// 道具などの細部は開示されない」を型で担保する。
// BattleEntry をそのまま送ると技と持ち物が漏れるので、送信前に必ず
// この形へ落とす。
public readonly struct PublicEntryView
{
    public string SpeciesId { get; }

    public PublicEntryView(string speciesId) => SpeciesId = speciesId;

    public static PublicEntryView Of(BattleEntry entry) => new(entry.SpeciesId);

    public static List<PublicEntryView> Of(IEnumerable<BattleEntry> entries) =>
        entries.Select(Of).ToList();
}

// 1ターンぶんの入力。どのパルが何をするかを指す。
// 「指定するパルと行動はどちらも伏せて同時に決める」ので、これは
// 両者ぶんが揃うまで相手には見せない。
public readonly struct TurnInput
{
    public int ActorIndex { get; }     // 選出4匹の何番目か
    public int MoveSlot { get; }       // 技枠 0..3。移動なら -1
    public Vector2I Target { get; }    // 技の狙い先 / 移動先

    public TurnInput(int actorIndex, int moveSlot, Vector2I target)
    {
        ActorIndex = actorIndex;
        MoveSlot = moveSlot;
        Target = target;
    }

    public bool IsMove => MoveSlot < 0;
}

// ホストが解決したあとに配る結果。クライアントはこれを描画するだけで、
// 自分では乱数を引かない。
public readonly struct TurnResult
{
    public int CycleNumber { get; }
    public int TurnInCycle { get; }

    // 実際に行動した順。行動順は優先度→種族値→ランダムで決まるので、
    // クライアント側では再現できない。ホストが決めた順序をそのまま配る。
    public IReadOnlyList<Faction> ActingOrder { get; }

    // 解決後の全個体のHP。差分ではなく実値を配るので、取りこぼしがあっても
    // 次のターンで整合する。
    public IReadOnlyList<int> HpAfter { get; }

    public BattleOutcome Outcome { get; }

    public TurnResult(int cycleNumber, int turnInCycle, IReadOnlyList<Faction> actingOrder,
                      IReadOnlyList<int> hpAfter, BattleOutcome outcome)
    {
        CycleNumber = cycleNumber;
        TurnInCycle = turnInCycle;
        ActingOrder = actingOrder;
        HpAfter = hpAfter;
        Outcome = outcome;
    }
}
