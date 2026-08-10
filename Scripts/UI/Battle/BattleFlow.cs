using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Battle;
using MysteryDungeon.Combat;
using MysteryDungeon.Dungeon;
using MysteryDungeon.Entities;
using MysteryDungeon.Grid;
using MysteryDungeon.Turn;

namespace MysteryDungeon.UI.Battle;

// 4画面の進行役。どの画面を出すか、時計を進めるか、次へ移るかを決める。
// 画面そのものは自分の描画にしか責任を持たず、フェーズの遷移はここだけが握る。
//
//   構築 → 選出(50秒) → 配置(20秒) → 対戦(20分)
//
// 選出・配置・対戦には制限時間がある。時間切れの既定動作（登録順の自動選出、
// 現在の配置のまま開始、引き分け）は BattleClock 側に実装済みなので、
// ここはそれを呼ぶだけ。
//
// 対戦フェーズの1ターンは3段で進む。
//   ① 操作するパルを選ぶ
//   ② 行動（技4つ＋移動）と狙う先を選ぶ
//   ③ 伏せて提出 → 両者が出すまで待つ → 解決
// ③の「待つ」は飾りではない。BattleSession が両者の入力を伏せたまま抱え、
// 揃うまで解決しないので、ここで待たなければ盤面は1マスも動かない。
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

    // 配置フェーズで組み立て中の並び。画面はこの1つを直接書き換える。
    public BattleDeployment Deployment => _deployment;

    private Control _screen;
    private BattleHud _hud;

    // 組み立て中の1ターンぶんの入力。提出した時点で捨てる。
    private Entity _actor;
    private int _slot = -2;               // -2 = 未選択 / -1 = 移動 / 0.. = 技枠
    private Vector2I? _aim;
    private List<Vector2I> _selectable = new();

    // 相手役が答えるまでの間。通信対戦がつながるまでの代役なので、
    // 即答させず「待っている」という状態が画面に出る長さを持たせる。
    private double _opponentDelay;

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

    // 盤面の実体はチームの個体が持っている。GridManager と FloorController は
    // そこから引く（対戦盤は1部屋しかないので、どの個体から引いても同じ）。
    private GridManager Grid => _sched.Roster.Count > 0 ? _sched.Roster[0].Grid : null;
    private FloorController Floor => _sched.Roster.Count > 0 ? _sched.Roster[0].FloorController : null;

    // 画面の入れ替え。前の画面は捨てる（状態はこちらが持っているので、
    // 画面側に持たせない）。
    public void Show(Phase phase)
    {
        _screen?.QueueFree();
        _screen = null;
        _hud = null;
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
                dep.Initialize(_deployment, _clock);   // 相手の配置は渡さない
                dep.DeployConfirmed += () => Show(Phase.Battle);
                _screen = dep;
                break;

            case Phase.Battle:
                ApplyDeployment();
                if (_sched.CycleNumber == 0) _sched.BeginCycle();

                var hud = new BattleHud();
                AddChild(hud);
                hud.Initialize(_sched, _clock, _session);
                hud.ActorChosen += OnActorChosen;
                hud.CommandChosen += OnCommandChosen;
                hud.TileClicked += OnTileClicked;
                hud.CommitPressed += OnCommit;
                hud.CancelPressed += BeginActorPick;
                _hud = hud;
                _screen = hud;
                BeginActorPick();
                break;
        }

        EmitSignal(SignalName.PhaseChanged, (int)phase);
    }

    // 配置画面で決めた並びを盤面の実体へ反映する。
    // 4匹が6マスの中で入れ替わるだけなので、順に置き直せば衝突しない
    // （PlaceAt は占有表を持たず座標を差し替えるだけ）。
    private void ApplyDeployment()
    {
        if (_deployment == null) return;

        foreach (var (entry, tile) in _deployment.Placements)
        {
            var pal = _sched.Roster.OfType<BattlePal>()
                            .FirstOrDefault(p => p.Faction == Faction.Player && p.Entry == entry)
                      ?? _sched.Roster.OfType<BattlePal>()
                            .FirstOrDefault(p => p.Faction == Faction.Player && p.SpeciesId == entry.SpeciesId);
            pal?.PlaceAt(tile);
        }
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

    // ---- 対戦フェーズ: ① 操作するパルを選ぶ ----

    private void BeginActorPick()
    {
        if (_hud == null) return;

        _actor = null;
        _slot = -2;
        _aim = null;
        _selectable = new List<Vector2I>();
        _hud.ClearChoice();

        var available = _sched.AvailableFor(Faction.Player).ToList();

        // 盤面側でも選べるように、候補のマスを射程色で示す。
        _hud.SetRange(available.Select(e => e.GridPosition), null);
        _hud.ShowActorPicker(available);
        _hud.SetCommitEnabled(false, "操作するパルを選ぶ");

        // 出せるパルが尽きた側は空の提出を出す。空でもターンは進むので、
        // 頭数が偏っても試合が止まらない。
        if (available.Count == 0 && !_session.HasSubmitted(Faction.Player))
        {
            _session.SubmitInput(Faction.Player, new TurnInput(-1, -1, Vector2I.Zero));
            _hud.ShowWaiting("出せるパルがいません。相手の決定を待っています…");
            _opponentDelay = 0.6;
        }
    }

    private void OnActorChosen(int rosterIndex)
    {
        if (_hud == null || rosterIndex < 0 || rosterIndex >= _sched.Roster.Count) return;
        var actor = _sched.Roster[rosterIndex];
        if (actor.Faction != Faction.Player || !actor.IsAlive || _sched.HasActed(actor)) return;

        _actor = actor;
        _slot = -2;
        _aim = null;
        _selectable = new List<Vector2I>();
        _hud.SetRange(System.Array.Empty<Vector2I>(), null);
        _hud.ShowCommands(actor);
        _hud.SetCommitEnabled(false, "行動を選ぶ");
    }

    // ---- ② 行動と狙う先 ----

    private void OnCommandChosen(int slot)
    {
        if (_hud == null || _actor == null) return;

        _slot = slot;
        _aim = null;
        _selectable = SelectableTiles(_actor, slot);

        // 狙う先を要らない技（周囲・部屋・全体）は候補が空になる。その場合は
        // 自分の足下を狙い先に据えて、そのまま提出できるようにする。
        if (_selectable.Count == 0)
        {
            _aim = _actor.GridPosition;
            _hud.SetRange(Preview(_actor, slot, _aim.Value), null);
            _hud.SetCommitEnabled(true, "狙う先の指定は要りません");
        }
        else
        {
            _hud.SetRange(_selectable, null);
            _hud.SetCommitEnabled(false, slot < 0 ? "移動先のマスを選ぶ" : "盤面で狙う先を選ぶ");
        }
    }

    private void OnTileClicked(Vector2I tile)
    {
        if (_hud == null) return;

        // ①の最中は、盤面のマスもパルの選択に使える。行動パネルの一覧と
        // 同じ操作を盤面からもできるようにして、位置で選べるようにする。
        if (_actor == null)
        {
            var pick = _sched.AvailableFor(Faction.Player).FirstOrDefault(e => e.GridPosition == tile);
            if (pick != null) OnActorChosen(_sched.Roster.ToList().IndexOf(pick));
            return;
        }

        if (_slot == -2 || !_selectable.Contains(tile)) return;

        _aim = tile;
        _hud.SetRange(Preview(_actor, _slot, tile), tile);
        _hud.SetCommitEnabled(true, "「決定して伏せる」で提出");
    }

    // 選べるマス。技の射程の形ごとに、どこを指させば意味があるかが変わる。
    private List<Vector2I> SelectableTiles(Entity actor, int slot)
    {
        var grid = Grid;
        var tiles = new List<Vector2I>();
        if (grid == null) return tiles;

        var occupied = _sched.Roster.Where(e => e.IsAlive && e != actor)
                             .Select(e => e.GridPosition).ToHashSet();

        // 移動は隣接8マス。1ターン1マスなので、遠くのマスは指させない。
        if (slot < 0)
        {
            foreach (var d in Neighbours)
            {
                var t = actor.GridPosition + d;
                if (grid.IsWalkable(t) && !occupied.Contains(t)) tiles.Add(t);
            }
            return tiles;
        }

        if (slot >= actor.Moves.Slots.Count) return tiles;
        var data = actor.Moves.Slots[slot].Data;
        if (data == null) return tiles;

        switch (data.Range)
        {
            case MoveRange.Adjacent:
                foreach (var d in Neighbours)
                {
                    var t = actor.GridPosition + d;
                    if (grid.IsWalkable(t)) tiles.Add(t);
                }
                break;

            // 直線と2マスは「向き」を選ぶ技。8方向の射線上のマスをすべて
            // 候補にして、その中の1マスを指すと向きが決まる形にする。
            case MoveRange.Line:
            case MoveRange.TwoTile:
                foreach (var d in Neighbours)
                    tiles.AddRange(TargetResolver.ResolveTiles(
                        data.Range, actor.GridPosition, d, actor.GridPosition, grid, Floor));
                break;

            case MoveRange.Area:
                for (int y = 0; y < BattleBoard.Height; y++)
                    for (int x = 0; x < BattleBoard.Width; x++)
                        if (grid.IsWalkable(new Vector2I(x, y))) tiles.Add(new Vector2I(x, y));
                break;

            // 周囲・部屋・全体は狙う先を持たない。候補なし＝即提出可。
        }
        return tiles;
    }

    // 提出前に「実際どこへ当たるか」を見せる。候補（選べるマス）とは別物で、
    // こちらは指した1マスから決まる着弾の形。
    private List<Vector2I> Preview(Entity actor, int slot, Vector2I aim)
    {
        var grid = Grid;
        if (grid == null) return new List<Vector2I> { aim };
        if (slot < 0) return new List<Vector2I> { aim };
        if (slot >= actor.Moves.Slots.Count) return new List<Vector2I> { aim };

        var data = actor.Moves.Slots[slot].Data;
        if (data == null) return new List<Vector2I> { aim };

        return data.Range switch
        {
            MoveRange.Adjacent => new List<Vector2I> { aim },
            MoveRange.Line or MoveRange.TwoTile => TargetResolver.ResolveTiles(
                data.Range, actor.GridPosition, StepToward(actor.GridPosition, aim),
                aim, grid, Floor),
            _ => TargetResolver.ResolveTiles(data.Range, actor.GridPosition,
                                             actor.FacingDirection, aim, grid, Floor),
        };
    }

    private static readonly Vector2I[] Neighbours =
    {
        new(0, -1), new(1, -1), new(1, 0), new(1, 1),
        new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1),
    };

    // 狙い先へ向かう8方向の単位ベクトル。射線上のマスを指した結果なので、
    // 軸ごとの符号を取るだけで元の方向に戻る。
    private static Vector2I StepToward(Vector2I from, Vector2I to) =>
        new(System.Math.Sign(to.X - from.X), System.Math.Sign(to.Y - from.Y));

    // ---- ③ 提出して待つ → 解決 ----

    private void OnCommit()
    {
        if (_hud == null || _actor == null || _slot == -2 || _aim == null) return;

        var mine = _sched.Roster.Where(e => e.Faction == Faction.Player).ToList();
        var input = new TurnInput(mine.IndexOf(_actor), _slot, _aim.Value);
        if (!_session.SubmitInput(Faction.Player, input)) return;

        _hud.ShowWaiting("相手の決定を待っています…");
        _hud.RefreshClock();

        // 代役が答えるまでの間。実際の通信対戦では相手の回線が決める。
        _opponentDelay = 0.4 + GD.Randf() * 0.8;
    }

    // 両者の入力が揃ったので解決する。相手が何を出したかは、揃ったこの
    // 時点で初めて読める（BattleSession が揃うまで null を返す）。
    private void ResolveTurn()
    {
        var foeInput = _session.PeekOpponentInput(Faction.Player);
        var myInput = _session.PeekOpponentInput(Faction.Enemy);

        var result = _session.ResolveTurn(BuildCommitment, 0.0);   // 経過は_Processが刻む
        if (result == null) return;

        if (myInput.HasValue) LogInput(Faction.Player, myInput.Value);
        if (foeInput.HasValue) LogInput(Faction.Enemy, foeInput.Value);

        // 全員が動き終えたらサイクルを閉じる。状態異常とランク減衰は
        // ここでだけ刻む（対戦仕様§ サイクル管理）。
        if (_sched.CycleComplete)
        {
            _sched.EndCycle();
            _sched.BeginCycle();
            _hud?.AppendLog($"— サイクル {_sched.CycleNumber} —");
        }

        Outcome = result.Value.Outcome;
        if (Outcome != BattleOutcome.Undecided)
        {
            _hud?.Refresh();
            Current = Phase.Finished;
            EmitSignal(SignalName.PhaseChanged, (int)Phase.Finished);
            return;
        }

        _hud?.Refresh();
        BeginActorPick();
    }

    private void LogInput(Faction faction, TurnInput input)
    {
        if (_hud == null) return;
        var mine = _sched.Roster.Where(e => e.Faction == faction).ToList();
        if (input.ActorIndex < 0 || input.ActorIndex >= mine.Count) return;

        var actor = mine[input.ActorIndex];
        string who = (faction == Faction.Player ? "" : "相手の ") + actor.ActorName;
        if (input.IsMove) { _hud.AppendLog($"{who} は移動した"); return; }

        var data = input.MoveSlot < actor.Moves.Slots.Count
            ? actor.Moves.Slots[input.MoveSlot].Data : null;
        _hud.AppendLog($"{who} の {data?.Name ?? "わざ"}！");
    }

    // TurnInput から実際の行動を組み立てる。BattleSession は「揃うまで
    // 伏せる」ことだけに責任を持つので、盤面の実体が要るこの変換はこちら側。
    private BattleScheduler.Commitment BuildCommitment(Faction faction, TurnInput input)
    {
        var mine = _sched.Roster.Where(e => e.Faction == faction).ToList();
        if (input.ActorIndex < 0 || input.ActorIndex >= mine.Count) return default;

        var actor = mine[input.ActorIndex];
        if (!actor.IsAlive || _sched.HasActed(actor)) return default;

        if (input.IsMove)
            return new BattleScheduler.Commitment(actor, new MoveAction(actor, input.Target), 0);

        if (input.MoveSlot >= actor.Moves.Slots.Count) return default;
        var slot = actor.Moves.Slots[input.MoveSlot];

        // 向きは狙い先で決まる。直線・2マスは向きにしか飛ばないので、
        // ここで合わせておかないと指した方向と着弾がずれる。
        var dir = StepToward(actor.GridPosition, input.Target);
        if (dir != Vector2I.Zero) actor.FaceDirection(dir);

        var defender = _sched.Roster.FirstOrDefault(e => e.IsAlive && e.GridPosition == input.Target
                                                         && e != actor);
        return new BattleScheduler.Commitment(
            actor, new AttackAction(actor, defender, slot, Floor), slot.Data?.Priority ?? 0);
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

            case Phase.Deploy:
                _clock.Deploy.Advance(delta);
                if (_screen is DeployScreen d) d.RefreshTimer();

                // 20秒。触らなければ既定配置のまま始まるので、締め切り時点の
                // 並びをそのまま持って対戦へ移る。
                if (_clock.DeployClosed) Show(Phase.Battle);
                break;

            case Phase.Battle:
                _clock.Match.Advance(delta);

                // 毎フレームは時計だけ。盤面はターンが解決したときに
                // 組み直す（Refresh はノードを全部作り直すので重い）。
                if (_screen is BattleHud hud) hud.RefreshClock();

                AdvanceTurn(delta);

                Outcome = _clock.Resolve(_sched);
                if (Outcome != BattleOutcome.Undecided)
                {
                    Current = Phase.Finished;
                    EmitSignal(SignalName.PhaseChanged, (int)Phase.Finished);
                }
                break;
        }
    }

    // 提出済みで相手待ちのときだけ進む。代役の返事が来たら解決へ。
    private void AdvanceTurn(double delta)
    {
        if (!_session.HasSubmitted(Faction.Player)) return;

        if (!_session.HasSubmitted(Faction.Enemy))
        {
            _opponentDelay -= delta;
            if (_opponentDelay > 0.0) return;
            _session.SubmitInput(Faction.Enemy, ScriptedOpponent.Decide(_sched, Faction.Enemy));
        }

        if (_session.BothSubmitted) ResolveTurn();
    }

    // 相手が落ちた場合は投了扱い。試合が止まったままにならないようにする。
    public void OnOpponentLeft()
    {
        Outcome = BattleOutcome.PlayerWin;
        Current = Phase.Finished;
        EmitSignal(SignalName.PhaseChanged, (int)Phase.Finished);
    }
}
