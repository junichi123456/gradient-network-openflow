using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Entities;
using MysteryDungeon.Species;
using MysteryDungeon.Turn;

namespace MysteryDungeon.Battle;

// 対戦を実機で触るための入口。
// 相手選択 → 構築 → 選出 → 配置 → 対戦 を通しで動かす。
//
// マッチングはまだ実装できる段階にないので、相手はNPC
// （Data/npc_teams.json の8人）から選ぶ。
//
//   godot --path . Scenes/BattleFlowScene.tscn
//
// 検証用に、起動直後の画面を撮って終了する経路も用意している。
//   godot --path . Scenes/BattleFlowScene.tscn -- --shot 出力先.png [--phase deploy]
// ルートは Control。Node2D の下に Control を置くと親に矩形が無いため
// サイズが0のままアンカーが解決されず、UIが左上へ潰れる。
public partial class BattleFlowScene : Control
{
    private static readonly string[] MyRoster = { "001", "004", "006", "009", "002", "010" };

    private UI.Battle.BattleFlow _flow;
    private BattleScheduler _sched;
    private string _shotPath;
    private string _shotPhase = "build";
    private int _frames;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var args = OS.GetCmdlineUserArgs();
        int i = System.Array.IndexOf(args, "--shot");
        if (i >= 0 && i + 1 < args.Length) _shotPath = args[i + 1];
        int p = System.Array.IndexOf(args, "--phase");
        if (p >= 0 && p + 1 < args.Length) _shotPhase = args[p + 1];

        var arena = new BattleArena(this);
        var mine = BuildTeam(MyRoster);

        // パルを立てるのは選出と配置が決まってから（BattleArena 参照）。
        // ここでは盤面と空の進行役だけを用意する。
        var sched = new BattleScheduler();
        _sched = sched;

        var clock = new BattleClock();
        _flow = new UI.Battle.BattleFlow { Name = "BattleFlow" };
        AddChild(_flow);
        _flow.Begin(mine, new List<PublicEntryView>(), clock, sched,
                    new BattleSession(sched, clock), arena);

        if (_shotPath != null) JumpTo(_shotPhase, mine);
    }

    // 撮影用にフェーズを飛ばす。手で触るときは使わない。
    private void JumpTo(string phase, BattleTeam mine)
    {
        if (phase == "opponent") return;

        // 構築と種族選択は相手を選んだ直後の画面。撮影用に直接出す。
        if (phase == "species")
        {
            _flow.ChooseOpponent(NpcTeamDatabase.First());
            _flow.Show(UI.Battle.BattleFlow.Phase.SpeciesPick);
            return;
        }

        // 相手を選ばないと以降のフェーズが成立しない。撮影では先頭の相手
        // （いちばん弱い相手）を選んだことにする。
        _flow.ChooseOpponent(NpcTeamDatabase.First());

        switch (phase)
        {
            case "selection": _flow.ConfirmBuild(); break;
            case "deploy":
                _flow.ConfirmBuild();
                _flow.ConfirmSelection(mine.AutoSelect());
                break;
            case "battle":
                _flow.ConfirmBuild();
                _flow.ConfirmSelection(mine.AutoSelect());
                _flow.Show(UI.Battle.BattleFlow.Phase.Battle);
                break;

            // 技を選んだ直後（射程が盤面に出ている状態）。射程の見え方は
            // 描いてみないと分からないので、撮れる経路を作っておく。
            case "command":
                _flow.ConfirmBuild();
                _flow.ConfirmSelection(mine.AutoSelect());
                _flow.Show(UI.Battle.BattleFlow.Phase.Battle);
                var hud = _flow.GetChildren().OfType<UI.Battle.BattleHud>().FirstOrDefault();
                var actor = _sched.AvailableFor(Faction.Player).FirstOrDefault();
                if (hud == null || actor == null) break;
                hud.EmitSignal(UI.Battle.BattleHud.SignalName.ActorChosen,
                               _sched.Roster.ToList().IndexOf(actor));
                hud.EmitSignal(UI.Battle.BattleHud.SignalName.CommandChosen, 0);
                break;
        }
    }

    private static BattleTeam BuildTeam(string[] ids)
    {
        var entries = ids.Select(id =>
        {
            var sp = SpeciesDatabase.Instance?.Get(id);
            return new BattleEntry
            {
                SpeciesId = id,
                // learnset の先頭4件はレベル1の技（威力15＋変化技）なので、
                // NPCと同じ選び方で4つ採る（DefaultLoadout）。
                MoveIds = DefaultLoadout.PickMoves(sp, MoveManager.MaxMoves),
            };
        }).ToList();
        return new BattleTeam(entries);
    }

    public override void _Process(double delta)
    {
        if (_shotPath == null) return;

        // レイアウトが確定してから撮る。1フレーム目はまだ寸法が入っていない。
        _frames++;
        if (_frames < 6) return;

        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(_shotPath);
        GD.Print($"[Shot] {_shotPhase} → {_shotPath} ({img.GetWidth()}x{img.GetHeight()})");
        GetTree().Quit();
    }
}
