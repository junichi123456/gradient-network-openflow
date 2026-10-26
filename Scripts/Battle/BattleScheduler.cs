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

    public int CycleNumber { get; private set; }
    public int TurnInCycle { get; private set; }

    // 通算ターン数。サイクルをまたいで増え続ける（天候の「20ターン」は
    // この数え方。状態異常とランク減衰はサイクル境界で刻む）。
    public int TotalTurns { get; private set; }

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

        var ordered = Order(a, b);

        // フロストラグ(PriorityDown): 「次の行動の実効優先度-1」は、いま行動順に使われた
        // この優先度で使い切る（EffectivePriority が引いた分）。この後の行動で新しく付いた
        // 分は次の手番に回る。行動を飛ばされる手番（凍結/気絶）でも、順序には効いたので消費する。
        foreach (var c in ordered)
            if (!c.IsEmpty) c.Actor.StatusEffects?.ConsumePriorityDown();

        foreach (var c in ordered)
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
                var action = ApplyEncore(c.Actor, c.Actor.FilterActionForStatus(c.Action));
                c.Actor.NotifyOwnActionStarting();
                action?.Execute(TotalTurns);
                c.Actor.NotifyOwnActionEnded();

                // 「自分のターン終了時」に効く持ち物。行動が不発でも
                // ターンは終わるので、この位置は行動の成否に依らない。
                ResolveTurnEndItems(c.Actor);
            }
        }
    }

    // リピートチャント(Encore): 次の行動を、かかった時点の「最後に使った技」へ強制的に
    // 差し替える。提出された狙い（移動なら移動先）をそのまま使い、その技で狙えない
    // マスなら不発（手番だけ消費）。移動を出していても差し替える。待機などはそのまま。
    private IAction ApplyEncore(Entity actor, IAction action)
    {
        int slotIndex = actor.StatusEffects.EncoreSlot;
        if (slotIndex < 0 || action == null) return action;

        var slots = actor.Moves.Slots;
        if (slotIndex >= slots.Count) return action;
        var forced = slots[slotIndex];

        Vector2I aim;
        Dungeon.FloorController floor = actor.FloorController;
        if (action is AttackAction attack)
        {
            if (ReferenceEquals(attack.Slot, forced)) return action;   // 既に同じ技
            aim = attack.AimTile ?? attack.Defender?.GridPosition ?? actor.GridPosition;
            floor = attack.Floor ?? floor;
        }
        else if (action is MoveAction move)
        {
            aim = move.TargetPos;
        }
        else
        {
            return action;
        }

        var tiles = BattleTargeting.SelectableTiles(actor, slotIndex, actor.Grid, floor, _roster);
        if (tiles.Count > 0 && !tiles.Contains(aim))
        {
            GD.Print($"[Battle] {actor.ActorName}はアンコールで{forced.Data.Name}を強いられたが、狙えず不発");
            UI.MessageLogger.Log($"{actor.ActorName}はアンコールで{forced.Data.Name}を強いられたが、狙えなかった!",
                                 UI.MessageLogger.IneffectiveColor);
            return new WaitAction(actor);
        }

        var dir = BattleTargeting.StepToward(actor.GridPosition, aim);
        if (dir != Vector2I.Zero) actor.FaceDirection(dir);
        var defender = _roster.FirstOrDefault(e => e.IsAlive && e != actor && e.GridPosition == aim);

        GD.Print($"[Battle] {actor.ActorName}はアンコールで{forced.Data.Name}を強いられた");
        UI.MessageLogger.Log($"{actor.ActorName}はアンコールで{forced.Data.Name}を出すことになった!",
                             UI.MessageLogger.IneffectiveColor);
        return new AttackAction(actor, defender, forced, floor, aim);
    }

    // リジェネバンド(持続): 自分のターン終了時にHPを10%回復。
    // パージバンド(持続): 自分のターン終了時に全状態異常蓄積値を100減少。
    // 闇のちから: 天候がきりの間、自分のターン終了時にHPを5%回復。
    // ——持ち物と特性で条件の文言（「自分のターン終了時」）が同じなので、
    // 同じ場所・同じ順で解決する。
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

        float ratio = Combat.PowerTraitWeather.TurnEndHealRatio(
            e, e.FloorController?.Weather?.Current ?? Dungeon.WeatherType.None);
        if (ratio > 0f)
        {
            int heal = Math.Max(1, (int)(e.Stats.MaxHp * ratio));
            e.Stats.Heal(heal);
            GD.Print($"[Battle] {e.ActorName} 闇のちから(きり)でHP{heal}回復");
        }
    }

    // 行動順に使う実効優先度。技そのものの優先度に、天候で開く特性ぶんを足す。
    //
    // 水のちから: 天候があめの間、保持者の優先度+1。**技ではなく個体に付く**
    // ので、その個体が出す手すべてが速くなる（移動も含む——移動の優先度0に
    // 対しても同じように+1される）。
    //
    // フロストラグ(PriorityDown): 次の行動の実効優先度から1を引く。
    public static int EffectivePriority(Entity actor, Combat.MoveData move) =>
        (move?.Priority ?? 0) + Combat.PowerTraitWeather.PriorityBonus(
            actor, actor?.FloorController?.Weather?.Current ?? Dungeon.WeatherType.None)
        - (actor?.StatusEffects?.PriorityPenalty ?? 0);

    // 行動順。優先度 → 俊敏の実数値(気質・突破値込み)が高い順 → 合計種族値(BST6)が低い順 → ランダム。
    // 俊敏の実数値は構築の突破値・気質で決まり、ランク変化・持ち物では動かない。
    // 対戦仕様の中核なので、検証から直接叩けるように公開している。
    public List<Commitment> Order(Commitment a, Commitment b)
    {
        if (a.IsEmpty) return new List<Commitment> { b };
        if (b.IsEmpty) return new List<Commitment> { a };

        int cmp = b.Priority.CompareTo(a.Priority);          // 優先度は高いほうが先
        if (cmp == 0)
            cmp = b.Actor.Stats.Speed.CompareTo(a.Actor.Stats.Speed); // 俊敏(実数値)は高いほうが先
        if (cmp == 0)
            cmp = Bst(a.Actor).CompareTo(Bst(b.Actor));      // 同速なら合計種族値(BST6)は低いほうが先
        // 専用の RandomNumberGenerator を持たず GD.Randf()（Godot本体の
        // 大域RNG）を直接使う。BattleScheduler は1試合ごとに new される
        // ため、専用インスタンスだと Godot 管理下のネイティブオブジェクトが
        // 試合数ぶん残り、GCが追いつくまで解放されない
        // ——ヘッドレスで何百試合も連続で回すと、後半になるほど
        // 目に見えて遅くなる形で表面化した（100試合が10分以上かかる）。
        if (cmp == 0)
            cmp = GD.Randf() < 0.5f ? -1 : 1;                 // 同値ならランダム

        return cmp <= 0 ? new List<Commitment> { a, b } : new List<Commitment> { b, a };
    }

    // 合計種族値。実ステータスではなく種族値そのものを見るので、
    // ランク変化や持ち物で行動順が動くことはない。
    // 6能力合計（BST6 = 旧BST3の狭義単調増加関数）。
    public static int Bst(Entity e) => e.Stats.BaseTotal;

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

    public bool AnyAlive(Faction faction) =>
        _roster.Any(e => e.Faction == faction && e.IsAlive);

    // 決着判定。片方が全滅していれば生存側の勝ち、両方生きていれば未決着。
    // 時間切れの引き分けは時計側の判断なので、ここでは扱わない
    // （BattleClock.Resolve が全滅判定とあわせて最終結果を出す）。
    public Faction? Winner()
    {
        bool player = AnyAlive(Faction.Player);
        bool enemy = AnyAlive(Faction.Enemy);
        if (player && enemy) return null;
        if (player) return Faction.Player;
        if (enemy) return Faction.Enemy;
        return null;   // 相打ちで両者全滅
    }
}
