using System.Linq;
using Godot;
using MysteryDungeon.Entities;
using MysteryDungeon.Species;

namespace MysteryDungeon.Battle;

// 通信対戦の実地検証。2プロセスを localhost で繋いで、編成の開示範囲と
// ターン入力の往復が実際に通ることを確かめる。
//   godot --headless Scenes/NetTestScene.tscn -- --host
//   godot --headless Scenes/NetTestScene.tscn -- --join
public partial class NetTestScene : Node2D
{
    private static readonly string[] HostRoster = { "001", "004", "006", "009", "002", "010" };
    private static readonly string[] GuestRoster = { "005", "008", "007", "003", "011", "012" };

    private BattleNetwork _net;
    private BattleTeam _team;
    private bool _isHost;
    private double _elapsed;
    private bool _sentTeam;
    private bool _sentInput;
    private bool _done;
    private bool _checkedInput;

    // ホストを先に立ち上げてゲストを後から繋ぐので、ホストのほうを長く
    // 生かしておく。先に落ちるとゲストの送信が切断と競合する。
    private double LifetimeSeconds => _isHost ? 12.0 : 7.0;

    public override void _Ready()
    {
        // "--" より後ろの引数は GetCmdlineUserArgs() 側に入る。
        // GetCmdlineArgs() ではエンジン自身の引数しか取れず、両方が
        // GUEST になってしまう。
        var args = OS.GetCmdlineUserArgs();
        _isHost = args.Contains("--host");

        _team = BuildTeam(_isHost ? HostRoster : GuestRoster);
        _net = new BattleNetwork { Name = "BattleNetwork" };
        AddChild(_net);

        var err = _isHost ? _net.Host() : _net.Join("127.0.0.1");
        if (err != Error.Ok)
        {
            GD.Print($"[NetTest] {(_isHost ? "HOST" : "GUEST")} 接続失敗: {err}");
            GetTree().Quit(1);
            return;
        }

        _net.OpponentTeamReceived += OnTeamReceived;
        GD.Print($"[NetTest] {(_isHost ? "HOST" : "GUEST")} 起動");
    }

    private static BattleTeam BuildTeam(string[] ids)
    {
        var entries = ids.Select(id =>
        {
            var sp = SpeciesDatabase.Instance?.Get(id);
            return new BattleEntry
            {
                SpeciesId = id,
                MoveIds = sp.Learnset.Select(l => l.MoveId).Distinct()
                            .Take(Entities.MoveManager.MaxMoves).ToList(),
            };
        }).ToList();
        return new BattleTeam(entries);
    }

    private void OnTeamReceived()
    {
        string who = _isHost ? "HOST" : "GUEST";
        var pub = _net.OpponentPublicTeam;

        GD.Print($"[検証:{who}] 相手の編成を受信: "
                 + $"{(pub.Count == 6 ? "OK" : "NG")} ({pub.Count}匹 "
                 + $"{string.Join(",", pub.Select(p => SpeciesDatabase.Instance?.Get(p.SpeciesId)?.DisplayName))})");

        if (_isHost)
        {
            // ホストは計算のために完全な編成を持つ。
            GD.Print($"[検証:HOST] 相手の完全な編成を保持: "
                     + $"{(_net.OpponentFullTeam != null && _net.OpponentFullTeam.Entries.All(e => e.MoveIds.Count == 4) ? "OK" : "NG")}");
            // 受け取ったので種族だけ返す。
            _net.SendTeam(_team);
        }
        else
        {
            // クライアントには種族しか届かない。技・持ち物を持つ経路が無い。
            GD.Print($"[検証:GUEST] 相手の技・持ち物は届かない: "
                     + $"{(_net.OpponentFullTeam == null ? "OK" : "NG")}");
        }
    }

    public override void _Process(double delta)
    {
        if (_done) return;
        _elapsed += delta;

        // 接続が立ってからゲストが編成を送る。
        if (!_sentTeam && _net.Connected && !_isHost && _elapsed > 0.5)
        {
            _net.SendTeam(_team);
            _sentTeam = true;
        }

        // 編成の往復が済んだらターン入力を1回だけ送ってみる。
        if (!_sentInput && _elapsed > 2.0)
        {
            if (_isHost)
            {
                _net.Session = new BattleSession(new BattleScheduler(), new BattleClock());
                _sentInput = true;
            }
            else if (_net.Connected)
            {
                _net.SendInput(new TurnInput(2, 1, new Vector2I(3, 4)));
                GD.Print("[NetTest:GUEST] ターン入力を送信");
                _sentInput = true;
            }
        }

        // ホストは入力が届いたかを見る。
        if (_isHost && _sentInput && !_checkedInput && _elapsed > 6.0)
        {
            _checkedInput = true;
            bool got = _net.Session != null && _net.Session.HasSubmitted(Faction.Enemy);
            GD.Print($"[検証:HOST] 相手のターン入力が届く: {(got ? "OK" : "NG")}");
            GD.Print($"[検証:HOST] 相手だけの提出では伏せたまま: "
                     + $"{(_net.Session != null && !_net.Session.BothSubmitted ? "OK" : "NG")}");
        }

        if (_elapsed > LifetimeSeconds)
        {
            _done = true;
            GD.Print($"[NetTest] {(_isHost ? "HOST" : "GUEST")} 終了");
            GetTree().Quit();
        }
    }
}
