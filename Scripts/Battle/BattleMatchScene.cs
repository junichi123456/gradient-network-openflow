using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;
using MysteryDungeon.Species;
using MysteryDungeon.Turn;

namespace MysteryDungeon.Battle;

// 1試合を最後まで回すヘッドレスの試合場。
//
// BattleTestScene は部品ごとの検証で、決着までは1試合しか通さない。
// こちらは「どちらかが全滅するまで」を目的にした専用の入口で、
// 持ち物の有無や相手を替えて何試合でも回せる。
//
//   godot --headless --path . Scenes/BattleMatchScene.tscn -- \
//         --home player --away npc_fire --matches 20 [--items]
//
// --items を付けない限り**両陣営とも持ち物を全部外す**。
// 対戦の骨格（射程・行動順・サイクル）だけで決着まで行けるかを見るため。
//
// ---- 完全ランダム構築モード ----
//
//   godot --headless --path . Scenes/BattleMatchScene.tscn -- \
//         --random --matches 100
//
// --random を付けると --home/--away/--items は無視され、**毎試合ごとに**
// 自陣・敵陣とも種族・技・持ち物のすべてを一から無作為に組み直す
// （NPCの学習済み編成には一切頼らない、素の対戦システムの検証）。
//   - 6匹の種族はランダムに重複なく選ぶ
//   - 各自の技4つは、その種族の learnset からランダムに重複なく選ぶ
//     （威力や属性で選ばない——DefaultLoadout とは別系統）
//   - 6匹**全員**に、対戦用持ち物16種から重複なく1つずつ持たせる
//     （持たせない選択肢は無い。1匹1つ・チーム内重複不可は既存の規則どおり）
// 行動判断（選出・配置・毎ターン）は NpcOpponent の同じ1本の判断ロジックを
// 両陣営に使う。**構築だけを無作為化し、判断は固定する**ことで、勝敗が
// 構築の強さ（種族値・持ち物の質）に意味のある形で応答しているか
// ——すなわちAIが「でたらめではない、筋の通った行動」を取れているか
// ——を統計的に見られるようにしてある。
//
// ---- 「最も強い構築」総当たりモード ----
//
//   godot --headless --path . Scenes/BattleMatchScene.tscn -- \
//         --strongest --teams 100 --repeat 10 [--shard K --shards N] [--out path.csv]
//
// 種族値上位（`StrongPoolSize`）のプールから技はDefaultLoadout（強い技を
// 優先）、持ち物は重複なく必ず全員へ、で「強いと思える構築」を --teams 件
// 無作為に組む（同一の6匹構成は作り直す）。乱数シードは固定なので、
// --shard を割っても全プロセスが同じ100構築を再現する。
// 全組み合わせ（Nチームなら N*(N-1)/2 通り）を --repeat 回ずつ対戦させ、
// 1戦ごとに先手/後手を入れ替える（先手側の利がある場合の偏りを消す）。
// --shard K --shards N を付けると、担当ペアだけを処理する
// （ペア番号 % N == K）。計算量が Nチーム²のオーダーで伸びるため
// （100チームで組み合わせ4,950通り×10戦=49,500試合）、複数プロセスへ
// 分けて回せるようにしてある。--out を付けると `i,j,勝ちi,勝ちj,引分,未決着,
// サイクル|区切り` を1ペア1行でCSV追記する（マージ・集計用）。
public partial class BattleMatchScene : Node2D
{
    // 手持ちの既定編成。構築画面がまだ編集に対応していないので、
    // BattleFlowScene と同じ6匹を使う。
    private static readonly string[] PlayerRoster = { "001", "004", "006", "009", "002", "010" };

    private const int TurnCap = 600;     // 1試合あたりの提出回数の上限
    private const int StrongPoolSize = 60;   // 「強い構築」の候補プール（種族値上位から）

    private string _home = "player";
    private string _away = null;         // 既定は先頭のNPC
    private int _matches = 1;
    private string _items = "none";      // none / both / home / away
    private bool _random;

    private bool _strongest;
    private int _teamCount = 100;
    private int _repeat = 10;
    private int _shardIndex;
    private int _shardCount = 1;
    private string _outPath;

    private int _challengerCount = 30;
    private int _loopSize = 5;
    private int _loops = 4;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _random = args.Contains("--random");
        _home = Arg(args, "--home") ?? _home;
        _away = Arg(args, "--away");
        _matches = int.TryParse(Arg(args, "--matches"), out var n) ? n : 1;
        // --items だけなら両陣営に持たせる。片側だけに持たせて、持ち物が
        // どちらの側を利しているかを切り分けられるようにしてある。
        int ai = System.Array.IndexOf(args, "--items");
        if (ai >= 0)
            _items = ai + 1 < args.Length && !args[ai + 1].StartsWith("--")
                ? args[ai + 1] : "both";

        _strongest = args.Contains("--strongest");
        // --tactics は既定値が違う（35チーム・1組4戦・5ループ）ので、
        // 指定が無いときの既定をモードごとに分ける。
        bool tactics = args.Contains("--tactics");
        _teamCount = int.TryParse(Arg(args, "--teams"), out var tc) ? tc : (tactics ? 35 : 100);
        _repeat = int.TryParse(Arg(args, "--repeat"), out var rp) ? rp : (tactics ? 4 : 10);
        _shardIndex = int.TryParse(Arg(args, "--shard"), out var si) ? si : 0;
        _shardCount = int.TryParse(Arg(args, "--shards"), out var sc) ? sc : 1;
        _outPath = Arg(args, "--out");
        _challengerCount = int.TryParse(Arg(args, "--challengers"), out var cc) ? cc : 30;
        _loopSize = int.TryParse(Arg(args, "--loop-size"), out var ls) ? ls : 5;
        _loops = int.TryParse(Arg(args, "--loops"), out var lp) ? lp : (tactics ? 5 : 4);

        if (args.Contains("--dump-meta"))
        {
            var hundred = MetaScenario.RegenerateHundred();
            foreach (var id in MetaScenario.MetaIds)
                GD.Print($"{id}: " + string.Join(" / ", hundred[id].Entries.Select(e =>
                    $"{e.SpeciesId}[{string.Join(",", e.MoveIds)}][{e.ItemId}]")));
            GetTree().Quit();
            return;
        }

        if (args.Contains("--dump-challengers"))
        {
            var hundred2 = MetaScenario.RegenerateHundred();
            var metas2 = MetaScenario.MetaIds.Select(id => hundred2[id]).ToList();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var challengers = MetaScenario.GenerateChallengers(metas2, _challengerCount);
            sw.Stop();
            GD.Print($"[対抗構築] {challengers.Count}件生成（{sw.ElapsedMilliseconds}ms）");
            foreach (var cat in challengers.GroupBy(c => string.Join(",", c.Beats.OrderBy(x => x))))
                GD.Print($"  カテゴリ{{{cat.Key}}}: {cat.Count()}件");
            for (int i = 0; i < challengers.Count; i++)
            {
                var (team, beats, adv) = challengers[i];
                GD.Print($"  #{i} beats={{{string.Join(",", beats.OrderBy(x => x))}}} adv={adv:F0}: "
                         + string.Join(" / ", team.Entries.Select(e => e.SpeciesId)));
            }
            GetTree().Quit();
            return;
        }

        if (tactics) { RunTactics(); return; }
        if (args.Contains("--meta-core")) { RunMetaCore(); return; }
        if (args.Contains("--meta-challengers")) { RunMetaChallengers(); return; }
        if (_strongest) { RunStrongestRoundRobin(); return; }
        if (_random) { RunRandomBatch(); return; }

        var homeTeam = TeamOf(_home);
        var awayProfile = ProfileOf(_away) ?? NpcTeamDatabase.First();

        if (_items != "both" && _items != "home") homeTeam = StripItems(homeTeam);
        if (_items != "both" && _items != "away") awayProfile = StripItems(awayProfile);

        GD.Print($"[試合] {Label(_home)} vs {awayProfile.Name}"
                 + $" / 持ち物 {_items} / {_matches}試合");
        GD.Print($"[試合] 持ち物の総数: 自陣 {CountItems(homeTeam)} / 敵陣 {CountItems(awayProfile.Team)}");

        int homeWin = 0, awayWin = 0, draw = 0, unresolved = 0;
        var cycles = new List<int>();

        for (int i = 0; i < _matches; i++)
        {
            var r = RunMatch(homeTeam, awayProfile, verbose: i == 0);
            switch (r.Outcome)
            {
                case BattleOutcome.PlayerWin: homeWin++; break;
                case BattleOutcome.EnemyWin: awayWin++; break;
                case BattleOutcome.Draw: draw++; break;
                default: unresolved++; break;
            }
            cycles.Add(r.Cycles);
        }

        GD.Print("");
        GD.Print($"[結果] {_matches}試合: 自陣{homeWin}勝 / 敵陣{awayWin}勝 "
                 + $"/ 引き分け{draw} / 未決着{unresolved}");
        if (cycles.Count > 0)
            GD.Print($"[結果] 決着までのサイクル: 平均{cycles.Average():F1} "
                     + $"/ 最短{cycles.Min()} / 最長{cycles.Max()}");

        GetTree().Quit();
    }

    // 完全ランダム構築モード。毎試合ごとに両陣営を無作為に組み直して回す。
    //
    // 「AIが筋の通った行動を取れているか」を測る手がかりとして、
    // **選出された4匹ぶんの合計種族値が高い側と、実際に勝った側が
    // 一致する割合**を集計する。構築の強さと勝敗が無関係（50%前後）なら
    // 行動判断が機能していない証拠、はっきり50%を超えていれば
    // 「強い構築を活かせている」という有意な行動の証拠になる。
    private void RunRandomBatch()
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        GD.Print($"[試合] 完全ランダム構築（種族・技・持ち物すべて無作為、"
                 + $"持ち物は必ず全員に付与）/ {_matches}試合");

        int homeWin = 0, awayWin = 0, draw = 0, unresolved = 0;
        var cycles = new List<int>();
        int bstAgree = 0, bstTie = 0, bstDecided = 0, buildViolations = 0;

        for (int i = 0; i < _matches; i++)
        {
            var homeTeam = RandomTeam(rng);
            var awayTeam = RandomTeam(rng);

            // 無作為抽出の実装ミスを見逃さないための機械検証。RosterSize(6)
            // <= 対戦用持ち物の種類(16) なので理屈上は必ず満たすはずだが、
            // 「必ず全員に持ち物」は RandomTeam の自己申告でしかないので、
            // 実際に組んだチームで毎回確かめる。
            bool okHome = homeTeam.Validate().Count == 0
                          && homeTeam.Entries.All(e => !string.IsNullOrEmpty(e.ItemId));
            bool okAway = awayTeam.Validate().Count == 0
                          && awayTeam.Entries.All(e => !string.IsNullOrEmpty(e.ItemId));
            if (!okHome || !okAway) buildViolations++;

            var awayProfile = new NpcTeam
            {
                Id = $"random_{i}", Name = "ランダム構築の相手", MainType = "Neutral",
                TotalBst = awayTeam.Entries.Sum(e => Bst(e)), Team = awayTeam,
            };

            var r = RunMatch(homeTeam, awayProfile, verbose: i == 0);
            switch (r.Outcome)
            {
                case BattleOutcome.PlayerWin: homeWin++; break;
                case BattleOutcome.EnemyWin: awayWin++; break;
                case BattleOutcome.Draw: draw++; break;
                default: unresolved++; break;
            }
            cycles.Add(r.Cycles);

            if (r.Outcome == BattleOutcome.PlayerWin || r.Outcome == BattleOutcome.EnemyWin)
            {
                if (r.HomeBst == r.AwayBst) bstTie++;
                else
                {
                    bstDecided++;
                    bool homeHigher = r.HomeBst > r.AwayBst;
                    bool homeWonMatch = r.Outcome == BattleOutcome.PlayerWin;
                    if (homeHigher == homeWonMatch) bstAgree++;
                }
            }
        }

        GD.Print("");
        GD.Print($"[検証] 毎試合とも構築規則を満たし、全員が持ち物を持つ: "
                 + $"{(buildViolations == 0 ? "OK" : $"NG {buildViolations}試合")}");
        GD.Print($"[結果] {_matches}試合: 自陣{homeWin}勝 / 敵陣{awayWin}勝 "
                 + $"/ 引き分け{draw} / 未決着{unresolved}");
        if (cycles.Count > 0)
            GD.Print($"[結果] 決着までのサイクル: 平均{cycles.Average():F1} "
                     + $"/ 最短{cycles.Min()} / 最長{cycles.Max()}");
        if (bstDecided > 0)
            GD.Print($"[結果] 選出4匹の合計種族値が高い側が勝った割合: "
                     + $"{100.0 * bstAgree / bstDecided:F1}% ({bstAgree}/{bstDecided}、種族値同点{bstTie}試合を除く)");

        GetTree().Quit();
    }

    // 「最も強い構築」N通りを組み、全組み合わせを --repeat 回ずつ対戦させる。
    // 乱数シードを固定しているのは、--shard で複数プロセスに分けても
    // 全プロセスが同じN構築を再現できるようにするため（構築の生成に
    // 使う乱数列と対戦中の行動判断の乱数列は別物——後者は GD.Randf/GD.Randi
    // という大域RNGなので、こちらのシード固定の影響を受けない）。
    private void RunStrongestRoundRobin()
    {
        var rng = new RandomNumberGenerator();
        rng.Seed = MetaScenario.Seed;

        var pool = MetaScenario.StrongPool(StrongPoolSize);

        var teams = new List<BattleTeam>();
        var seen = new HashSet<string>();
        for (int guard = 0; teams.Count < _teamCount && guard < _teamCount * 200; guard++)
        {
            var t = MetaScenario.StrongestTeam(pool, rng);
            var key = string.Join(",", t.Entries.Select(e => e.SpeciesId).OrderBy(x => x));
            if (!seen.Add(key)) continue;
            teams.Add(t);
        }

        GD.Print($"[強構築] 種族値上位{pool.Count}種から{teams.Count}通りを生成");

        // 担当ペア以外を素通りする都合上、必ず同じ順でペアを列挙する
        // （ペア番号 = shard割当の鍵）。
        var pairs = new List<(int I, int J)>();
        for (int i = 0; i < teams.Count; i++)
            for (int j = i + 1; j < teams.Count; j++)
                pairs.Add((i, j));

        if (_shardIndex == 0 && !string.IsNullOrEmpty(_outPath))
        {
            var manifest = teams.Select((t, idx) =>
                $"{idx}: " + string.Join(" / ", t.Entries.Select(e =>
                    $"{SpeciesDatabase.Instance?.Get(e.SpeciesId)?.DisplayName ?? e.SpeciesId}"
                    + $"[{e.ItemId}]")));
            System.IO.File.WriteAllLines(_outPath + ".teams.txt", manifest);
        }

        var wins = new int[teams.Count];
        var losses = new int[teams.Count];
        var draws = new int[teams.Count];
        var undecided = new int[teams.Count];
        var csvLines = new List<string>();

        long done = 0, totalAssigned = pairs.Count(p => (p.I * teams.Count + p.J) % _shardCount == _shardIndex);
        var allCycles = new List<int>();

        foreach (var (i, j) in pairs)
        {
            if ((i * teams.Count + j) % _shardCount != _shardIndex) continue;

            int winI = 0, winJ = 0, draw = 0, undecidedPair = 0;
            var cycles = new List<int>();
            for (int k = 0; k < _repeat; k++)
            {
                bool iHome = k % 2 == 0;   // 先手/後手を戦ごとに入れ替え、先手側の利を消す
                var homeTeam = iHome ? teams[i] : teams[j];
                var awayTeam = iHome ? teams[j] : teams[i];
                var awayProfile = new NpcTeam
                {
                    Id = $"strong_{(iHome ? j : i)}", Name = "強構築の相手",
                    MainType = "Neutral", Team = awayTeam,
                };

                var r = RunMatch(homeTeam, awayProfile, verbose: false);
                cycles.Add(r.Cycles);
                switch (r.Outcome)
                {
                    case BattleOutcome.PlayerWin: if (iHome) winI++; else winJ++; break;
                    case BattleOutcome.EnemyWin: if (iHome) winJ++; else winI++; break;
                    case BattleOutcome.Draw: draw++; break;
                    default: undecidedPair++; break;
                }
            }

            wins[i] += winI; wins[j] += winJ;
            losses[i] += winJ; losses[j] += winI;
            draws[i] += draw; draws[j] += draw;
            undecided[i] += undecidedPair; undecided[j] += undecidedPair;
            allCycles.AddRange(cycles);
            csvLines.Add($"{i},{j},{winI},{winJ},{draw},{undecidedPair},{string.Join('|', cycles)}");

            done++;
            if (done % 50 == 0)
                GD.Print($"[強構築] 担当ペア {done}/{totalAssigned} 完了");
        }

        if (!string.IsNullOrEmpty(_outPath))
            System.IO.File.AppendAllLines(_outPath, csvLines);

        GD.Print("");
        GD.Print($"[結果] shard {_shardIndex}/{_shardCount}: 担当{csvLines.Count}ペア"
                 + $"（{csvLines.Count * (long)_repeat}試合）処理完了");
        if (allCycles.Count > 0)
            GD.Print($"[結果] 決着までのサイクル: 平均{allCycles.Average():F1} "
                     + $"/ 最短{allCycles.Min()} / 最長{allCycles.Max()}");

        // shard分割時、この勝敗集計はこのプロセスが担当したペアのぶんだけ
        // ——全体のランキングは --out のCSVを全shardぶんマージしてから出す
        // （Tools/merge_strongest_results.py）。
        var ranking = Enumerable.Range(0, teams.Count)
            .Select(idx => (idx, played: wins[idx] + losses[idx] + draws[idx] + undecided[idx]))
            .Where(x => x.played > 0)
            .OrderByDescending(x => (double)wins[x.idx] / x.played)
            .Take(10);
        GD.Print("[結果] このshard内・勝率上位10構築:");
        foreach (var (idx, played) in ranking)
            GD.Print($"  #{idx}: {wins[idx]}勝{losses[idx]}敗{draws[idx]}分{undecided[idx]}未決着"
                     + $"（{100.0 * wins[idx] / played:F1}%、{played}試合）");

        GetTree().Quit();
    }

    // ---- §21/§22: 環境メタ（3すくみ）と、それに対抗する30構築 ----
    //
    //   godot --headless -- --meta-core --loop-size 5 --loops 4 [--challengers 30] --out path
    //   godot --headless -- --meta-challengers --shard K --shards N --repeat 20 --out path
    //
    // --meta-core: 3すくみ（#66→#53→#30、§21）どうしの3ペアと、3すくみ×
    // 対抗構築30件=90ペアを、この1プロセスで順番に処理する。3すくみの
    // 各構築は5戦のループごとに適応しうる（MetaScenario.AdaptAfterLoop）ので
    // 状態が試合をまたいで持ち越る——並列化すると引き継ぎが壊れるため、
    // ここだけは常に単一プロセス（shard非対応）。93ペア×20戦=1,860試合。
    //
    // --meta-challengers: 対抗構築どうしの総当たり（C(30,2)=435ペア、
    // どちらも適応しない静的な構築どうし）。--strongest と同じ形で
    // shard分割できる。
    private readonly struct PairResult
    {
        public int WinsA { get; init; }
        public int WinsB { get; init; }
        public int Draws { get; init; }
        public int Undecided { get; init; }
        public List<int> Cycles { get; init; }
    }

    // 5戦（既定）ぶんを1ループ実行し、両陣営の勝敗とサイクル数、
    // 各陣営で戦闘不能になった個体を種族IDごとに集計して返す。
    // gameOffset は先手/後手の入れ替えを試合番号ではなく通算で回すための
    // オフセット（ループをまたいでも交互になるように）。
    private (int WinsA, int WinsB, int Draws, int Undecided, List<int> Cycles,
             Dictionary<string, int> FaintsA, Dictionary<string, int> FaintsB)
        RunLoop(BattleTeam teamA, BattleTeam teamB, int gamesInLoop, int gameOffset)
    {
        int winsA = 0, winsB = 0, draws = 0, undecided = 0;
        var cycles = new List<int>();
        var faintsA = new Dictionary<string, int>();
        var faintsB = new Dictionary<string, int>();

        for (int k = 0; k < gamesInLoop; k++)
        {
            bool aHome = (gameOffset + k) % 2 == 0;
            var homeTeam = aHome ? teamA : teamB;
            var awayTeam = aHome ? teamB : teamA;
            var awayProfile = new NpcTeam
            {
                Id = "meta_tmp", Name = "メタ対戦", MainType = "Neutral", Team = awayTeam,
            };

            var r = RunMatch(homeTeam, awayProfile, verbose: false);
            cycles.Add(r.Cycles);

            var aliveA = aHome ? r.HomeAlive : r.AwayAlive;
            var aliveB = aHome ? r.AwayAlive : r.HomeAlive;
            foreach (var (sp, alive) in aliveA) if (!alive) faintsA[sp] = faintsA.GetValueOrDefault(sp) + 1;
            foreach (var (sp, alive) in aliveB) if (!alive) faintsB[sp] = faintsB.GetValueOrDefault(sp) + 1;

            bool aWon = (aHome && r.Outcome == BattleOutcome.PlayerWin)
                     || (!aHome && r.Outcome == BattleOutcome.EnemyWin);
            bool bWon = (aHome && r.Outcome == BattleOutcome.EnemyWin)
                     || (!aHome && r.Outcome == BattleOutcome.PlayerWin);
            if (aWon) winsA++;
            else if (bWon) winsB++;
            else if (r.Outcome == BattleOutcome.Draw) draws++;
            else undecided++;
        }
        return (winsA, winsB, draws, undecided, cycles, faintsA, faintsB);
    }

    // 1組の対戦カードを --loops 回ぶん（各 --loop-size 戦）通す。
    // aIsMeta/bIsMeta が true の側は、ループの合間に適応の機会を得る
    // （勝率が閾値未満なら、そのループで一番倒れた1匹の技か持ち物を1つ
    // 変える——§22）。適応結果は mutationLog に積む。
    private PairResult RunAdaptivePair(BattleTeam teamA, bool aIsMeta, BattleTeam teamB, bool bIsMeta,
        List<string> mutationLog, string labelA, string labelB)
    {
        int totalA = 0, totalB = 0, totalDraws = 0, totalUndecided = 0;
        var allCycles = new List<int>();
        int gameOffset = 0;

        for (int loop = 0; loop < _loops; loop++)
        {
            var res = RunLoop(teamA, teamB, _loopSize, gameOffset);
            gameOffset += _loopSize;
            totalA += res.WinsA; totalB += res.WinsB;
            totalDraws += res.Draws; totalUndecided += res.Undecided;
            allCycles.AddRange(res.Cycles);

            if (aIsMeta)
            {
                var msg = MetaScenario.AdaptAfterLoop(teamA, teamB, res.WinsA, _loopSize, res.FaintsA);
                if (msg != null) mutationLog.Add($"[{labelA} vs {labelB} / loop{loop + 1}] {msg}");
            }
            if (bIsMeta)
            {
                var msg = MetaScenario.AdaptAfterLoop(teamB, teamA, res.WinsB, _loopSize, res.FaintsB);
                if (msg != null) mutationLog.Add($"[{labelB} vs {labelA} / loop{loop + 1}] {msg}");
            }
        }

        return new PairResult
        {
            WinsA = totalA, WinsB = totalB, Draws = totalDraws,
            Undecided = totalUndecided, Cycles = allCycles,
        };
    }

    private void RunMetaCore()
    {
        var hundred = MetaScenario.RegenerateHundred();
        var metaTeams = MetaScenario.MetaIds.Select(id => hundred[id]).ToList();
        var challengers = MetaScenario.GenerateChallengers(metaTeams, _challengerCount);

        GD.Print($"[メタ] 3すくみ基準: {string.Join(", ", MetaScenario.MetaIds.Select(id => $"#{id}"))}");
        GD.Print($"[メタ] 対抗構築 {challengers.Count}件（既存2/3体以上に有利な構築のみ）");
        GD.Print($"[メタ] 1カード = {_loopSize}戦×{_loops}ループ = {_loopSize * _loops}戦");
        GD.Print("");

        var mutationLog = new List<string>();
        var csvLines = new List<string>();

        // 3すくみ内の3ペア。両陣営とも適応する。
        for (int a = 0; a < metaTeams.Count; a++)
        for (int b = a + 1; b < metaTeams.Count; b++)
        {
            string la = $"M{a}(#{MetaScenario.MetaIds[a]})", lb = $"M{b}(#{MetaScenario.MetaIds[b]})";
            var res = RunAdaptivePair(metaTeams[a], true, metaTeams[b], true, mutationLog, la, lb);
            csvLines.Add($"meta,{a},{b},{res.WinsA},{res.WinsB},{res.Draws},{res.Undecided},"
                         + $"{string.Join('|', res.Cycles)}");
            GD.Print($"[メタ] {la} vs {lb}: {res.WinsA}勝{res.WinsB}敗"
                     + $"（分{res.Draws}/未決着{res.Undecided}）");
        }

        // 各メタ × 対抗構築30件。メタ側だけが適応する。
        for (int m = 0; m < metaTeams.Count; m++)
        {
            string lm = $"M{m}(#{MetaScenario.MetaIds[m]})";
            int mWins = 0, mLosses = 0;
            for (int c = 0; c < challengers.Count; c++)
            {
                var res = RunAdaptivePair(metaTeams[m], true, challengers[c].Team, false,
                    mutationLog, lm, $"C{c}");
                csvLines.Add($"vs,{m},{c},{res.WinsA},{res.WinsB},{res.Draws},{res.Undecided},"
                             + $"{string.Join('|', res.Cycles)}");
                mWins += res.WinsA; mLosses += res.WinsB;
            }
            GD.Print($"[メタ] {lm} vs 対抗構築30件: 通算{mWins}勝{mLosses}敗"
                     + $"（{challengers.Count * _loopSize * _loops}戦中）");
        }

        if (!string.IsNullOrEmpty(_outPath))
        {
            System.IO.File.WriteAllLines(_outPath, csvLines);
            System.IO.File.WriteAllLines(_outPath + ".mutations.txt", mutationLog);

            var metaManifest = metaTeams.Select((t, i) =>
                $"M{i}(#{MetaScenario.MetaIds[i]}): " + string.Join(" / ", t.Entries.Select(e =>
                    $"{e.SpeciesId}[{string.Join(",", e.MoveIds)}][{e.ItemId}]")));
            System.IO.File.WriteAllLines(_outPath + ".meta_final.txt", metaManifest);

            var challengerManifest = challengers.Select((c, i) =>
                $"C{i} beats={{{string.Join(",", c.Beats.OrderBy(x => x))}}}: "
                + string.Join(" / ", c.Team.Entries.Select(e => $"{e.SpeciesId}[{e.ItemId}]")));
            System.IO.File.WriteAllLines(_outPath + ".challengers.txt", challengerManifest);
        }

        GD.Print("");
        GD.Print($"[メタ] 適応イベント数: {mutationLog.Count}");
        foreach (var line in mutationLog) GD.Print("  " + line);

        GetTree().Quit();
    }

    // 対抗構築どうしの総当たり（静的、shard分割可）。
    private void RunMetaChallengers()
    {
        var hundred = MetaScenario.RegenerateHundred();
        var metaTeams = MetaScenario.MetaIds.Select(id => hundred[id]).ToList();
        var challengers = MetaScenario.GenerateChallengers(metaTeams, _challengerCount)
            .Select(c => c.Team).ToList();

        var pairs = new List<(int I, int J)>();
        for (int i = 0; i < challengers.Count; i++)
            for (int j = i + 1; j < challengers.Count; j++)
                pairs.Add((i, j));

        var csvLines = new List<string>();
        long done = 0;
        long totalAssigned = pairs.Count(p => (p.I * challengers.Count + p.J) % _shardCount == _shardIndex);
        var allCycles = new List<int>();
        int repeat = _loopSize * _loops;   // 既定 5×4=20（--loop-size/--loops で調整）

        foreach (var (i, j) in pairs)
        {
            if ((i * challengers.Count + j) % _shardCount != _shardIndex) continue;

            int winI = 0, winJ = 0, draw = 0, undecided = 0;
            var cycles = new List<int>();
            for (int k = 0; k < repeat; k++)
            {
                bool iHome = k % 2 == 0;
                var homeTeam = iHome ? challengers[i] : challengers[j];
                var awayTeam = iHome ? challengers[j] : challengers[i];
                var awayProfile = new NpcTeam
                {
                    Id = $"chal_{(iHome ? j : i)}", Name = "対抗構築の相手",
                    MainType = "Neutral", Team = awayTeam,
                };

                var r = RunMatch(homeTeam, awayProfile, verbose: false);
                cycles.Add(r.Cycles);
                switch (r.Outcome)
                {
                    case BattleOutcome.PlayerWin: if (iHome) winI++; else winJ++; break;
                    case BattleOutcome.EnemyWin: if (iHome) winJ++; else winI++; break;
                    case BattleOutcome.Draw: draw++; break;
                    default: undecided++; break;
                }
            }

            allCycles.AddRange(cycles);
            csvLines.Add($"{i},{j},{winI},{winJ},{draw},{undecided},{string.Join('|', cycles)}");

            done++;
            if (done % 50 == 0)
                GD.Print($"[対抗構築] 担当ペア {done}/{totalAssigned} 完了");
        }

        if (!string.IsNullOrEmpty(_outPath))
            System.IO.File.AppendAllLines(_outPath, csvLines);

        GD.Print("");
        GD.Print($"[結果] shard {_shardIndex}/{_shardCount}: 担当{csvLines.Count}ペア"
                 + $"（{csvLines.Count * (long)repeat}試合）処理完了");
        if (allCycles.Count > 0)
            GD.Print($"[結果] 決着までのサイクル: 平均{allCycles.Average():F1} "
                     + $"/ 最短{allCycles.Min()} / 最長{allCycles.Max()}");

        GetTree().Quit();
    }

    // ---- §25: 5戦術・35チームの適応総当たり ----
    //
    //   godot --headless -- --tactics [--teams 35 --repeat 4 --loops 5] --out path
    //
    // 5戦術（TacticalBuilder.Tactic）から2つずつ選んだ10通りの組を35チームへ
    // 割り当て、C(35,2)=595ペア×4戦を1ループとして5ループ回す（計11,900試合）。
    // **1試合ごとに、両陣営とも「いま戦っている相手」に勝てるよう6匹を
    // 組み直す**——種族も含めて自由に入れ替わる。
    //
    // 組み直しは互いに相手の**直前の構築**を見て行う（同時に組み替えるので、
    // 相手の"次"の構築は原理的に読めない）。両者ぶんを先にスナップショット
    // してから同時に組み直すため、先後の有利不利は生じない。
    //
    // 全チームが適応し、その状態が試合をまたいで持ち越るので shard 分割は
    // できない（並列化すると「どの試合がどの順に起きたか」が壊れる）。
    private sealed class TacticTeam
    {
        public int Index;
        public Tactic A, B;
        public BattleTeam Build;
        // 種族ごとの自己評価。「その種族を入れた試合の勝率」を -1..+1 に写す
        // ——固定の加減点だとすぐ振り切れて差が潰れるので、勝率そのものを使う。
        public readonly Dictionary<string, int> Plays = new();
        public readonly Dictionary<string, int> Won = new();
        public int Wins, Losses, Draws, Undecided;

        public Dictionary<string, float> Memory()
        {
            var m = new Dictionary<string, float>(Plays.Count);
            foreach (var (sp, n) in Plays)
                if (n >= 4)   // 4戦未満は雑音なので効かせない
                    m[sp] = (Won.GetValueOrDefault(sp) / (float)n - 0.5f) * 2f;
            return m;
        }

        public void Record(bool won)
        {
            foreach (var e in Build.Entries)
            {
                Plays[e.SpeciesId] = Plays.GetValueOrDefault(e.SpeciesId) + 1;
                if (won) Won[e.SpeciesId] = Won.GetValueOrDefault(e.SpeciesId) + 1;
            }
        }
    }

    // 5戦術から2つ選ぶ全10通り。35チームへ順に配るので、各組に3〜4チーム。
    private static readonly (Tactic A, Tactic B)[] TacticPairs =
    {
        (Tactic.Guardian, Tactic.Burst),   (Tactic.Guardian, Tactic.Control),
        (Tactic.Guardian, Tactic.HitAway), (Tactic.Guardian, Tactic.Weather),
        (Tactic.Burst,    Tactic.Control), (Tactic.Burst,    Tactic.HitAway),
        (Tactic.Burst,    Tactic.Weather), (Tactic.Control,  Tactic.HitAway),
        (Tactic.Control,  Tactic.Weather), (Tactic.HitAway,  Tactic.Weather),
    };

    private static string TacticJa(Tactic t) => t switch
    {
        Tactic.Guardian => "仁王立ち",
        Tactic.Burst => "ワンサイクル",
        Tactic.Control => "コントロール",
        Tactic.HitAway => "ヒットアンドアウェイ",
        _ => "天候",
    };

    private void RunTactics()
    {
        var rng = new RandomNumberGenerator();
        rng.Seed = MetaScenario.Seed + 7;

        var teams = new List<TacticTeam>();
        for (int i = 0; i < _teamCount; i++)
        {
            var (a, b) = TacticPairs[i % TacticPairs.Length];
            var t = new TacticTeam { Index = i, A = a, B = b };
            // 初手はまだ相手を知らないので、戦術の適性だけで組む。
            t.Build = TacticalBuilder.Build(a, b, null, null, rng);
            teams.Add(t);
        }

        var pairs = new List<(int I, int J)>();
        for (int i = 0; i < teams.Count; i++)
            for (int j = i + 1; j < teams.Count; j++)
                pairs.Add((i, j));

        GD.Print($"[戦術] {teams.Count}チーム / {pairs.Count}ペア × {_repeat}戦 × {_loops}ループ "
                 + $"= {pairs.Count * _repeat * _loops:N0}試合");
        foreach (var g in teams.GroupBy(t => (t.A, t.B)))
            GD.Print($"  {TacticJa(g.Key.A)}＋{TacticJa(g.Key.B)}: {g.Count()}チーム "
                     + $"({string.Join(",", g.Select(t => "T" + t.Index))})");
        GD.Print("");

        var csv = new List<string>();
        var builds = new List<string>();
        var allCycles = new List<int>();

        for (int loop = 1; loop <= _loops; loop++)
        {
            int done = 0;
            foreach (var (i, j) in pairs)
            {
                var ta = teams[i];
                var tb = teams[j];
                int winA = 0, winB = 0, draw = 0, undecided = 0;

                for (int k = 0; k < _repeat; k++)
                {
                    // 互いに相手の「直前の構築」を見て同時に組み直す。
                    var seenA = ta.Build.Entries.Select(e => e.SpeciesId).ToList();
                    var seenB = tb.Build.Entries.Select(e => e.SpeciesId).ToList();
                    ta.Build = TacticalBuilder.Build(ta.A, ta.B, seenB, ta.Memory(), rng);
                    tb.Build = TacticalBuilder.Build(tb.A, tb.B, seenA, tb.Memory(), rng);

                    bool aHome = k % 2 == 0;   // 先手/後手を戦ごとに入れ替える
                    var home = aHome ? ta.Build : tb.Build;
                    var away = aHome ? tb.Build : ta.Build;
                    var awayProfile = new NpcTeam
                    {
                        Id = "tactic", Name = "戦術構築の相手", MainType = "Neutral", Team = away,
                    };

                    var r = RunMatch(home, awayProfile, verbose: false);
                    allCycles.Add(r.Cycles);

                    bool aWon = (aHome && r.Outcome == BattleOutcome.PlayerWin)
                             || (!aHome && r.Outcome == BattleOutcome.EnemyWin);
                    bool bWon = (aHome && r.Outcome == BattleOutcome.EnemyWin)
                             || (!aHome && r.Outcome == BattleOutcome.PlayerWin);

                    if (aWon) { winA++; ta.Wins++; tb.Losses++; }
                    else if (bWon) { winB++; tb.Wins++; ta.Losses++; }
                    else if (r.Outcome == BattleOutcome.Draw) { draw++; ta.Draws++; tb.Draws++; }
                    else { undecided++; ta.Undecided++; tb.Undecided++; }

                    ta.Record(aWon);
                    tb.Record(bWon);
                }

                csv.Add($"{loop},{i},{j},{winA},{winB},{draw},{undecided}");
                if (++done % 100 == 0)
                    GD.Print($"[戦術] loop{loop}: {done}/{pairs.Count}ペア完了");
            }

            // このループ最後の試合を終えた時点の構築を記録する（5段階）。
            foreach (var t in teams)
                builds.Add($"{loop},{t.Index},{t.A},{t.B}," + string.Join(",",
                    t.Build.Entries.Select(e =>
                        $"{e.SpeciesId}:{e.ItemId}:{string.Join("|", e.MoveIds)}")));

            var top = teams.OrderByDescending(t => t.Wins).First();
            GD.Print($"[戦術] --- loop{loop} 完了 / 首位 T{top.Index}"
                     + $"({TacticJa(top.A)}＋{TacticJa(top.B)}) 通算{top.Wins}勝{top.Losses}敗 ---");
        }

        if (!string.IsNullOrEmpty(_outPath))
        {
            System.IO.File.WriteAllLines(_outPath, csv);
            System.IO.File.WriteAllLines(_outPath + ".builds.txt", builds);
        }

        GD.Print("");
        if (allCycles.Count > 0)
            GD.Print($"[結果] {allCycles.Count:N0}試合 / 決着までのサイクル: "
                     + $"平均{allCycles.Average():F1} / 最短{allCycles.Min()} / 最長{allCycles.Max()}");
        GD.Print("[結果] チーム別の通算成績（勝率順）:");
        foreach (var t in teams.OrderByDescending(t => (double)t.Wins / Mathf.Max(1, t.Wins + t.Losses + t.Draws + t.Undecided)))
        {
            int played = t.Wins + t.Losses + t.Draws + t.Undecided;
            GD.Print($"  T{t.Index,-3} {TacticJa(t.A)}＋{TacticJa(t.B),-12} "
                     + $"{100.0 * t.Wins / played,5:F1}%  {t.Wins}勝{t.Losses}敗{t.Draws}分{t.Undecided}未決着");
        }

        GetTree().Quit();
    }

    private readonly struct MatchResult
    {
        public BattleOutcome Outcome { get; init; }
        public int Cycles { get; init; }
        public int HomeBst { get; init; }   // 選出4匹の合計種族値（自陣）
        public int AwayBst { get; init; }   // 同・敵陣

        // 選出され、実際に盤面に立った個体だけが入る（種族ID→生存かどうか）。
        // §22の適応ロジック（弱点個体の特定）に使う——それ以外の呼び出しは無視してよい。
        public IReadOnlyDictionary<string, bool> HomeAlive { get; init; }
        public IReadOnlyDictionary<string, bool> AwayAlive { get; init; }
    }

    // 1試合。両陣営とも同じ判断ロジックで動かし、決着まで進める。
    private MatchResult RunMatch(BattleTeam homeTeam, NpcTeam awayProfile, bool verbose)
    {
        var host = new Node { Name = "Match" };
        AddChild(host);

        var arena = new BattleArena(host);
        var sched = new BattleScheduler();
        var clock = new BattleClock();
        var flow = new UI.Battle.BattleFlow();
        host.AddChild(flow);
        // headless: true — この試合場に画面を見る人はいない。1ターン
        // ごとに盤面56マス＋レール＋名簿ぶんのUIを作り直すのは、対戦の
        // 骨格そのものより重い無駄な仕事だった（100試合が完走しないほど）。
        flow.Begin(homeTeam, new List<PublicEntryView>(), clock, sched,
                   new BattleSession(sched, clock), arena, headless: true);

        // 自陣も同じ判断で動かす。**選出と配置まで同じにしないと比較にならない**
        // ——最初は自陣だけ登録順の自動選出＋既定配置で回しており、同じ編成
        // どうしの試合が4勝16敗になった。差は編成ではなく、選出と配置の
        // 決め方だった。
        var homeBrain = new NpcOpponent(
            new NpcTeam { Id = "home", Name = "自陣", MainType = "Neutral", Team = homeTeam },
            Faction.Player, foeView: BattleSession.DiscloseTeam(awayProfile.Team));

        flow.ConfirmBuild();
        flow.ChooseOpponent(awayProfile);
        flow.ConfirmSelection(homeBrain.Selection);

        // 配置フェーズで作られた既定配置を、自陣の判断で置き直す。
        foreach (var (entry, tile) in homeBrain.Deployment.Placements)
            flow.Deployment.Place(entry, tile);

        flow.Show(UI.Battle.BattleFlow.Phase.Battle);

        var me = homeBrain;

        if (verbose)
        {
            GD.Print("");
            foreach (var e in sched.Roster)
                GD.Print($"  {Side(e)} {e.ActorName,-12} BST{BattleScheduler.Bst(e),4} "
                         + $"HP{e.Stats.MaxHp,4} 持ち物 {(string.IsNullOrEmpty(e.HeldItemId) ? "なし" : e.HeldItemId)}");
            GD.Print("");
        }

        // 選出4匹ぶんの合計種族値。死亡してもBST自体は変わらないので、
        // このタイミングで確定してよい（決着後の集計に使う）。
        int homeBst = sched.Roster.Where(e => e.Faction == Faction.Player).Sum(Bst);
        int awayBst = sched.Roster.Where(e => e.Faction == Faction.Enemy).Sum(Bst);

        var alive = sched.Roster.Where(e => e.IsAlive).ToHashSet();
        int lastTurn = -1, submissions = 0;

        while (flow.Current != UI.Battle.BattleFlow.Phase.Finished && submissions < TurnCap)
        {
            flow.SubmitPlayerInput(me.Decide(sched, arena.Grid, arena.Floor));
            flow._Process(1.0);
            submissions++;

            // ターンが解決したら盤面の要約を1行出す。
            int turn = sched.CycleNumber * 100 + sched.TurnInCycle;
            if (!verbose || turn == lastTurn) continue;
            lastTurn = turn;

            foreach (var e in alive.Where(e => !e.IsAlive).ToList())
            {
                GD.Print($"  ** {Side(e)} {e.ActorName} 倒れた");
                alive.Remove(e);
            }
            GD.Print($"[C{sched.CycleNumber}T{sched.TurnInCycle}] {HpLine(sched)}");
        }

        var result = new MatchResult
        {
            Outcome = flow.Outcome, Cycles = sched.CycleNumber,
            HomeBst = homeBst, AwayBst = awayBst,
            HomeAlive = sched.Roster.Where(e => e.Faction == Faction.Player)
                .ToDictionary(e => e.SpeciesId, e => e.IsAlive),
            AwayAlive = sched.Roster.Where(e => e.Faction == Faction.Enemy)
                .ToDictionary(e => e.SpeciesId, e => e.IsAlive),
        };
        if (System.Environment.GetEnvironmentVariable("BM_DEBUG") == "1")
            GD.Print($"[Match] cycles={sched.CycleNumber} submissions={submissions} outcome={flow.Outcome}");

        if (verbose)
        {
            GD.Print("");
            GD.Print($"[決着] {Outcome(flow.Outcome)} "
                     + $"（{sched.CycleNumber}サイクル / 提出{submissions}回）");
            foreach (var e in sched.Roster)
                GD.Print($"  {Side(e)} {e.ActorName,-12} "
                         + (e.IsAlive ? $"生存 HP{e.Stats.CurrentHp}/{e.Stats.MaxHp}" : "倒れた"));
        }

        // QueueFree は解放をフレーム末まで遅らせる。ここは _Ready() の中で
        // 複数試合を同期的に回しているだけでフレームが一度も挟まらないため、
        // QueueFree のままだと試合を重ねるほど未解放のノード（StyleBoxFlat
        // だらけの BattleHud 一式）が積み上がり、終了時に大量リークで
        // 落ちる（結果の出力自体は先に済んでいるので実害はないが、行儀が悪い）。
        // Free() で即座に解放する。
        host.Free();
        return result;
    }

    private static int Bst(Entity e) => BattleScheduler.Bst(e);
    private static int Bst(BattleEntry e) =>
        e.Species is { } sp ? sp.BaseHP + sp.BaseAtk + sp.BaseDef : 0;

    private static string HpLine(BattleScheduler sched) => string.Join("  ",
        sched.Roster.Select(e => $"{Side(e)}{e.ActorName}"
                                 + $" {(e.IsAlive ? e.Stats.CurrentHp.ToString() : "×")}"));

    private static string Side(Entity e) => e.Faction == Faction.Player ? "自" : "敵";

    private static string Outcome(BattleOutcome o) => o switch
    {
        BattleOutcome.PlayerWin => "自陣の勝ち（敵陣が全滅）",
        BattleOutcome.EnemyWin => "敵陣の勝ち（自陣が全滅）",
        BattleOutcome.Draw => "引き分け",
        _ => "未決着",
    };

    // ---- 編成の組み立て ----

    // 完全ランダム構築。6匹の種族・各自の技4つ・全員ぶんの持ち物を、
    // 威力や種族値による選り好みを一切せず無作為に決める。
    //
    // 持ち物は「必ず全員に付与」（構築内で重複しない16種から6匹ぶんを
    // 重複なく引く——16種 > 6匹なので必ず足りる）。技は威力順ではなく
    // learnset からの無作為抽出（DefaultLoadout / NPC生成器の選び方とは
    // 別系統。あちらは「強い技を優先」、こちらは「選ばない」）。
    private static BattleTeam RandomTeam(RandomNumberGenerator rng)
    {
        var species = SpeciesDatabase.Instance?.All.Keys.ToList() ?? new List<string>();
        Shuffle(species, rng);

        // タグ:伝説は1構築に1体まで（BattleTeam.Validate と同じ規則）。
        // シャッフル済みの列を先頭から拾い、2体目以降の伝説だけ読み飛ばす。
        var speciesIds = new List<string>();
        bool hasLegendary = false;
        foreach (var id in species)
        {
            if (speciesIds.Count >= BattleTeam.RosterSize) break;
            bool legendary = SpeciesDatabase.Instance?.Get(id)?.IsLegendary ?? false;
            if (legendary && hasLegendary) continue;
            speciesIds.Add(id);
            if (legendary) hasLegendary = true;
        }

        var heldItems = ItemDatabase.AllIds()
            .Where(id => ItemDatabase.Get(id)?.Type == ItemType.BattleHeld).ToList();
        Shuffle(heldItems, rng);
        var itemIds = heldItems.Take(BattleTeam.RosterSize).ToList();

        var entries = new List<BattleEntry>();
        for (int i = 0; i < speciesIds.Count; i++)
        {
            var entry = new BattleEntry { SpeciesId = speciesIds[i] };
            var learnable = entry.Learnable();
            Shuffle(learnable, rng);
            entry.MoveIds = learnable.Take(MoveManager.MaxMoves).Select(m => m.Id).ToList();
            entry.ItemId = itemIds.ElementAtOrDefault(i);
            entries.Add(entry);
        }
        return new BattleTeam(entries);
    }

    // Fisher-Yates。RandomNumberGenerator は System.Random と違って
    // シャッフルの口を持たないので、ここで1つだけ用意する。
    private static void Shuffle<T>(IList<T> list, RandomNumberGenerator rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = (int)(rng.Randf() * (i + 1));
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private string Label(string id) => id == "player" ? "手持ち6匹" : ProfileOf(id)?.Name ?? id;

    private static NpcTeam ProfileOf(string id) =>
        string.IsNullOrEmpty(id) ? null : NpcTeamDatabase.Get(id);

    private static BattleTeam TeamOf(string id)
    {
        var profile = ProfileOf(id);
        if (profile != null) return profile.Team;

        // 手持ちの既定編成。技はNPCと同じ選び方（構築画面が編集に
        // 対応するまでの仮置き）。
        return new BattleTeam(PlayerRoster.Select(sid =>
        {
            var sp = SpeciesDatabase.Instance?.Get(sid);
            return new BattleEntry
            {
                SpeciesId = sid,
                MoveIds = DefaultLoadout.PickMoves(sp, MoveManager.MaxMoves),
            };
        }));
    }

    // 持ち物を全部外した写しを作る。BattleEntry は init なので詰め直す。
    private static BattleTeam StripItems(BattleTeam team) =>
        new(team.Entries.Select(e => new BattleEntry
        {
            SpeciesId = e.SpeciesId,
            MoveIds = e.MoveIds,
            ItemId = null,
        }));

    private static NpcTeam StripItems(NpcTeam profile) => new()
    {
        Id = profile.Id, Name = profile.Name, MainType = profile.MainType,
        TotalBst = profile.TotalBst, Team = StripItems(profile.Team),
    };

    private static int CountItems(BattleTeam team) =>
        team.Entries.Count(e => !string.IsNullOrEmpty(e.ItemId));

    private static string Arg(string[] args, string name)
    {
        int i = System.Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
