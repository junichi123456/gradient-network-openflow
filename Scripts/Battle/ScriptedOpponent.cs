using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;

namespace MysteryDungeon.Battle;

// 通信対戦がつながるまでの立会人。
//
// 対戦は本来「両陣営とも人が全個体を操作する」ので、この判断ロジックは
// 製品の一部ではない。相手が居ないと BattleSession は永久に BothSubmitted へ
// 到達せず、こちらが決定しても何も起きない画面になってしまう——それを
// 避けるためだけの代役で、BattleNetwork が相手の入力を運ぶようになったら
// 呼び出し側から外れる。
//
// 台本は BattleTestScene と同じ「最も近い敵へ、届く技のうち最も威力の
// 高いものを撃つ。届かなければ1歩詰める」。強さは目的ではない。
public static class ScriptedOpponent
{
    private static readonly RandomNumberGenerator Rng = new();

    // 出せるパルが居なければ ActorIndex = -1（空の提出）を返す。
    // 空でもターンは進むので、片側だけ頭数が尽きても試合は止まらない。
    public static TurnInput Decide(BattleScheduler sched, Faction faction)
    {
        var mine = sched.Roster.Where(e => e.Faction == faction).ToList();
        var available = mine.Where(e => e.IsAlive && !sched.HasActed(e)).ToList();
        if (available.Count == 0) return new TurnInput(-1, -1, Vector2I.Zero);

        var actor = available[Mathf.Clamp((int)(Rng.Randf() * available.Count), 0, available.Count - 1)];
        int actorIndex = mine.IndexOf(actor);

        var foes = sched.Roster.Where(e => e.IsAlive && e.Faction != faction).ToList();
        if (foes.Count == 0) return new TurnInput(actorIndex, -1, actor.GridPosition);

        var nearest = foes.OrderBy(e => Chebyshev(e.GridPosition, actor.GridPosition)).First();
        int dist = Chebyshev(nearest.GridPosition, actor.GridPosition);

        // 射程が届く技のうち最も威力の高いもの。隣接技は隣に居ないと選ばない。
        int best = -1, bestPower = 0;
        for (int i = 0; i < actor.Moves.Slots.Count; i++)
        {
            var data = actor.Moves.Slots[i].Data;
            if (data == null || data.Power <= 0) continue;
            if (!Reaches(data.Range, dist)) continue;
            if (data.Power > bestPower) { bestPower = data.Power; best = i; }
        }

        if (best >= 0) return new TurnInput(actorIndex, best, nearest.GridPosition);

        // 届かないので1歩詰める。斜めも1歩として扱う（盤面は全マス床）。
        var step = actor.GridPosition + new Vector2I(
            System.Math.Sign(nearest.GridPosition.X - actor.GridPosition.X),
            System.Math.Sign(nearest.GridPosition.Y - actor.GridPosition.Y));
        return new TurnInput(actorIndex, -1, step);
    }

    private static bool Reaches(MoveRange range, int dist) => range switch
    {
        MoveRange.Adjacent => dist <= 1,
        MoveRange.TwoTile => dist <= 2,
        _ => true,       // 直線/範囲/部屋/全体/周囲は盤面のどこからでも狙える
    };

    private static int Chebyshev(Vector2I a, Vector2I b) =>
        System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));
}
