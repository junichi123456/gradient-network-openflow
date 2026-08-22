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
public partial class BattleMatchScene : Node2D
{
    // 手持ちの既定編成。構築画面がまだ編集に対応していないので、
    // BattleFlowScene と同じ6匹を使う。
    private static readonly string[] PlayerRoster = { "001", "004", "006", "009", "002", "010" };

    private const int TurnCap = 600;     // 1試合あたりの提出回数の上限

    private string _home = "player";
    private string _away = null;         // 既定は先頭のNPC
    private int _matches = 1;
    private string _items = "none";      // none / both / home / away
    private bool _random;

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

    private readonly struct MatchResult
    {
        public BattleOutcome Outcome { get; init; }
        public int Cycles { get; init; }
        public int HomeBst { get; init; }   // 選出4匹の合計種族値（自陣）
        public int AwayBst { get; init; }   // 同・敵陣
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
        flow.Begin(homeTeam, new List<PublicEntryView>(), clock, sched,
                   new BattleSession(sched, clock), arena);

        // 自陣も同じ判断で動かす。**選出と配置まで同じにしないと比較にならない**
        // ——最初は自陣だけ登録順の自動選出＋既定配置で回しており、同じ編成
        // どうしの試合が4勝16敗になった。差は編成ではなく、選出と配置の
        // 決め方だった。
        var homeBrain = new NpcOpponent(
            new NpcTeam { Id = "home", Name = "自陣", MainType = "Neutral", Team = homeTeam },
            Faction.Player);

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
        };

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
        var speciesIds = species.Take(BattleTeam.RosterSize).ToList();

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
