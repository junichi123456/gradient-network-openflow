using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Dungeon;
using MysteryDungeon.Entities;
using MysteryDungeon.Grid;

namespace MysteryDungeon.Battle;

// 対戦の相手を務めるNPC。
//
// マッチング（人の相手を探す仕組み）はまだ実装できる段階にないので、
// 相手側の3つの決定——選出・配置・毎ターンの行動——をここが担う。
// **人の相手と同じ入口しか使わない**のが設計上の要点で、選出は
// BattleClock.SubmitSelection、配置は BattleDeployment、行動は TurnInput。
// 盤面を直接触る近道は持たない。だから通信対戦が入るとき、置き換わるのは
// この1クラスだけで済む。
//
// 選出と配置は対戦が始まる前に決めきる。人の側と同様、相手の選択を
// 見ないまま決めるので、伏せて同時に決める規則がそのまま成り立つ。
public sealed class NpcOpponent
{
    public NpcTeam Profile { get; }
    public Faction Faction { get; }

    // 対戦前に決めきる2つ。外からは読めるが、画面へ渡す口は作っていない
    // （相手の選出と配置は対戦開始まで開示しない — §14）。
    public IReadOnlyList<BattleEntry> Selection { get; private set; }
    public BattleDeployment Deployment { get; private set; }

    public NpcOpponent(NpcTeam profile, Faction faction = Entities.Faction.Enemy)
    {
        Profile = profile;
        Faction = faction;
        Prepare();
    }

    public string Name => Profile?.Name ?? "NPC";

    // ---- 対戦前: 選出と配置 ----

    private void Prepare()
    {
        Selection = ChooseSelection();
        Deployment = ChooseDeployment(Selection);
    }

    // 6匹から4匹。合計種族値と最大威力を見て上から採るが、同点は乱数で
    // 崩す。毎回まったく同じ4匹だと、2戦目以降が作業になる。
    private List<BattleEntry> ChooseSelection()
    {
        if (Profile == null) return new List<BattleEntry>();

        return Profile.Team.Entries
            .OrderByDescending(e =>
            {
                var sp = e.Species;
                int bst = sp == null ? 0 : sp.BaseHP + sp.BaseAtk + sp.BaseDef;
                int power = e.MoveIds.Select(m => MoveDatabase.Get(m)?.Power ?? 0)
                                     .DefaultIfEmpty(0).Max();
                // 専用の RandomNumberGenerator は持たない。NpcOpponent は
                // 対戦のたびに new されるので、専用インスタンスだと
                // ヘッドレスで何百試合も回すほど未解放のネイティブ
                // オブジェクトが積み上がり後半だけ極端に遅くなる
                // （BattleScheduler._rng を GD.Randf() に切り替えた理由と同じ）。
                return bst + power * 2 + (int)(GD.Randi() % 61);
            })
            .Take(BattleTeam.SelectionSize)
            .ToList();
    }

    // 自陣6マスへ4匹。隣接技しか持たない個体を前列（相手に近い側）へ、
    // 射程を持つ個体を後列へ置く。前に出ないと何もできない個体を後ろに
    // 置くと、詰めるだけで1サイクル溶ける。
    private BattleDeployment ChooseDeployment(IReadOnlyList<BattleEntry> selection)
    {
        var tiles = BattleDeployment.AvailableTiles(Faction);

        // 相手に近い側から並べる。論理座標で Enemy は上(Yが小)にいるので、
        // Enemy にとっては Y が大きいマスほど相手に近い。
        tiles.Sort((a, b) => Faction == Entities.Faction.Player
            ? a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X)
            : a.Y != b.Y ? b.Y.CompareTo(a.Y) : a.X.CompareTo(b.X));

        // 前に出たい順。隣接技しか無ければ最優先で前列。
        var order = selection.OrderByDescending(MeleeBias).ToList();

        var map = new Dictionary<BattleEntry, Vector2I>();
        for (int i = 0; i < order.Count && i < tiles.Count; i++)
            map[order[i]] = tiles[i];
        return new BattleDeployment(Faction, map);
    }

    // 隣接技の比率。1.0 なら殴るしかないので前に出る必要がある。
    private static float MeleeBias(BattleEntry e)
    {
        var ranges = e.MoveIds.Select(m => MoveDatabase.Get(m))
                              .Where(m => m != null && m.Power > 0)
                              .Select(m => m.Range).ToList();
        if (ranges.Count == 0) return 0f;
        return ranges.Count(r => r == MoveRange.Adjacent) / (float)ranges.Count;
    }

    // ---- 対戦中: 毎ターンの行動 ----

    // 出せるパルが居なければ ActorIndex = -1（空の提出）を返す。
    // 空でもターンは進むので、片側だけ頭数が尽きても試合は止まらない。
    public TurnInput Decide(BattleScheduler sched, GridManager grid, FloorController floor)
    {
        var mine = sched.Roster.Where(e => e.Faction == Faction).ToList();
        var available = mine.Where(e => e.IsAlive && !sched.HasActed(e)).ToList();
        if (available.Count == 0) return new TurnInput(-1, -1, Vector2I.Zero);

        var foes = sched.Roster.Where(e => e.IsAlive && e.Faction != Faction).ToList();
        if (foes.Count == 0) return new TurnInput(mine.IndexOf(available[0]), -1,
                                                 available[0].GridPosition);

        // 出せる全員 × 技4つ × 狙える全マスを総当たりして、最も得点の
        // 高い1手を採る。盤面は56マスで候補も高々数百なので、素直に全部
        // 見たほうが「なぜその手を選んだか」を追える。
        TurnInput best = default;
        float bestScore = float.NegativeInfinity;

        foreach (var actor in available)
        {
            int actorIndex = mine.IndexOf(actor);

            for (int slot = 0; slot < actor.Moves.Slots.Count; slot++)
            {
                var data = actor.Moves.Slots[slot].Data;
                if (data == null || actor.Moves.Slots[slot].CurrentPp <= 0) continue;
                if (data.Power <= 0) continue;              // 変化技は評価しない

                var aims = BattleTargeting.SelectableTiles(actor, slot, grid, floor, sched.Roster);

                // 狙う先を持たない技（周囲・部屋・全体）は自分の足下を指す。
                if (aims.Count == 0) aims = new List<Vector2I> { actor.GridPosition };

                foreach (var aim in aims)
                {
                    float score = ScoreAttack(actor, slot, aim, grid, floor, sched.Roster);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = new TurnInput(actorIndex, slot, aim);
                }
            }
        }

        // 誰にも当たらないなら詰める。最も近い敵へ1歩。
        if (bestScore <= 0f)
        {
            var mover = available
                .OrderBy(e => foes.Min(f => Chebyshev(f.GridPosition, e.GridPosition)))
                .First();
            var target = foes.OrderBy(f => Chebyshev(f.GridPosition, mover.GridPosition)).First();
            var step = mover.GridPosition + BattleTargeting.StepToward(
                mover.GridPosition, target.GridPosition);

            var walkable = BattleTargeting.SelectableTiles(mover, -1, grid, floor, sched.Roster);
            if (!walkable.Contains(step) && walkable.Count > 0)
                step = walkable.OrderBy(t => Chebyshev(t, target.GridPosition)).First();

            return new TurnInput(mine.IndexOf(mover), -1, step);
        }

        return best;
    }

    // 1手の得点。実際に当たる相手を TargetResolver 経由で数えるので、
    // 直線が味方に遮られる・範囲が味方を巻き込む、といった事情がそのまま
    // 点に反映される。
    private float ScoreAttack(Entity actor, int slot, Vector2I aim,
                              GridManager grid, FloorController floor,
                              IEnumerable<Entity> roster)
    {
        var data = actor.Moves.Slots[slot].Data;
        var victims = BattleTargeting.Victims(actor, slot, aim, grid, floor, roster);
        if (victims.Count == 0) return 0f;

        float score = 0f;
        foreach (var v in victims)
        {
            float hit = data.Power * Effectiveness(data, v) * (data.Accuracy / 100f);

            // 味方は損。巻き込む技を「当たるから」と撃たないための重み。
            if (v.Faction == actor.Faction) { score -= hit * 1.5f; continue; }

            score += hit;

            // 倒しきれる相手には上乗せする。頭数の差がそのまま
            // サイクルあたりの手数の差になるので、削るより落とすほうが得。
            if (hit >= v.Stats.CurrentHp) score += 120f;
        }
        return score;
    }

    // 相性倍率。実ダメージ計算は通さない（副作用があるうえ、相手の持ち物や
    // ランクまで読めてしまう）。撃ち分けの判断にはこれで足りる。
    private static float Effectiveness(MoveData data, Entity target)
    {
        var species = Species.SpeciesDatabase.Instance?.Get(target.SpeciesId);
        if (species == null || species.Types.Count == 0) return 1f;
        return TypeChartManager.GetMultiplier(
            data.Type, species.Types.Select(t => t.ToString()).ToArray());
    }

    private static int Chebyshev(Vector2I a, Vector2I b) =>
        System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));
}
