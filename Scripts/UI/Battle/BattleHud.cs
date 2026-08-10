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
// 規則なので、中身は解決まで見せない。
public partial class BattleHud : Control
{
    // 技枠が選ばれた（移動は -1）。狙う先はこのあと盤面のクリックで決める。
    [Signal] public delegate void CommandChosenEventHandler(int moveSlot);
    // 盤面のマスが押された。狙う先の指定に使う。
    [Signal] public delegate void TileClickedEventHandler(Vector2I tile);
    // 「決定して伏せる」。ここで初めて提出される。
    [Signal] public delegate void CommitPressedEventHandler();

    private Button _commit;
    private int _chosenSlot = -2;   // -2 = 未選択 / -1 = 移動 / 0.. = 技枠

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
        SetAnchorsPreset(LayoutPreset.FullRect);

        var bg = new ColorRect { Color = BattleTheme.Ground };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var root = new VBoxContainer { Name = "Root" };
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 10);
        AddChild(root);

        root.AddChild(BuildTopBar());
        root.AddChild(BuildRail());

        var split = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 12);
        root.AddChild(split);

        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _board = new GridContainer { Columns = BattleBoard.Width };
        _board.AddThemeConstantOverride("h_separation", 2);
        _board.AddThemeConstantOverride("v_separation", 2);
        left.AddChild(_board);
        split.AddChild(left);

        var side = new VBoxContainer { CustomMinimumSize = new Vector2(268, 0) };
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
    private void RefreshRail()
    {
        foreach (var c in _rail.GetChildren()) c.QueueFree();

        var order = _sched.Roster
            .Where(e => e.IsAlive)
            .OrderBy(BattleScheduler.Bst)
            .ToList();

        for (int i = 0; i < order.Count; i++)
        {
            var e = order[i];
            var card = new PanelContainer { CustomMinimumSize = new Vector2(78, 0) };
            bool acting = i == 0 && !_sched.HasActed(e);
            card.AddThemeStyleboxOverride("panel", BattleTheme.Panel(
                acting ? BattleTheme.BrassBg : BattleTheme.Surface,
                acting ? BattleTheme.Brass : BattleTheme.Line, 4));

            var col = new VBoxContainer();
            col.AddThemeConstantOverride("separation", 1);
            col.AddChild(Text(e.ActorName, BattleTheme.Faction(e.Faction), BattleTheme.FontLabel));
            col.AddChild(Text($"BST {BattleScheduler.Bst(e)}", BattleTheme.Muted, 10));
            if (acting) col.AddChild(Text("いま行動", BattleTheme.Brass, 9));
            card.AddChild(col);
            _rail.AddChild(card);
        }
    }

    private void RefreshBoard()
    {
        foreach (var c in _board.GetChildren()) c.QueueFree();
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
                tile.CustomMinimumSize = new Vector2(38, 38);
                var here = pos;
                tile.Pressed += () => EmitSignal(SignalName.TileClicked, here);

                if (occupied.TryGetValue(pos, out var e))
                {
                    var chip = UnitChip(e);
                    chip.MouseFilter = MouseFilterEnum.Ignore;
                    tile.AddChild(chip);
                }
                _board.AddChild(tile);
            }
    }

    // 盤上の1体。名前とHPだけ。狭いので属性は色で示す。
    private Control UnitChip(Entity e)
    {
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", BattleTheme.Panel(
            BattleTheme.FactionBg(e.Faction), BattleTheme.Faction(e.Faction), 2, 2));

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 1);
        col.AddChild(Text(e.ActorName, BattleTheme.Faction(e.Faction), 9));

        var hp = new HpBar();
        hp.SetHp(e.Stats.CurrentHp, e.Stats.MaxHp);
        col.AddChild(hp);
        _boardHp[e] = hp;

        box.AddChild(col);
        return box;
    }

    private void RefreshRoster()
    {
        foreach (var c in _rosterList.GetChildren()) c.QueueFree();
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

    // 行動中のパルの技を並べる。移動も1つの選択肢として同列に置く。
    public void ShowCommands(Entity actor)
    {
        foreach (var c in _commands.GetChildren()) c.QueueFree();
        if (actor == null) return;

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
            btn.AddChild(row);
            _commands.AddChild(btn);
        }

        var moveBtn = BattleUiKit.ClickableCard(
            _chosenSlot == -1 ? BattleTheme.BrassBg : BattleTheme.Surface,
            _chosenSlot == -1 ? BattleTheme.Brass : BattleTheme.Line, 4);
        moveBtn.Pressed += () => ChooseCommand(-1, actor);
        var mrow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        mrow.AddChild(Text("移動する", BattleTheme.Ink2, BattleTheme.FontSmall));
        moveBtn.AddChild(mrow);
        _commands.AddChild(moveBtn);

        // 提出は選び終えてから。押すまでは相手に何も伝わらない。
        _commit = new Button { Text = "決定して伏せる", Disabled = true };
        _commit.Pressed += () => EmitSignal(SignalName.CommitPressed);
        _commands.AddChild(_commit);
    }

    // 技/移動を選んだ時点で射程を出す。提出はまだしない。
    private void ChooseCommand(int slot, Entity actor)
    {
        _chosenSlot = slot;
        if (_commit != null) _commit.Disabled = false;
        EmitSignal(SignalName.CommandChosen, slot);
        ShowCommands(actor);   // 選択状態を反映して組み直す
    }

    public int ChosenSlot => _chosenSlot;

    // ターンが解決したら選択を捨てる。次のターンはまた白紙から。
    public void ClearChoice()
    {
        _chosenSlot = -2;
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
        while (_log.GetChildCount() > 8) _log.GetChild(0).QueueFree();
    }

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
