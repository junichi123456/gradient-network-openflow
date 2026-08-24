using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Dungeon;
using MysteryDungeon.Entities;
using MysteryDungeon.Grid;
using MysteryDungeon.Turn;

namespace MysteryDungeon.Battle;

// 「その行動でどこを指せるか」「指すとどこへ当たるか」の規則。
//
// プレイヤーの操作（BattleFlow）とNPCの判断（NpcOpponent）が同じ表を見る。
// 別々に持つと、UIでは選べないマスをNPCだけが撃てる（あるいはその逆）と
// いう食い違いが必ず起きる——learnset の生成器と検証器が別々に規則表を
// 持っていて実際に事故った、あれと同じ構図。
public static class BattleTargeting
{
    // 8方向。移動も隣接攻撃も斜めを含む（盤面は全マスが床）。
    public static readonly Vector2I[] Directions =
    {
        new(0, -1), new(1, -1), new(1, 0), new(1, 1),
        new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1),
    };

    // 狙い先へ向かう単位ベクトル。射線上のマスを指した結果なので、
    // 軸ごとの符号を取るだけで元の方向に戻る。
    public static Vector2I StepToward(Vector2I from, Vector2I to) =>
        new(System.Math.Sign(to.X - from.X), System.Math.Sign(to.Y - from.Y));

    // 指せるマス。空で返るのは「狙う先を持たない技」（周囲・部屋・全体）で、
    // その場合は指定なしでそのまま撃てる。
    public static List<Vector2I> SelectableTiles(
        Entity actor, int slot, GridManager grid, FloorController floor,
        IEnumerable<Entity> roster)
    {
        var tiles = new List<Vector2I>();
        if (actor == null || grid == null) return tiles;

        // 移動は隣接8マス。1ターン1マスなので、遠くのマスは指させない。
        if (slot < 0)
        {
            var occupied = roster.Where(e => e.IsAlive && e != actor)
                                 .Select(e => e.GridPosition).ToHashSet();
            foreach (var d in Directions)
            {
                var t = actor.GridPosition + d;
                if (grid.IsWalkable(t) && !occupied.Contains(t)) tiles.Add(t);
            }
            return tiles;
        }

        if (slot >= actor.Moves.Slots.Count) return tiles;
        var data = actor.Moves.Slots[slot].Data;
        if (data == null) return tiles;

        switch (data.Range)
        {
            case MoveRange.Adjacent:
                foreach (var d in Directions)
                {
                    var t = actor.GridPosition + d;
                    if (grid.IsWalkable(t)) tiles.Add(t);
                }
                break;

            // 直線と2マスは「向き」を選ぶ技。8方向の射線上のマスをすべて
            // 候補にして、その中の1マスを指すと向きが決まる形にする。
            case MoveRange.Line:
            case MoveRange.TwoTile:
                foreach (var d in Directions)
                    tiles.AddRange(TargetResolver.ResolveTiles(
                        data.Range, actor.GridPosition, d, actor.GridPosition, grid, floor));
                break;

            case MoveRange.Area:
                for (int y = 0; y < BattleBoard.Height; y++)
                    for (int x = 0; x < BattleBoard.Width; x++)
                        if (grid.IsWalkable(new Vector2I(x, y))) tiles.Add(new Vector2I(x, y));
                break;

            // 周囲・部屋・全体は狙う先を持たない。候補なし＝即提出可。
        }
        return tiles;
    }

    // 指した1マスから決まる着弾の形。候補（SelectableTiles）とは別物で、
    // こちらは提出前に「実際どこへ当たるか」を見せるためのもの。
    public static List<Vector2I> ImpactTiles(
        Entity actor, int slot, Vector2I aim, GridManager grid, FloorController floor)
    {
        if (grid == null || slot < 0 || actor == null) return new List<Vector2I> { aim };
        if (slot >= actor.Moves.Slots.Count) return new List<Vector2I> { aim };

        var data = actor.Moves.Slots[slot].Data;
        if (data == null) return new List<Vector2I> { aim };

        return data.Range switch
        {
            MoveRange.Adjacent => new List<Vector2I> { aim },
            MoveRange.Line or MoveRange.TwoTile => TargetResolver.ResolveTiles(
                data.Range, actor.GridPosition, StepToward(actor.GridPosition, aim),
                aim, grid, floor),
            _ => TargetResolver.ResolveTiles(data.Range, actor.GridPosition,
                                             actor.FacingDirection, aim, grid, floor),
        };
    }

    // その狙い方で実際に当たる相手。味方の巻き込みもそのまま返る
    // （評価する側が損得を判断する）。
    //
    // 隣接だけは AttackAction が単体路を通るので TargetResolver を挟まない。
    public static List<Entity> Victims(
        Entity actor, int slot, Vector2I aim, GridManager grid, FloorController floor,
        IEnumerable<Entity> roster)
    {
        if (actor == null || slot < 0 || slot >= actor.Moves.Slots.Count)
            return new List<Entity>();

        var data = actor.Moves.Slots[slot].Data;
        if (data == null) return new List<Entity>();

        if (data.Range == MoveRange.Adjacent)
        {
            var one = roster.FirstOrDefault(e => e.IsAlive && e != actor && e.GridPosition == aim);
            return one == null ? new List<Entity>() : new List<Entity> { one };
        }

        // 直線・2マスは向きで飛ぶ。評価のためだけに実体の向きを変えるわけには
        // いかないので、向きを引数に取れる TargetResolver 側で解決する……
        // が、Resolve は user.FacingDirection を見る。ここだけ一時的に
        // 向きを合わせ、必ず元へ戻す。
        var saved = actor.FacingDirection;
        var dir = StepToward(actor.GridPosition, aim);
        if (dir != Vector2I.Zero) actor.FaceDirection(dir);
        try
        {
            return TargetResolver.Resolve(data.Range, actor, aim, grid, floor);
        }
        finally
        {
            if (saved != Vector2I.Zero) actor.FaceDirection(saved);
        }
    }
}
