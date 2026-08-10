using System.Linq;
using Godot;
using MysteryDungeon.Dungeon;
using MysteryDungeon.Entities;
using MysteryDungeon.Grid;
using MysteryDungeon.Species;
using MysteryDungeon.Turn;

namespace MysteryDungeon.Battle;

// 対戦を実機で触るための入口。構築 → 選出 → 配置 → 対戦 を通しで動かす。
//
// 通信対戦の相手はまだ繋がないので、相手側は固定の編成を置いて成立させる。
// 目的は「画面を見て操作すること」なので、そこは割り切っている。
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
    private static readonly string[] FoeRoster = { "005", "008", "007", "003", "011", "012" };

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

        var grid = new GridManager { Name = "GridManager" };
        AddChild(grid);
        var turns = new TurnManager { Name = "TurnManager" };
        AddChild(turns);
        var floor = new FloorController { Name = "FloorController" };
        AddChild(floor);
        floor.InitializeArena(grid, turns);

        var mine = BuildTeam(MyRoster);
        var foe = BuildTeam(FoeRoster);

        // 盤面へ実際に並べる。対戦画面が中身のある状態で見えるように。
        var sched = new BattleScheduler();
        _sched = sched;
        Deploy(Faction.Player, mine, grid, floor, sched);
        Deploy(Faction.Enemy, foe, grid, floor, sched);

        var clock = new BattleClock();
        _flow = new UI.Battle.BattleFlow { Name = "BattleFlow" };
        AddChild(_flow);
        _flow.Begin(mine, PublicEntryView.Of(foe.Entries), clock, sched,
                    new BattleSession(sched, clock));

        if (_shotPath != null) JumpTo(_shotPhase, mine);
    }

    // 撮影用にフェーズを飛ばす。手で触るときは使わない。
    private void JumpTo(string phase, BattleTeam mine)
    {
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
                MoveIds = sp.Learnset.Select(l => l.MoveId).Distinct()
                            .Take(MoveManager.MaxMoves).ToList(),
            };
        }).ToList();
        return new BattleTeam(entries);
    }

    private void Deploy(Faction f, BattleTeam team, GridManager grid,
                        FloorController floor, BattleScheduler sched)
    {
        var dep = BattleDeployment.Default(f, team.AutoSelect());
        foreach (var (entry, tile) in dep.Placements)
        {
            var pal = new BattlePal { SpeciesId = entry.SpeciesId, Faction = f, Entry = entry };
            AddChild(pal);
            pal.Grid = grid;
            pal.FloorController = floor;
            pal.PlaceAt(tile);
            pal.FaceDirection(BattleBoard.Facing(f));
            pal.Visible = false;          // 盤面はHUD側で描くので実体は隠す
            floor.AddArenaActor(pal);
            sched.Register(pal);
        }
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
