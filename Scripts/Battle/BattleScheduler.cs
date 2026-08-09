using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Entities;
using MysteryDungeon.Turn;

namespace MysteryDungeon.Battle;

// 対戦の進行モデル。迷宮の TurnScheduler（エネルギー蓄積式）とは別物で、
// 両者が同居しても干渉しない。
//
// 用語:
//   ターン  - パル1匹の行動。両陣営が1匹ずつ出すので1ターンに2回の行動が起きる
//   サイクル - 全員が1回ずつ行動した単位。4匹選出なので 1サイクル = 4ターン
//
// 1サイクルの間、各パルはちょうど1ターンだけ行動する。1ターン目に動いた
// パルは同じサイクルの2〜4ターン目には出せず、サイクルが切り替わって
// はじめて再び出せる。
//
// 行動順は「優先度 → 合計種族値の低い順 → ランダム」。種族値が低いほど
// 先に動くので、非力な個体に先手という形で補償が入る。
public sealed class BattleScheduler
{
    // 1ターンぶんの提出。どのパルを出すかも行動も伏せたまま集め、
    // 両陣営が揃ってから公開して解決する。
    public readonly struct Commitment
    {
        public Entity Actor { get; }
        public IAction Action { get; }
        public int Priority { get; }

        public Commitment(Entity actor, IAction action, int priority)
        {
            Actor = actor;
            Action = action;
            Priority = priority;
        }

        public bool IsEmpty => Actor == null;
    }

    private readonly List<Entity> _roster = new();
    private readonly HashSet<Entity> _actedThisCycle = new();
    private readonly RandomNumberGenerator _rng = new();

    public int CycleNumber { get; private set; }
    public int TurnInCycle { get; private set; }

    // 通算ターン数。サイクルをまたいで増え続ける（天候の「20ターン」は
    // この数え方。状態異常とランク減衰はサイクル境界で刻む）。
    public int TotalTurns { get; private set; }

    public BattleScheduler()
    {
        // 再現用のシード管理は行わない方針。命中判定や急所と同じく、
        // 同値時の順番も素のランダムで決める。
        _rng.Randomize();
    }

    public void Register(Entity actor)
    {
        if (!_roster.Contains(actor)) _roster.Add(actor);
    }

    public IReadOnlyList<Entity> Roster => _roster;

    // まだこのサイクルで行動しておらず、生存しているパル。
    // 提出できる候補はこの集合に限られる。
    public IEnumerable<Entity> AvailableFor(Faction faction) =>
        _roster.Where(e => e.Faction == faction && e.IsAlive && !_actedThisCycle.Contains(e));

    public bool HasActed(Entity actor) => _actedThisCycle.Contains(actor);

    // 1ターンを解決する。両陣営の提出を受け取り、行動順を決めて実行する。
    // 提出が空（出せるパルがいない）側は単に飛ばす。
    public void ResolveTurn(Commitment a, Commitment b)
    {
        TurnInCycle++;
        TotalTurns++;

        foreach (var c in Order(a, b))
        {
            if (c.IsEmpty) continue;

            // 先に動いたほうの攻撃で倒れた場合、後手は行動できない。
            if (!c.Actor.IsAlive)
            {
                GD.Print($"[Battle C{CycleNumber}T{TurnInCycle}] {c.Actor.ActorName} は倒れており行動できない");
                _actedThisCycle.Add(c.Actor);
                continue;
            }

            _actedThisCycle.Add(c.Actor);

            if (c.Actor.IsActionLocked())
            {
                GD.Print($"[Battle C{CycleNumber}T{TurnInCycle}] {c.Actor.ActorName} は行動できない（凍結/気絶）");
            }
            else
            {
                var action = c.Actor.FilterActionForStatus(c.Action);
                action?.Execute(TotalTurns);

                // 「自分のターン終了時」に効く持ち物。行動が不発でも
                // ターンは終わるので、この位置は行動の成否に依らない。
                ResolveTurnEndItems(c.Actor);
            }
        }
    }

    // リジェネバンド(持続): 自分のターン終了時にHPを10%回復。
    // パージバンド(持続): 自分のターン終了時に全状態異常蓄積値を100減少。
    private static void ResolveTurnEndItems(Entity e)
    {
        if (!e.IsAlive) return;

        if (e.Holds(Combat.BattleItemEffect.RegenOnTurnEnd))
        {
            int heal = Math.Max(1, (int)(e.Stats.MaxHp * 0.10f));
            e.Stats.Heal(heal);
            GD.Print($"[Battle] {e.ActorName} リジェネバンドでHP{heal}回復");
        }

        if (e.Holds(Combat.BattleItemEffect.PurgeOnTurnEnd))
        {
            e.StatusEffects.DecayAccumulation(100);
            GD.Print($"[Battle] {e.ActorName} パージバンドで蓄積値-100");
        }
    }

    // 行動順。優先度が最初で、合計種族値はその次。どちらも同じならランダム。
    // 対戦仕様の中核なので、検証から直接叩けるように公開している。
    public List<Commitment> Order(Commitment a, Commitment b)
    {
        if (a.IsEmpty) return new List<Commitment> { b };
        if (b.IsEmpty) return new List<Commitment> { a };

        int cmp = b.Priority.CompareTo(a.Priority);          // 優先度は高いほうが先
        if (cmp == 0)
            cmp = Bst(a.Actor).CompareTo(Bst(b.Actor));      // 種族値は低いほうが先
        if (cmp == 0)
            cmp = _rng.Randf() < 0.5f ? -1 : 1;              // 同値ならランダム

        return cmp <= 0 ? new List<Commitment> { a, b } : new List<Commitment> { b, a };
    }

    // 合計種族値。実ステータスではなく種族値そのものを見るので、
    // ランク変化や持ち物で行動順が動くことはない。
    public static int Bst(Entity e) =>
        e.Stats.BaseMaxHp + e.Stats.BaseAtk + e.Stats.BaseDef;

    // サイクルが終わったか（全員が行動を終えたか）。
    public bool CycleComplete =>
        _roster.Where(e => e.IsAlive).All(e => _actedThisCycle.Contains(e));

    // 次のサイクルへ。状態異常とランク減衰はここで刻む（ターンではなく
    // サイクル単位で管理する、という仕様のとおり）。
    public void BeginCycle()
    {
        CycleNumber++;
        TurnInCycle = 0;
        _actedThisCycle.Clear();
        GD.Print($"[Battle] === サイクル {CycleNumber} ===");
    }

    public void EndCycle()
    {
        foreach (var e in _roster.Where(x => x.IsAlive))
            e.ResolveStatusTick();
    }

    // 決着判定。片方が全滅していれば生存側の勝ち、両方生きていれば未決着。
    public Faction? Winner()
    {
        bool player = _roster.Any(e => e.Faction == Faction.Player && e.IsAlive);
        bool enemy = _roster.Any(e => e.Faction == Faction.Enemy && e.IsAlive);
        if (player && enemy) return null;
        if (player) return Faction.Player;
        if (enemy) return Faction.Enemy;
        return null;   // 相打ちで両者全滅
    }
}
