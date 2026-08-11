using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Battle;
using MysteryDungeon.Entities;

namespace MysteryDungeon.UI.Battle;

// 対戦画面。設計案の第4画面をそのまま組んだもの。
//
// 構成（上から）
//   上部バー   … サイクル/ターン、両者の提出状態、20分の残り時間
//   行動順レール… 合計種族値を数字ごと並べる。低い順に動くという規則を
//                 覚えてもらうため、隠さず見せる
//   盤面       … 8行x7列。射程内と狙う先を地色で示す
//   右カラム   … 行動選択・残りの頭数・戦闘ログ
//
// 相手の状態は「選択中…／決定済み」の2値しか出さない。伏せて同時に決める
// 規則なので、中身は解決まで見せない。**相手がどのパルを操作しているかも
// 中身のうち**なので、レールで「いま行動」と名指しできるのは自分側だけ。
//
// 1ターンの操作は3段。
//   ① 操作するパルを選ぶ  … このサイクルでまだ動いていない自分のパル
//   ② 行動を選ぶ          … 技4つ＋移動。選ぶと射程が盤面に出る
//   ③ 伏せて提出し、待つ  … 両者が出すまで何も起きない → 解決
public partial class BattleHud : Control
{
    // 操作するパルが選ばれた。_sched.Roster の添字を渡す。
    [Signal] public delegate void ActorChosenEventHandler(int rosterIndex);
    // 技枠が選ばれた（移動は -1）。狙う先はこのあと盤面のクリックで決める。
    [Signal] public delegate void CommandChosenEventHandler(int moveSlot);
    // 盤面のマスが押された。狙う先の指定に使う。
    [Signal] public delegate void TileClickedEventHandler(Vector2I tile);
    // 「決定して伏せる」。ここで初めて提出される。
    [Signal] public delegate void CommitPressedEventHandler();
    // 「別のパルにする」。①へ戻る。
    [Signal] public delegate void CancelPressedEventHandler();

    // 盤面1マスの辺長。8行7列なので、これで盤面全体の寸法が決まる。
    private const int TileSize = 56;

    private Button _commit;
    private int _chosenSlot = -2;   // -2 = 未選択 / -1 = 移動 / 0.. = 技枠

    // 自分が操作すると決めたパル。①が済むまで null。レールの「操作中」も
    // これだけを見る（相手側は絶対に名指ししない）。
    private Entity _actor;

    // 提出できるか。狙う先が要る技かどうかは射程で変わり、その判断は
    // BattleFlow が持っているので、有効/無効は外から与えてもらう。
    // 画面を組み直すたびに既定値へ戻ると、選び終えても押せない画面になる。
    private bool _commitEnabled;

    // 次に何をすればよいかの一行。行動パネルの中に置く（盤面の端に出すと
    // 視線が行動選択から離れる）。
    private string _hint = "";
    private Label _hintLabel;

    private BattleScheduler _sched;
    private BattleClock _clock;
    private BattleSession _session;

    private Label _phase, _timer, _selfState, _foeState;
    private ProgressBar _timeGauge;
    private HBoxContainer _rail;
    private GridContainer _board;
    private VBoxContainer _commands, _rosterList, _log;

    private readonly Dictionary<Entity, HpBar> _boardHp = new();
    private readonly Dictionary<Entity, HpBar> _rosterHp = new();

    // 射程表示。選んだ技で変わるので、盤面の再描画時に参照する。
    private HashSet<Vector2I> _rangeTiles = new();
    private Vector2I? _aimTile;

    public void Initialize(BattleScheduler sched, BattleClock clock, BattleSession session)
    {
        _sched = sched;
        _clock = clock;
        _session = session;
        BuildLayout();
        Refresh();
    }

    // ---- レイアウト ----

    private void BuildLayout()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var bg = new ColorRect { Color = BattleTheme.Ground };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var root = new VBoxContainer { Name = "Root" };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // 画面の縁に face を貼り付けない。全画面表示では実際に文字が切れる。
        root.OffsetLeft = 10; root.OffsetTop = 6;
        root.OffsetRight = -10; root.OffsetBottom = -6;
        root.AddThemeConstantOverride("separation", 10);
        AddChild(root);

        root.AddChild(BuildTopBar());
        root.AddChild(BuildRail());

        var split = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 12);
        root.AddChild(split);

        var left = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        // 盤面は寸法が固定（56px x 7列）なので、伸ばすのではなく余白の
        // 真ん中へ置く。GridContainer に ExpandFill を与えると矩形だけが
        // 広がり、中身は左上に寄ったまま——それが左詰めの正体だった。
        // 横方向は Alignment.Center の HBox、縦方向は同じく VBox に任せる。
        var boardRow = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        _board = new GridContainer
        {
            Columns = BattleBoard.Width,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        _board.AddThemeConstantOverride("h_separation", 2);
        _board.AddThemeConstantOverride("v_separation", 2);
        boardRow.AddChild(_board);
        left.AddChild(boardRow);
        split.AddChild(left);

        var side = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(280, 0),
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
        };
        side.AddThemeConstantOverride("separation", 10);
        _commands = Card(side, "行動");
        _rosterList = Card(side, "残り");
        _log = Card(side, "戦闘ログ");
        split.AddChild(side);
    }

    private Control BuildTopBar()
    {
        var bar = new PanelContainer();
        bar.AddThemeStyleboxOverride("panel", BattleTheme.Panel(BattleTheme.Raised, BattleTheme.Line));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        bar.AddChild(row);

        _phase = Text("サイクル 1 · ターン 1 / 4", BattleTheme.Muted, BattleTheme.FontLabel);
        row.AddChild(_phase);

        _selfState = Pill("自分 選択中…", BattleTheme.Muted, BattleTheme.Sunk);
        _foeState = Pill("相手 選択中…", BattleTheme.Muted, BattleTheme.Sunk);
        row.AddChild(_selfState);
        row.AddChild(_foeState);

        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        // 残り時間は数字より帯のほうが視認が速いので両方出す。
        _timeGauge = new ProgressBar
        {
            CustomMinimumSize = new Vector2(110, 5),
            ShowPercentage = false, MaxValue = BattleClock.MatchLimitSeconds,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        row.AddChild(_timeGauge);

        _timer = Text("20:00", BattleTheme.Ink, BattleTheme.FontTitle);
        row.AddChild(_timer);
        return bar;
    }

    private Control BuildRail()
    {
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 52),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _rail = new HBoxContainer();
        _rail.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_rail);
        return scroll;
    }

    private VBoxContainer Card(Container parent, string title)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", BattleTheme.Panel(BattleTheme.Surface, BattleTheme.Line));
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 6);
        card.AddChild(col);

        col.AddChild(Text(title, BattleTheme.Muted, BattleTheme.FontLabel));
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        col.AddChild(body);

        parent.AddChild(card);
        return body;
    }

    // ---- 描画の更新 ----

    // 時計だけの更新。毎フレーム呼ばれる想定なので、ノードを作り直さない。
    // 盤面ごと組み直すと1フレームに数百個の stylebox を作ることになり、
    // ソフトウェア描画では実際に固まった。
    public void RefreshClock() => RefreshTopBar();

    // 盤面まで含めた組み直し。状態が動いたときだけ呼ぶ。
    public void Refresh()
    {
        RefreshTopBar();
        RefreshRail();
        RefreshBoard();
        RefreshRoster();
    }

    private void RefreshTopBar()
    {
        _phase.Text = $"サイクル {_sched.CycleNumber} · ターン {_sched.TurnInCycle} / {BattleTeam.SelectionSize}";

        double remain = _clock.Match.Remaining;
        _timer.Text = $"{(int)remain / 60}:{(int)remain % 60:D2}";
        _timeGauge.Value = remain;

        // 残り1分を切ったら赤へ。数字と帯の両方で分かるようにする。
        bool urgent = remain <= 60.0;
        _timer.AddThemeColorOverride("font_color", urgent ? BattleTheme.Crit : BattleTheme.Ink);

        SetPill(_selfState, _session != null && _session.HasSubmitted(Faction.Player),
                "自分 決定済み", "自分 選択中…");
        SetPill(_foeState, _session != null && _session.HasSubmitted(Faction.Enemy),
                "相手 決定済み", "相手 選択中…");
    }

    // 相手については「出したかどうか」だけを見せる。中身は解決まで伏せる。
    private static void SetPill(Label pill, bool done, string doneText, string waitText)
    {
        pill.Text = done ? doneText : waitText;
        pill.AddThemeColorOverride("font_color", done ? BattleTheme.Brass : BattleTheme.Muted);
    }

    // 行動順レール。種族値を隠さず並べ、左ほど先に動くことを見せる。
    //
    // ここに出してよいのは公開情報だけ。種族値も、そのサイクルで既に動いたか
    // どうかも、解決を見ていれば分かるので公開情報にあたる。
    // 一方「このターンに誰を操作するか」は伏せて同時に決める対象なので、
    // 名指しできるのは自分が選んだ1匹（操作中）に限る。相手側は、たとえ
    // 提出済みでも誰を出したかを示さない。
    private void RefreshRail()
    {
        BattleUiKit.ClearChildren(_rail);

        var order = _sched.Roster
            .Where(e => e.IsAlive)
            .OrderBy(BattleScheduler.Bst)
            .ToList();

        foreach (var e in order)
        {
            var card = new PanelContainer { CustomMinimumSize = new Vector2(78, 0) };
            bool acted = _sched.HasActed(e);
            bool operating = e == _actor;      // 自分の選択のみ。相手には付かない
            card.AddThemeStyleboxOverride("panel", BattleTheme.Panel(
                operating ? BattleTheme.BrassBg : BattleTheme.Surface,
                operating ? BattleTheme.Brass : BattleTheme.Line, 4));

            var col = new VBoxContainer();
            col.AddThemeConstantOverride("separation", 1);
            col.AddChild(Text(e.ActorName, BattleTheme.Faction(e.Faction), BattleTheme.FontLabel));
            col.AddChild(Text($"BST {BattleScheduler.Bst(e)}", BattleTheme.Muted, 10));
            col.AddChild(operating ? Text("操作中", BattleTheme.Brass, 9)
                                   : Text(acted ? "行動済" : "未行動", BattleTheme.Muted, 9));
            card.AddChild(col);
            _rail.AddChild(card);
        }
    }

    private void RefreshBoard()
    {
        BattleUiKit.ClearChildren(_board);
        _boardHp.Clear();

        var occupied = _sched.Roster.Where(e => e.IsAlive)
                              .ToDictionary(e => e.GridPosition, e => e);

        for (int y = 0; y < BattleBoard.Height; y++)
            for (int x = 0; x < BattleBoard.Width; x++)
            {
                var pos = new Vector2I(x, y);

                // 射程内 → 真鍮の地。狙う先 → 真鍮で塗りつぶし。
                bool inRange = _rangeTiles.Contains(pos);
                bool isAim = _aimTile == pos;
                var tile = BattleUiKit.ClickableCard(
                    isAim ? BattleTheme.Brass : inRange ? BattleTheme.BrassBg : BattleTheme.Surface,
                    inRange || isAim ? BattleTheme.Brass : BattleTheme.Surface, 2);
                tile.CustomMinimumSize = new Vector2(TileSize, TileSize);
                tile.ClipContents = true;
                var here = pos;
                tile.Pressed += () => EmitSignal(SignalName.TileClicked, here);

                if (occupied.TryGetValue(pos, out var e))
                    BattleUiKit.AddFilled(tile, UnitChip(e), margin: 2);
                _board.AddChild(tile);
            }
    }

    // 盤上の1体。名前とHPだけ。狭いので属性は色で示す。
    private Control UnitChip(Entity e)
    {
        var box = new PanelContainer { ClipContents = true };
        box.AddThemeStyleboxOverride("panel", BattleTheme.Panel(
            BattleTheme.FactionBg(e.Faction), BattleTheme.Faction(e.Faction), 2, 2));

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 2);

        // マス幅は狭い。名前は詰めて省略する（はみ出すと隣のマスへ被る）。
        var nm = Text(e.ActorName, BattleTheme.Faction(e.Faction), 10);
        nm.ClipText = true;
        nm.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        nm.HorizontalAlignment = HorizontalAlignment.Center;
        nm.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        col.AddChild(nm);

        // HPバーは高さ固定。VBox に任せると縦に伸びて名前を押しつぶす。
        var hp = new HpBar
        {
            CustomMinimumSize = new Vector2(0, 5),
            SizeFlagsVertical = SizeFlags.ShrinkEnd,
        };
        hp.SetHp(e.Stats.CurrentHp, e.Stats.MaxHp);
        col.AddChild(hp);
        _boardHp[e] = hp;

        box.AddChild(col);
        return box;
    }

    private void RefreshRoster()
    {
        BattleUiKit.ClearChildren(_rosterList);
        _rosterHp.Clear();

        foreach (var e in _sched.Roster.Where(e => e.Faction == Faction.Player))
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);

            string mark = !e.IsAlive ? "×" : _sched.HasActed(e) ? "済" : "未";
            row.AddChild(Pill(mark, e.IsAlive ? BattleTheme.Ink2 : BattleTheme.Crit, BattleTheme.Sunk));
            row.AddChild(Text(e.ActorName, BattleTheme.Ink, BattleTheme.FontSmall));
            row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

            var hp = new HpBar { CustomMinimumSize = new Vector2(66, 7) };
            hp.SetHp(e.Stats.CurrentHp, e.Stats.MaxHp);
            row.AddChild(hp);
            _rosterHp[e] = hp;

            _rosterList.AddChild(row);
        }
    }

    // ① 操作するパルを選ぶ。候補はこのサイクルでまだ動いていない自分のパル。
    // 誰も残っていなければ「出せるパルがいない」と明示する（黙って空欄に
    // すると、操作を受け付けていないのか壊れているのか区別できない）。
    public void ShowActorPicker(IReadOnlyList<Entity> available)
    {
        _actor = null;
        _chosenSlot = -2;
        _commitEnabled = false;
        BattleUiKit.ClearChildren(_commands);

        _commands.AddChild(Text("操作するパルを選ぶ", BattleTheme.Ink, BattleTheme.FontLabel));

        if (available == null || available.Count == 0)
        {
            _commands.AddChild(Text("このサイクルで出せるパルがいません",
                                    BattleTheme.Muted, BattleTheme.FontSmall));
            return;
        }

        foreach (var e in available)
        {
            var btn = BattleUiKit.ClickableCard(BattleTheme.Surface, BattleTheme.Line, 4);
            int index = _sched.Roster.ToList().IndexOf(e);
            btn.Pressed += () => EmitSignal(SignalName.ActorChosen, index);

            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(Text(e.ActorName, BattleTheme.Ink, BattleTheme.FontSmall));
            row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

            var hp = new HpBar { CustomMinimumSize = new Vector2(54, 6), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            hp.SetHp(e.Stats.CurrentHp, e.Stats.MaxHp);
            row.AddChild(hp);
            row.AddChild(Text($"BST {BattleScheduler.Bst(e)}", BattleTheme.Muted, 10));

            BattleUiKit.AddFilled(btn, row);
            _commands.AddChild(btn);
        }
    }

    // ③ 提出したあと。相手が出すまでは何も操作させない。
    public void ShowWaiting(string message)
    {
        BattleUiKit.ClearChildren(_commands);
        _commands.AddChild(Text("提出済み", BattleTheme.Brass, BattleTheme.FontLabel));
        _commands.AddChild(Text(message, BattleTheme.Muted, BattleTheme.FontSmall));
    }

    // ② 行動中のパルの技を並べる。移動も1つの選択肢として同列に置く。
    public void ShowCommands(Entity actor)
    {
        BattleUiKit.ClearChildren(_commands);
        if (actor == null) return;
        bool actorChanged = _actor != actor;
        _actor = actor;

        _commands.AddChild(Text(actor.ActorName, BattleTheme.Ink, BattleTheme.FontBody));

        for (int i = 0; i < actor.Moves.Slots.Count; i++)
        {
            var slot = actor.Moves.Slots[i];
            bool on = _chosenSlot == i;
            var btn = BattleUiKit.ClickableCard(on ? BattleTheme.BrassBg : BattleTheme.Surface,
                                                on ? BattleTheme.Brass : BattleTheme.Line, 4);
            int index = i;
            btn.Pressed += () => ChooseCommand(index, actor);

            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(Text(BattleTheme.ElementLabel(slot.Data.Type),
                              BattleTheme.Element(slot.Data.Type), BattleTheme.FontLabel));
            row.AddChild(Text(slot.Data.Name, BattleTheme.Ink, BattleTheme.FontSmall));
            row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
            row.AddChild(Text(slot.Data.Power > 0 ? slot.Data.Power.ToString() : "—",
                              BattleTheme.Muted, BattleTheme.FontSmall));
            BattleUiKit.AddFilled(btn, row);
            _commands.AddChild(btn);
        }

        var moveBtn = BattleUiKit.ClickableCard(
            _chosenSlot == -1 ? BattleTheme.BrassBg : BattleTheme.Surface,
            _chosenSlot == -1 ? BattleTheme.Brass : BattleTheme.Line, 4);
        moveBtn.Pressed += () => ChooseCommand(-1, actor);
        var mrow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        mrow.AddChild(Text("移動する", BattleTheme.Ink2, BattleTheme.FontSmall));
        BattleUiKit.AddFilled(moveBtn, mrow);
        _commands.AddChild(moveBtn);

        _hintLabel = Text(_hint, BattleTheme.Muted, 10);
        _hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _commands.AddChild(_hintLabel);

        // 提出は選び終えてから。押すまでは相手に何も伝わらない。
        // 有効/無効は SetCommitEnabled で与えられた状態から引く。ここで
        // 固定値を入れると、選び直しの組み直しごとに状態が飛ぶ。
        _commit = new Button { Text = "決定して伏せる", Disabled = !_commitEnabled };
        _commit.Pressed += () => EmitSignal(SignalName.CommitPressed);
        _commands.AddChild(_commit);

        var back = new Button { Text = "別のパルにする" };
        back.Pressed += () => EmitSignal(SignalName.CancelPressed);
        _commands.AddChild(back);

        // レールは「操作中」の1匹が変わったときだけ描き直す。技を選び直す
        // たびに組み直すと、1ターンに何度も全カードを作ることになる。
        if (actorChanged) RefreshRail();
    }

    // 射程だけを差し替える。盤面は描き直さないので、直後に Refresh() を
    // 呼ぶ場合に使う（同じフレームで盤面を2回組み直さないため）。
    public void SetRangeQuiet(IEnumerable<Vector2I> tiles, Vector2I? aim)
    {
        _rangeTiles = new HashSet<Vector2I>(tiles);
        _aimTile = aim;
    }

    // 技/移動を選んだ時点で射程を出す。提出はまだしない。
    private void ChooseCommand(int slot, Entity actor)
    {
        _chosenSlot = slot;
        EmitSignal(SignalName.CommandChosen, slot);
        ShowCommands(actor);   // 選択状態を反映して組み直す
    }

    public int ChosenSlot => _chosenSlot;
    public Entity Operating => _actor;

    // 提出できるかどうかと、次に何をすればよいかの案内文。どちらも
    // 「狙う先が要る技か」を知っている BattleFlow 側から与えられる。
    public void SetCommitEnabled(bool enabled, string hint = null)
    {
        _commitEnabled = enabled;
        _hint = hint ?? "";
        if (_commit != null) _commit.Disabled = !enabled;
        if (_hintLabel != null) _hintLabel.Text = _hint;
    }

    // ターンが解決したら選択を捨てる。次のターンはまた白紙から。
    public void ClearChoice()
    {
        _actor = null;
        _chosenSlot = -2;
        _commitEnabled = false;
        _aimTile = null;
        _rangeTiles.Clear();
    }

    public void SetRange(IEnumerable<Vector2I> tiles, Vector2I? aim)
    {
        _rangeTiles = new HashSet<Vector2I>(tiles);
        _aimTile = aim;
        RefreshBoard();
    }

    public void AppendLog(string line)
    {
        _log.AddChild(Text(line, BattleTheme.Ink2, BattleTheme.FontSmall));

        // QueueFree だけでは子から外れない（解放はフレーム末まで遅れる）。
        // 外れないまま件数を見ると、この while はフレームが来るまで
        // 永久に回り続ける——1フレーム内で複数行を吐く経路（通し検証）で
        // 実際に止まった。ツリーからは即座に外し、解放だけを遅らせる。
        while (_log.GetChildCount() > LogLines)
        {
            var oldest = _log.GetChild(0);
            _log.RemoveChild(oldest);
            oldest.QueueFree();
        }
    }

    private const int LogLines = 8;

    // ---- 小物 ----

    private static Label Text(string s, Color c, int size)
    {
        var l = new Label { Text = s };
        l.AddThemeColorOverride("font_color", c);
        l.AddThemeFontSizeOverride("font_size", size);
        return l;
    }

    private static Label Pill(string s, Color fg, Color bg)
    {
        var l = Text(s, fg, BattleTheme.FontLabel);
        var sb = BattleTheme.Panel(bg, bg, 99);
        sb.SetContentMarginAll(3);
        l.AddThemeStyleboxOverride("normal", sb);
        return l;
    }
}
