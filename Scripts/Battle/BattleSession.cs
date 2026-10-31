using System.Collections.Generic;
using Godot;
using MysteryDungeon.Entities;

namespace MysteryDungeon.Battle;

// ホスト側が持つ対戦の権威。トランスポートには依存しないので、ENet でも
// ローカル対戦でも同じものを使える（テストもここを直接叩く）。
//
// 役目は1つ:「両陣営の入力が揃うまで伏せておき、揃った時点で解決する」。
// 伏せる責任をここに集約しているので、通信層は入力をそのまま渡すだけでよい。
public sealed class BattleSession
{
    private readonly BattleScheduler _scheduler;
    private readonly BattleClock _clock;

    private readonly Dictionary<Faction, TurnInput> _pending = new();

    public BattleSession(BattleScheduler scheduler, BattleClock clock)
    {
        _scheduler = scheduler;
        _clock = clock;
    }

    public BattleScheduler Scheduler => _scheduler;
    public BattleClock Clock => _clock;

    // 入力を受け取る。まだ相手が出していなくても、ここでは何も起きない。
    // 二重提出は拒否する（出し直しで相手の手を見てから変えることを防ぐ）。
    public bool SubmitInput(Faction faction, TurnInput input)
    {
        if (_pending.ContainsKey(faction)) return false;
        _pending[faction] = input;
        return true;
    }

    public bool HasSubmitted(Faction faction) => _pending.ContainsKey(faction);

    // 両陣営が出し終わったか。これが true になるまで、どちらの入力も
    // 相手には見えない。
    public bool BothSubmitted =>
        _pending.ContainsKey(Faction.Player) && _pending.ContainsKey(Faction.Enemy);

    // 相手の入力を覗く唯一の経路。両者が揃う前は必ず null を返す。
    // 「伏せて同時に決める」を型と手続きの両方で守るための関門。
    public TurnInput? PeekOpponentInput(Faction viewer)
    {
        if (!BothSubmitted) return null;
        var other = viewer == Faction.Player ? Faction.Enemy : Faction.Player;
        return _pending.TryGetValue(other, out var v) ? v : null;
    }

    // 揃った入力を解決する。解決できなければ null。
    // 実際の行動生成（TurnInput → IAction）は呼び出し側の責務にしている。
    // AttackAction/MoveAction の組み立てには盤面の実体が要るためで、
    // ここは「揃うまで伏せる」ことだけに責任を持つ。
    public TurnResult? ResolveTurn(
        System.Func<Faction, TurnInput, BattleScheduler.Commitment> build,
        double turnSeconds)
    {
        if (!BothSubmitted) return null;

        var a = build(Faction.Player, _pending[Faction.Player]);
        var b = build(Faction.Enemy, _pending[Faction.Enemy]);

        // 解決順はホストが決める。クライアントは乱数を引かないので、
        // この順序を結果として配る必要がある。
        var order = new List<Faction>();
        foreach (var c in _scheduler.Order(a, b))
            if (!c.IsEmpty) order.Add(c.Actor.Faction);

        _scheduler.ResolveTurn(a, b);
        _clock.Match.Advance(turnSeconds);
        _pending.Clear();

        var hp = new List<int>();
        foreach (var e in _scheduler.Roster) hp.Add(e.IsAlive ? e.Stats.CurrentHp : 0);

        return new TurnResult(_scheduler.CycleNumber, _scheduler.TurnInCycle,
                              order, hp, _clock.Resolve(_scheduler));
    }

    // 相手に見せてよい編成。技と持ち物を落として種族だけにする。
    public static List<PublicEntryView> DiscloseTeam(BattleTeam team) =>
        PublicEntryView.Of(team.Entries);
}
