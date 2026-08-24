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

    // 相手の6匹（種族のみ）。選出フェーズの時点で開示されている情報
    // （§14）なので、選出の判断に使ってよい。技・持ち物・配置は
    // 引き続き分からない——このクラス自身、それらを画面へ渡す口を
    // 作っていないのと対称。
    public IReadOnlyList<PublicEntryView> FoeView { get; }

    // 対戦前に決めきる2つ。外からは読めるが、画面へ渡す口は作っていない
    // （相手の選出と配置は対戦開始まで開示しない — §14）。
    public IReadOnlyList<BattleEntry> Selection { get; private set; }
    public BattleDeployment Deployment { get; private set; }

    public NpcOpponent(NpcTeam profile, Faction faction = Entities.Faction.Enemy,
                       IReadOnlyList<PublicEntryView> foeView = null)
    {
        Profile = profile;
        Faction = faction;
        FoeView = foeView;
        Prepare();
    }

    public string Name => Profile?.Name ?? "NPC";

    // ---- 対戦前: 選出と配置 ----

    private void Prepare()
    {
        Selection = ChooseSelection();
        Deployment = ChooseDeployment(Selection);
    }

    // 6匹から4匹。相手の6匹（種族のみ、FoeView）が分かっていれば、その
    // 相手に対する相性を見て選ぶ——分からなければ（FoeView無し）従来どおり
    // 合計種族値と最大威力を見て上から採る。同点は乱数で崩す。毎回まったく
    // 同じ4匹だと、2戦目以降が作業になる。
    private List<BattleEntry> ChooseSelection()
    {
        if (Profile == null) return new List<BattleEntry>();

        var foeTypes = FoeView == null ? null : FoeView
            .Select(v => Species.SpeciesDatabase.Instance?.Get(v.SpeciesId)?.Types)
            .Where(t => t != null && t.Count > 0)
            .ToList();

        return Profile.Team.Entries
            .OrderByDescending(e => SelectionScore(e, foeTypes))
            .Take(BattleTeam.SelectionSize)
            .ToList();
    }

    // 1匹ぶんの選出スコア。専用の RandomNumberGenerator は持たない。
    // NpcOpponent は対戦のたびに new されるので、専用インスタンスだと
    // ヘッドレスで何百試合も回すほど未解放のネイティブオブジェクトが
    // 積み上がり後半だけ極端に遅くなる（BattleScheduler._rng を
    // GD.Randf() に切り替えた理由と同じ）。
    private float SelectionScore(BattleEntry e, List<List<Element>> foeTypes)
    {
        var sp = e.Species;
        int bst = sp == null ? 0 : sp.BaseHP + sp.BaseAtk + sp.BaseDef;
        int power = e.MoveIds.Select(m => MoveDatabase.Get(m)?.Power ?? 0)
                             .DefaultIfEmpty(0).Max();
        float jitter = GD.Randi() % 61;

        if (foeTypes == null || foeTypes.Count == 0)
            return bst + power * 2 + jitter;

        // 相手6匹それぞれに対して「こちらの最強打点がどれだけ通るか」
        // − 「相手の自属性（＝おそらくのSTAB）がこちらにどれだけ通るか」
        // を足し合わせる。技・持ち物・配置は分からないので、見えている
        // 種族の属性だけから両方向の相性を見積もる。
        var ownTypes = sp?.Types;
        var moves = e.MoveIds.Select(MoveDatabase.Get)
                             .Where(m => m != null && m.Power > 0).ToList();

        float matchup = 0f;
        foreach (var foe in foeTypes)
        {
            float off = moves.Count == 0 ? 0f
                : moves.Max(m => m.Power * TypeMultiplier(m.Type, foe));
            float def = ownTypes == null || ownTypes.Count == 0 ? 1f
                : foe.Sum(ft => TypeMultiplier(ft.ToString(), ownTypes));
            matchup += off - def * 40f;   // 被弾側は倍率(0〜4)を打点(数十〜百)と釣り合う尺度に上げる
        }

        return bst * 0.5f + matchup + jitter;
    }

    private static float TypeMultiplier(string atkType, List<Element> defTypes) =>
        TypeChartManager.GetMultiplier(atkType, defTypes.Select(t => t.ToString()).ToArray());

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
    //
    // 敵を倒すことだけを見て突っ込むと、削り合いで先に頭数が減る側が
    // そのまま押し切られる。**このマスに留まる/移る と、まだ動いていない
    // 敵からどれだけ狙われ得るか**を見積もり、危険な残高（HPに対して
    // 見積もり被害が大きい）のときは、攻撃よりも安全なマスへの退避を
    // 上回らせる。安全なら見積もりは0に近いので、従来どおり攻撃・接近が
    // 選ばれる——「ときには生存を優先する」の実装。
    public TurnInput Decide(BattleScheduler sched, GridManager grid, FloorController floor)
    {
        var mine = sched.Roster.Where(e => e.Faction == Faction).ToList();
        var available = mine.Where(e => e.IsAlive && !sched.HasActed(e)).ToList();
        if (available.Count == 0) return new TurnInput(-1, -1, Vector2I.Zero);

        var foes = sched.Roster.Where(e => e.IsAlive && e.Faction != Faction).ToList();
        if (foes.Count == 0) return new TurnInput(mine.IndexOf(available[0]), -1,
                                                 available[0].GridPosition);

        // このサイクル中にまだ動く可能性がある敵だけを脅威として数える。
        // 既に行動済みの敵は、次にこちらが動くまでの間はもう手を出せない。
        //
        // 各脅威の各攻撃技が「狙える先」は敵の位置と技の射程だけで決まり、
        // このDecide()呼び出しの間は変わらない。actor×移動先マスの数ぶん
        // 何度も引き直すと1万試合規模で重くなるので、ここで1回だけ求めて
        // 使い回す（EstimateThreat はこの結果を集合の当たり判定にしか使わない）。
        var threats = foes.Where(f => !sched.HasActed(f)).ToList();
        var threatMoves = new List<(Entity Foe, MoveData Data, HashSet<Vector2I> Reach)>();
        foreach (var foe in threats)
        {
            for (int slot = 0; slot < foe.Moves.Slots.Count; slot++)
            {
                var data = foe.Moves.Slots[slot].Data;
                if (data == null || data.Power <= 0 || foe.Moves.Slots[slot].CurrentPp <= 0) continue;

                // SelectableTiles は「狙わず即提出できる」技すべて（周囲・部屋・全体）を
                // 空リストで返す。部屋・全体は実際に盤面全体へ届くので null(=常に届く)
                // で正しいが、**周囲だけは自分中心3x3という狭い射程**——同じ「空」を
                // 「盤面全体に届く」と扱うと、周囲技を持つ敵1体だけでどのマスも
                // 危険域になり、過剰に退避し続けるバグになる。
                HashSet<Vector2I> reach;
                if (data.Range == MoveRange.Surrounding)
                {
                    reach = new HashSet<Vector2I>();
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            reach.Add(foe.GridPosition + new Vector2I(dx, dy));
                }
                else
                {
                    var list = BattleTargeting.SelectableTiles(foe, slot, grid, floor, sched.Roster);
                    reach = list.Count > 0 ? new HashSet<Vector2I>(list) : null;   // null = 部屋・全体
                }
                threatMoves.Add((foe, data, reach));
            }
        }

        // 出せる全員 × 技4つ × 狙える全マス、加えて移動先の全マスを
        // 総当たりして、最も得点の高い1手を採る。盤面は56マスで候補も
        // 高々数百なので、素直に全部見たほうが「なぜその手を選んだか」を追える。
        TurnInput best = default;
        float bestScore = float.NegativeInfinity;

        foreach (var actor in available)
        {
            int actorIndex = mine.IndexOf(actor);

            // 攻撃技は原則その場に留まる（Adjacent以外は距離を詰めない）ので、
            // 留まった場合の被弾リスクは技によらず共通——aimごとに引き直さない。
            float stayThreat = EstimateThreat(actor, actor.GridPosition, threatMoves);
            float stayPenalty = SurvivalPenalty(actor, stayThreat, sched.CycleNumber);

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
                    float offense = ScoreAttack(actor, slot, aim, grid, floor, sched.Roster);
                    if (offense <= 0f) continue;   // 当たらない/意味の無い手は候補にしない

                    float score = offense - stayPenalty;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = new TurnInput(actorIndex, slot, aim);
                }
            }

            // 移動先の候補。危険な場所に留まって攻撃するより、安全な場所へ
            // 退くほうが得点が高ければこちらが選ばれる。脅威が無ければ
            // 全マスの見積もりが0近辺で並ぶので、接近ボーナスが tie-break し、
            // 従来どおり敵へ寄っていく。
            var moveTiles = BattleTargeting.SelectableTiles(actor, -1, grid, floor, sched.Roster);
            foreach (var tile in moveTiles)
            {
                float destThreat = EstimateThreat(actor, tile, threatMoves);
                float score = ApproachBonus(actor.GridPosition, tile, foes)
                             - SurvivalPenalty(actor, destThreat, sched.CycleNumber);
                if (score <= bestScore) continue;
                bestScore = score;
                best = new TurnInput(actorIndex, -1, tile);
            }
        }

        // 何も候補が無かった（移動先すら無い＝完全に固定されている）ときだけ、
        // その場に留まる扱いにする保険。
        if (bestScore == float.NegativeInfinity)
        {
            var mover = available.First();
            return new TurnInput(mine.IndexOf(mover), -1, mover.GridPosition);
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

    // そのマスに居るとき、まだ動いていない敵からどれだけ狙われ得るかの
    // 見積もり。実ダメージ計算は通さない（ScoreAttack と同じ理由）。
    // 各敵の最も痛い1手を採って足し合わせる——複数の敵がこのサイクル中に
    // まだ動けるなら、その全員から狙われ得るという悲観的な見積もり。
    //
    // 「狙える先」自体は Decide() 側で1回だけ求めて渡してもらう
    // （敵の位置と技の射程だけで決まり、tile ごとに引き直す必要が無い）。
    private static float EstimateThreat(Entity actor, Vector2I tile,
        IReadOnlyList<(Entity Foe, MoveData Data, HashSet<Vector2I> Reach)> threatMoves)
    {
        if (threatMoves.Count == 0) return 0f;

        Dictionary<Entity, float> worstPerFoe = null;
        foreach (var (foe, data, reach) in threatMoves)
        {
            if (reach != null && !reach.Contains(tile)) continue;

            float hit = data.Power * Effectiveness(data, actor) * (data.Accuracy / 100f);
            worstPerFoe ??= new Dictionary<Entity, float>();
            if (!worstPerFoe.TryGetValue(foe, out var cur) || hit > cur) worstPerFoe[foe] = hit;
        }
        return worstPerFoe == null ? 0f : worstPerFoe.Values.Sum();
    }

    // 見積もり被害をHPに対する比率で見て、危険なときだけ強く効かせる。
    // ratio<<1（掠り傷程度）ならほぼ0のまま従来の判断を変えず、
    // ratio>=1（このサイクル中に落とされ得る）で強く効かせる。2乗にして
    // いるのは「軽い脅威は無視、重い脅威は強く避ける」という質的な切り替えに
    // したいため（線形だと中程度の脅威でも常に慎重になりすぎる）。
    //
    // 上限は控えめ（最大約102）に抑えてある。強い決定打（急所級の一撃や
    // 撃破ボーナス+120を含む ScoreAttack は軽く150〜300を超える）を、
    // 「ときには生存を優先する」という程度の重みで覆してしまわないように
    // ——退避が優先されるのは、攻めても大した戦果が無くはっきり分が悪い
    // 局面に絞る。
    //
    // cycleNumber が経るほど係数を落とす（урgency）。両者が互いを
    // 「危険」と見て退き合うと、幾何によっては安全なマスへ逃げ切れず
    // 何サイクルも足踏みし続けることがある——最初にこの実装で1万試合中
    // 一部が数百サイクルに達し、対戦上限(600提出)で未決着になった。
    // 長引くほど慎重さより決着を優先させることで、この足踏みを抜けさせる。
    private static float SurvivalPenalty(Entity actor, float threat, int cycleNumber)
    {
        if (threat <= 0f) return 0f;
        float ratio = threat / System.MathF.Max(1f, actor.Stats.CurrentHp);
        ratio = Mathf.Min(ratio, 1.3f);
        float urgency = Mathf.Max(0.25f, 1f - cycleNumber * 0.04f);
        return 60f * ratio * ratio * urgency;
    }

    // 危険が無い場面のtie-break用。敵に近づくほど小さく加点し、脅威が
    // 拮抗しているとき（EstimateThreatが横並び）でも接近・詰めが選ばれる
    // 従来の挙動を保つ。
    private static float ApproachBonus(Vector2I from, Vector2I to, IReadOnlyList<Entity> foes)
    {
        int before = foes.Min(f => Chebyshev(f.GridPosition, from));
        int after = foes.Min(f => Chebyshev(f.GridPosition, to));
        return (before - after) * 2f;
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
