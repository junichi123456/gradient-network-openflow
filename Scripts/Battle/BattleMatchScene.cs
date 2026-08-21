using System.Collections.Generic;
using System.Linq;
using Godot;
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

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _home = Arg(args, "--home") ?? _home;
        _away = Arg(args, "--away");
        _matches = int.TryParse(Arg(args, "--matches"), out var n) ? n : 1;
        // --items だけなら両陣営に持たせる。片側だけに持たせて、持ち物が
        // どちらの側を利しているかを切り分けられるようにしてある。
        int ai = System.Array.IndexOf(args, "--items");
        if (ai >= 0)
            _items = ai + 1 < args.Length && !args[ai + 1].StartsWith("--")
                ? args[ai + 1] : "both";

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

    private readonly struct MatchResult
    {
        public BattleOutcome Outcome { get; init; }
        public int Cycles { get; init; }
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

        var result = new MatchResult { Outcome = flow.Outcome, Cycles = sched.CycleNumber };

        if (verbose)
        {
            GD.Print("");
            GD.Print($"[決着] {Outcome(flow.Outcome)} "
                     + $"（{sched.CycleNumber}サイクル / 提出{submissions}回）");
            foreach (var e in sched.Roster)
                GD.Print($"  {Side(e)} {e.ActorName,-12} "
                         + (e.IsAlive ? $"生存 HP{e.Stats.CurrentHp}/{e.Stats.MaxHp}" : "倒れた"));
        }

        host.QueueFree();
        return result;
    }

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
