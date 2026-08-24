using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Entities;

namespace MysteryDungeon.Battle;

// 通信対戦のトランスポート層。Godot の ENetMultiplayerPeer を使う。
//
// ■ 構成の判断（すべてこちらで決めた）
//
// 1. ホスト権威型。乱数にシードを持たせない方針なので両者が同じ結果を
//    再現できず、ロックステップは成立しない。片方が計算して結果を配る。
//
// 2. マッチングサーバは置かない。片方がホストになり、もう片方が
//    アドレスを指定して繋ぐ直接接続。マッチングを挟むには常時動く
//    サーバが要り、運用費と実装が別物になるため。§1-2 の「対戦受付」は
//    この直接接続で代替する。
//
// 3. 切断は投了扱い。20分の試合中に相手が落ちたまま止まると
//    決着がつかないので、残った側の勝ちにする。
//
// ■ 正直に書いておく限界
//
// ホストは相手の技と持ち物を「計算のために」メモリ上に持つ。人間に表示は
// しないが、ホスト側を改造すれば覗ける。これはホスト権威型に本質的な穴で、
// 塞ぐには中立なサーバが要る。友人同士の対戦を想定した割り切り。
public partial class BattleNetwork : Node
{
    public const int DefaultPort = 34567;

    [Signal] public delegate void OpponentJoinedEventHandler();
    [Signal] public delegate void OpponentLeftEventHandler();
    [Signal] public delegate void OpponentTeamReceivedEventHandler();
    [Signal] public delegate void TurnResolvedEventHandler();

    public bool IsHost { get; private set; }
    public bool Connected { get; private set; }

    // ホストのみが持つ権威。クライアント側は null のまま。
    public BattleSession Session { get; set; }

    // 相手から届いた編成。ホストは完全な内容（計算に要る）、クライアントは
    // 種族だけ（開示範囲どおり）。
    public BattleTeam OpponentFullTeam { get; private set; }
    public List<PublicEntryView> OpponentPublicTeam { get; private set; } = new();

    // 直近に受け取った解決結果（クライアント側の描画用）。
    public TurnResult? LastResult { get; private set; }

    private int _opponentPeerId = -1;

    // ---- 接続 ----

    public Error Host(int port = DefaultPort)
    {
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(port, maxClients: 1);   // 1対1なので1人だけ
        if (err != Error.Ok) return err;

        Multiplayer.MultiplayerPeer = peer;
        IsHost = true;
        WireSignals();
        GD.Print($"[Net] ホストとして待機中 port={port}");
        return Error.Ok;
    }

    public Error Join(string address, int port = DefaultPort)
    {
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateClient(address, port);
        if (err != Error.Ok) return err;

        Multiplayer.MultiplayerPeer = peer;
        IsHost = false;
        WireSignals();
        GD.Print($"[Net] {address}:{port} へ接続中");
        return Error.Ok;
    }

    private void WireSignals()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
    }

    private void OnPeerConnected(long id)
    {
        _opponentPeerId = (int)id;
        Connected = true;
        GD.Print($"[Net] 相手が接続した peer={id}");
        EmitSignal(SignalName.OpponentJoined);
    }

    // 切断は投了扱い。相手が落ちたまま試合が止まらないようにする。
    private void OnPeerDisconnected(long id)
    {
        Connected = false;
        GD.Print($"[Net] 相手が切断した peer={id} — 投了扱い");
        EmitSignal(SignalName.OpponentLeft);
    }

    public BattleOutcome OutcomeOnDisconnect(Faction survivor) =>
        survivor == Faction.Player ? BattleOutcome.PlayerWin : BattleOutcome.EnemyWin;

    // ---- 編成の送受信 ----

    // 自分の編成を相手へ送る。ホストへは完全な内容（相手のぶんも計算する
    // ため）、クライアントへは種族だけ（開示範囲）。
    public void SendTeam(BattleTeam team)
    {
        if (IsHost)
            RpcId(_opponentPeerId, MethodName.ReceivePublicTeam, EncodePublic(team));
        else
            RpcId(1, MethodName.ReceiveFullTeam, EncodeFull(team));
    }

    // クライアント → ホスト。計算に必要なので技と持ち物まで含む。
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveFullTeam(string json)
    {
        OpponentFullTeam = DecodeFull(json);
        OpponentPublicTeam = PublicEntryView.Of(OpponentFullTeam.Entries);
        GD.Print($"[Net] 相手の編成を受信（完全） {OpponentFullTeam.Entries.Count}匹");
        EmitSignal(SignalName.OpponentTeamReceived);

        // 受け取ったら、こちらの編成は種族だけ返す。
        EmitSignal(SignalName.OpponentJoined);
    }

    // ホスト → クライアント。開示範囲どおり種族しか送らない。
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceivePublicTeam(string json)
    {
        OpponentPublicTeam = DecodePublic(json);
        GD.Print($"[Net] 相手の編成を受信（種族のみ） {OpponentPublicTeam.Count}匹");
        EmitSignal(SignalName.OpponentTeamReceived);
    }

    // ---- 送ってはいけないもの ----
    //
    // 選出（6匹のどれを4匹に絞ったか）と配置（どのマスに置いたか）は
    // **相手へ一切送らない**。相手について分かってよいのは最初に開示した
    // 6匹だけで、それ以外は対戦開始まで伏せる。
    //
    // ホストは自分が計算するために両者ぶんを保持するが、クライアントへは
    // 流さない。対戦が始まったあとは、盤上の見えている情報として
    // TurnResult 経由で自然に開示される。
    //
    // ここに SendSelection / SendDeployment のような口を足すと、その時点で
    // 規則が壊れる。足すなら「対戦開始後にまとめて配る」形にすること。

    // ---- ターン入力と結果 ----

    // クライアント → ホスト。1ターンの入力を送る。
    public void SendInput(TurnInput input)
    {
        if (IsHost) return;   // ホストは自分の Session に直接入れる
        RpcId(1, MethodName.ReceiveInput, input.ActorIndex, input.MoveSlot, input.Target);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveInput(int actorIndex, int moveSlot, Vector2I target)
    {
        // 権威側だけが受け取る。伏せる判断は BattleSession が持つので、
        // ここは素通しでよい。
        Session?.SubmitInput(Faction.Enemy, new TurnInput(actorIndex, moveSlot, target));
    }

    // ホスト → クライアント。解決結果を配る。クライアントは自分で
    // 乱数を引かないので、行動順もHPもここから受け取る。
    public void BroadcastResult(TurnResult r)
    {
        if (!IsHost) return;
        RpcId(_opponentPeerId, MethodName.ReceiveResult,
              r.CycleNumber, r.TurnInCycle,
              r.ActingOrder.Select(f => (int)f).ToArray(),
              r.HpAfter.ToArray(), (int)r.Outcome);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveResult(int cycle, int turn, int[] order, int[] hp, int outcome)
    {
        LastResult = new TurnResult(cycle, turn,
                                    order.Select(i => (Faction)i).ToList(),
                                    hp.ToList(), (BattleOutcome)outcome);
        EmitSignal(SignalName.TurnResolved);
    }

    // ---- 直列化 ----
    // RPC の引数は Variant 互換に限られるので、編成は JSON 文字列で運ぶ。

    public static string EncodeFull(BattleTeam team)
    {
        var arr = new Godot.Collections.Array();
        foreach (var e in team.Entries)
        {
            var moves = new Godot.Collections.Array();
            foreach (var m in e.MoveIds) moves.Add(m);
            arr.Add(new Godot.Collections.Dictionary
            {
                ["s"] = e.SpeciesId,
                ["m"] = moves,
                ["i"] = e.ItemId ?? "",
            });
        }
        return Json.Stringify(arr);
    }

    public static BattleTeam DecodeFull(string json)
    {
        var parsed = Json.ParseString(json);
        var entries = new List<BattleEntry>();
        foreach (var v in parsed.AsGodotArray())
        {
            var d = v.AsGodotDictionary();
            var moves = d["m"].AsGodotArray().Select(x => x.AsString()).ToList();
            string item = d["i"].AsString();
            entries.Add(new BattleEntry
            {
                SpeciesId = d["s"].AsString(),
                MoveIds = moves,
                ItemId = string.IsNullOrEmpty(item) ? null : item,
            });
        }
        return new BattleTeam(entries);
    }

    // 開示用。種族しか詰めないので、技や持ち物が漏れる余地がない。
    public static string EncodePublic(BattleTeam team)
    {
        var arr = new Godot.Collections.Array();
        foreach (var e in team.Entries) arr.Add(e.SpeciesId);
        return Json.Stringify(arr);
    }

    public static List<PublicEntryView> DecodePublic(string json) =>
        Json.ParseString(json).AsGodotArray()
            .Select(v => new PublicEntryView(v.AsString())).ToList();
}
