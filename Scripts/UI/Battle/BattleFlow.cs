using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Battle;
using MysteryDungeon.Entities;

namespace MysteryDungeon.UI.Battle;

// 4画面の進行役。どの画面を出すか、時計を進めるか、次へ移るかを決める。
// 画面そのものは自分の描画にしか責任を持たず、フェーズの遷移はここだけが握る。
//
//   構築 → 選出(50秒) → 配置 → 対戦(20分)
//
// 選出と対戦には制限時間がある。時間切れの既定動作（登録順の自動選出、
// 引き分け）は BattleClock 側に実装済みなので、ここはそれを呼ぶだけ。
public partial class BattleFlow : Control
{
    public enum Phase { Build, Selection, Deploy, Battle, Finished }

    [Signal] public delegate void PhaseChangedEventHandler(int phase);

    public Phase Current { get; private set; } = Phase.Build;
    public BattleOutcome Outcome { get; private set; } = BattleOutcome.Undecided;

    private BattleTeam _team;
    private IReadOnlyList<PublicEntryView> _foeTeam;
    private BattleClock _clock;
    private BattleScheduler _sched;
    private BattleSession _session;

    private IReadOnlyList<BattleEntry> _selection;
    private BattleDeployment _deployment;

    private Control _screen;

    public void Begin(BattleTeam team, IReadOnlyList<PublicEntryView> foeTeam,
                      BattleClock clock, BattleScheduler sched, BattleSession session)
    {
        _team = team;
        _foeTeam = foeTeam;
        _clock = clock;
        _sched = sched;
        _session = session;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Show(Phase.Build);
    }

    // 画面の入れ替え。前の画面は捨てる（状態はこちらが持っているので、
    // 画面側に持たせない）。
    public void Show(Phase phase)
    {
        _screen?.QueueFree();
        _screen = null;
        Current = phase;

        switch (phase)
        {
            case Phase.Build:
                var build = new TeamBuildScreen();
                AddChild(build);
                build.Initialize(_team);
                build.BuildConfirmed += () => ConfirmBuild();
                _screen = build;
                break;

            case Phase.Selection:
                var sel = new SelectionScreen();
                AddChild(sel);
                sel.Initialize(_team, _foeTeam, _clock);
                sel.SelectionConfirmed += () => ConfirmSelection(sel.Picked);
                _screen = sel;
                break;

            case Phase.Deploy:
                _deployment ??= BattleDeployment.Default(Faction.Player, _selection);
                var dep = new DeployScreen();
                AddChild(dep);
                dep.Initialize(_deployment);       // 相手の配置は渡さない
                dep.DeployConfirmed += () => Show(Phase.Battle);
                _screen = dep;
                break;

            case Phase.Battle:
                var hud = new BattleHud();
                AddChild(hud);
                hud.Initialize(_sched, _clock, _session);

                // 行動パネルは「いま動く番のパル」の技を出す。出さないと
                // 何も選べない画面になる。
                var actor = _sched.AvailableFor(Faction.Player)
                                  .OrderBy(BattleScheduler.Bst).FirstOrDefault();
                if (actor != null) hud.ShowCommands(actor);
                _screen = hud;
                break;
        }

        EmitSignal(SignalName.PhaseChanged, (int)phase);
    }

    // 構築が規則を満たしていれば選出へ。満たしていなければ進ませない。
    public bool ConfirmBuild()
    {
        if (_team.Validate().Count > 0) return false;
        Show(Phase.Selection);
        return true;
    }

    public void ConfirmSelection(IReadOnlyList<BattleEntry> picked)
    {
        if (_team.ValidateSelection(picked).Count > 0) return;
        _clock.SubmitSelection(Faction.Player, _team, picked);
        _selection = picked;
        Show(Phase.Deploy);
    }

    public override void _Process(double delta)
    {
        switch (Current)
        {
            case Phase.Selection:
                _clock.Selection.Advance(delta);
                if (_screen is SelectionScreen s) s.RefreshTimer();

                // 50秒で締め切り。未提出なら登録順の昇順で自動選出される。
                if (_clock.SelectionClosed && _selection == null)
                {
                    _selection = _clock.ResolveSelection(Faction.Player, _team);
                    Show(Phase.Deploy);
                }
                break;

            case Phase.Battle:
                _clock.Match.Advance(delta);

                // 毎フレームは時計だけ。盤面はターンが解決したときに
                // 組み直す（Refresh はノードを全部作り直すので重い）。
                if (_screen is BattleHud hud) hud.RefreshClock();

                Outcome = _clock.Resolve(_sched);
                if (Outcome != BattleOutcome.Undecided)
                {
                    Current = Phase.Finished;
                    EmitSignal(SignalName.PhaseChanged, (int)Phase.Finished);
                }
                break;
        }
    }

    // 相手が落ちた場合は投了扱い。試合が止まったままにならないようにする。
    public void OnOpponentLeft()
    {
        Outcome = BattleOutcome.PlayerWin;
        Current = Phase.Finished;
        EmitSignal(SignalName.PhaseChanged, (int)Phase.Finished);
    }
}
