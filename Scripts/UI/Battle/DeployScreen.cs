using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Battle;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;
using MysteryDungeon.Species;
using static MysteryDungeon.UI.Battle.BattleUiKit;

namespace MysteryDungeon.UI.Battle;

// 配置画面。自陣6マスへ4匹を自由に置く（20秒）。
//
// 相手について分かるのは最初に開示された6匹だけ。
// **誰を選出したかも、どこへ置いたかも、対戦開始まで一切分からない。**
// したがって敵陣の6マスはすべて「?」で埋める。4マスが埋まることは
// 規則から分かるが、どの4マスかは分からない、という状態を見せる。
//
// 盤面のごく一部だけを扱うので、実際の上下関係のまま2つのゾーンを
// 向かい合わせて表示する。2マス空くので、どこを空けるかが読み合いになる。
//
// 画面は常に自分が下。相手側では盤面が180度回って相手自身が下に見える
// （BattleBoard.ToViewOf が担当。ロジックの論理座標は1つだけ）。
//
// 操作は2手。①動かす1匹を選ぶ（自陣のマスか、下段のカード）→ ②置き先の
// マスを押す。埋まっているマスを選べば入れ替わる。どちらの手を待っている
// かは上部の案内文が常に示す。
public partial class DeployScreen : Control
{
    // 自陣のマスが押された。配置の更新自体はこの画面が行うので、外へは
    // 「触られた」という事実だけを伝える（ログや通信の足がかり）。
    [Signal] public delegate void TileClickedEventHandler(Vector2I tile);
    [Signal] public delegate void DeployConfirmedEventHandler();

    private BattleDeployment _mine;
    private BattleClock _clock;

    private GridContainer _foeZone, _myZone;
    private HBoxContainer _party;
    private Label _hint, _timer;
    private ProgressBar _gauge;
    private Button _start;

    // いま「動かす対象」として選ばれている1匹。null なら①を待っている。
    private BattleEntry _held;

    // 相手の配置は引数に取らない。渡す口が無ければ漏れようがない。
    public void Initialize(BattleDeployment mine, BattleClock clock = null)
    {
        _mine = mine;
        _clock = clock;
        BuildLayout();
        Refresh();
    }

    private void BuildLayout()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = BattleTheme.Ground };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var root = Col(10);
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.OffsetLeft = 10; root.OffsetTop = 6;
        root.OffsetRight = -10; root.OffsetBottom = -6;
        AddChild(root);

        root.AddChild(TopBar("配置", out var bar));
        _hint = Text("動かすパルを選ぶ", BattleTheme.Ink, BattleTheme.FontLabel);
        bar.AddChild(_hint);
        bar.AddChild(Spacer());

        // 20秒。選出画面と同じ形（帯＋数字）にして、残量の読み方を揃える。
        _gauge = new ProgressBar
        {
            CustomMinimumSize = new Vector2(110, 5), ShowPercentage = false,
            MaxValue = BattleClock.DeployLimitSeconds,
            Value = BattleClock.DeployLimitSeconds,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        bar.AddChild(_gauge);
        _timer = Text("0:20", BattleTheme.Ink, BattleTheme.FontTitle);
        bar.AddChild(_timer);

        _start = new Button { Text = "この配置で開始" };
        _start.Pressed += () => EmitSignal(SignalName.DeployConfirmed);
        bar.AddChild(_start);

        var center = Col(2);
        center.SizeFlagsVertical = SizeFlags.ExpandFill;
        center.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(center);

        _foeZone = Zone();
        center.AddChild(Wrap(_foeZone));
        center.AddChild(Centered("▲ 相手 ／ 自分 ▼", BattleTheme.Muted, BattleTheme.FontLabel));
        _myZone = Zone();
        center.AddChild(Wrap(_myZone));

        center.AddChild(Centered("マスを押して選び、もう一度別のマスを押すと動きます（埋まっていれば入れ替え）。",
                                 BattleTheme.Muted, BattleTheme.FontSmall));
        center.AddChild(Centered("相手の選出と配置は対戦開始まで分かりません。",
                                 BattleTheme.Muted, BattleTheme.FontSmall));

        // 下部の余白は自分の選出4匹の詳細に使う。配置は技の射程と持ち物で
        // 決まるので、それを見ないまま位置だけ考えることはできない。
        root.AddChild(Text("自分の選出 — カードを押しても選べます", BattleTheme.Muted, BattleTheme.FontLabel));
        _party = Row(8);
        // 中央の盤面は伸び縮みしてよいが、この帯は中身が入りきる高さを
        // 確保する。足りないと技の行と持ち物が画面外へ落ちる。
        _party.CustomMinimumSize = new Vector2(0, PartyCardHeight);
        root.AddChild(_party);
    }

    // 種族名＋属性 / 技4行 / 持ち物 が入る高さ。
    private const int PartyCardHeight = 132;

    private static Label Centered(string s, Color c, int size)
    {
        var l = Text(s, c, size);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        return l;
    }

    private static GridContainer Zone()
    {
        var g = new GridContainer { Columns = BattleBoard.FormationWidth };
        g.AddThemeConstantOverride("h_separation", 2);
        g.AddThemeConstantOverride("v_separation", 2);
        return g;
    }

    private static Control Wrap(Control inner)
    {
        var h = Row(0);
        h.Alignment = BoxContainer.AlignmentMode.Center;
        h.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        h.AddChild(inner);
        return h;
    }

    public void Refresh()
    {
        FillZone(_foeZone, Faction.Enemy);
        FillZone(_myZone, Faction.Player);
        FillParty();

        _hint.Text = _held == null
            ? "動かすパルを選ぶ"
            : $"{NameOf(_held)} の置き先を選ぶ（埋まっていれば入れ替え）";
        _hint.AddThemeColorOverride("font_color", _held == null ? BattleTheme.Ink : BattleTheme.Brass);

        var errs = _mine.Validate();
        _start.Disabled = errs.Count > 0;
        RefreshTimer();
    }

    public void RefreshTimer()
    {
        if (_clock == null) return;
        double remain = _clock.Deploy.Remaining;
        _timer.Text = $"{(int)remain / 60}:{(int)remain % 60:D2}";
        _gauge.Value = remain;

        // 残り5秒を切ったら赤へ。20秒なので選出より短い閾値にする。
        bool urgent = remain <= 5.0;
        _timer.AddThemeColorOverride("font_color", urgent ? BattleTheme.Crit : BattleTheme.Ink);
    }

    private static string NameOf(BattleEntry e) => e.Species?.DisplayName ?? e.SpeciesId;

    // マスが押されたときの解釈。①未選択なら「そこにいる1匹を持つ」、
    // ②選択中なら「そこへ置く」。空マスを未選択で押しても何も起きない。
    public void PressTile(Vector2I tile)
    {
        EmitSignal(SignalName.TileClicked, tile);

        if (_held == null)
        {
            _held = _mine.At(tile);
        }
        else
        {
            _mine.Place(_held, tile);
            _held = null;
        }
        Refresh();
    }

    public void PressEntry(BattleEntry entry)
    {
        _held = _held == entry ? null : entry;
        Refresh();
    }

    public BattleEntry Held => _held;

    private void FillZone(GridContainer zone, Faction faction)
    {
        BattleUiKit.ClearChildren(zone);

        // 敵陣は中身を持たない。6マスすべて不明として描く。
        if (faction == Faction.Enemy)
        {
            foreach (var _ in BattleDeployment.AvailableTiles(faction))
                zone.AddChild(UnknownCell());
            return;
        }

        foreach (var tile in BattleDeployment.AvailableTiles(faction))
        {
            var entry = _mine.At(tile);
            var cell = Cell(entry?.SpeciesId, faction, entry != null && entry == _held);
            var here = tile;
            cell.Pressed += () => PressTile(here);
            zone.AddChild(cell);
        }
    }

    // 選出した4匹の詳細。技4つと持ち物まで出す（自分の情報なので伏せる
    // 理由が無く、配置を決める材料そのもの）。
    private void FillParty()
    {
        BattleUiKit.ClearChildren(_party);

        foreach (var entry in _mine.Placements.Keys.OrderBy(e => _mine.Placements[e].Y)
                                    .ThenBy(e => _mine.Placements[e].X))
        {
            bool on = entry == _held;
            var card = ClickableCard(on ? BattleTheme.BrassBg : BattleTheme.Surface,
                                     on ? BattleTheme.Brass : BattleTheme.Line);
            card.CustomMinimumSize = new Vector2(220, PartyCardHeight);
            card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var e = entry;
            card.Pressed += () => PressEntry(e);

            var col = Col(2);
            BattleUiKit.AddFilled(card, col);

            var sp = entry.Species;
            var head = Row(6);
            head.AddChild(Text(NameOf(entry), BattleTheme.Ink, BattleTheme.FontSmall));
            if (sp != null) foreach (var t in sp.Types) head.AddChild(ElementChip(t));
            head.AddChild(Spacer());
            if (sp != null)
                head.AddChild(Text($"BST {sp.BaseHP + sp.BaseAtk + sp.BaseDef}",
                                   BattleTheme.Muted, BattleTheme.FontLabel));
            col.AddChild(head);

            // 技は射程まで出す。どこへ置くかは射程の形で決まるので、
            // 名前と威力だけでは足りない。
            foreach (var mid in entry.MoveIds)
            {
                var m = MoveDatabase.Get(mid);
                if (m == null) continue;
                var r = Row(4);
                r.AddChild(ElementChip(m.Type));
                r.AddChild(Text(m.Name, BattleTheme.Ink2, BattleTheme.FontLabel));
                r.AddChild(Spacer());
                r.AddChild(Text(RangeLabel(m.Range), BattleTheme.Muted, 10));
                r.AddChild(Text(m.Power > 0 ? m.Power.ToString() : "—", BattleTheme.Muted, 10));
                col.AddChild(r);
            }

            var it = string.IsNullOrEmpty(entry.ItemId) ? null : ItemDatabase.Get(entry.ItemId);
            col.AddChild(Text($"持ち物 {it?.Name ?? "なし"}",
                              it == null ? BattleTheme.Muted : BattleTheme.Ink2, 10));

            _party.AddChild(card);
        }
    }

    // 射程の短い表示名。配置の判断に直結するので略さず形が分かる語にする。
    private static string RangeLabel(MoveRange range) => range switch
    {
        MoveRange.Adjacent => "隣接",
        MoveRange.Line => "直線",
        MoveRange.TwoTile => "2マス",
        MoveRange.Area => "範囲",
        MoveRange.Room => "部屋",
        MoveRange.FullFloor => "全体",
        MoveRange.Surrounding => "周囲",
        _ => "—",
    };

    // 相手の1マス。誰がいるか、そもそも居るのかも分からない。
    // 空マスの「空」とは別の見た目にして、「空いている」と「不明」を
    // 取り違えないようにする。
    private static Control UnknownCell()
    {
        var card = Card(BattleTheme.FoeBg, BattleTheme.Foe, 3);
        card.CustomMinimumSize = new Vector2(64, 64);
        var col = Col(2);
        col.Alignment = BoxContainer.AlignmentMode.Center;
        col.AddChild(Text("?", BattleTheme.Foe, BattleTheme.FontTitle));
        card.AddChild(col);
        return card;
    }

    // 1マス。空きマスは破線ではなく沈めた面＋「空」で示す。
    // どこが空いているかも情報なので、埋まっているマスと同じ重みで見せる。
    // 選択中の1匹は真鍮で塗って、次の一手が「置き先の指定」だと分かるようにする。
    private static Button Cell(string speciesId, Faction faction, bool held)
    {
        bool empty = string.IsNullOrEmpty(speciesId);
        var card = ClickableCard(
            held ? BattleTheme.Brass : empty ? BattleTheme.Sunk : BattleTheme.FactionBg(faction),
            held ? BattleTheme.Brass : empty ? BattleTheme.Line : BattleTheme.Faction(faction), 3);
        card.CustomMinimumSize = new Vector2(64, 64);

        var col = Col(2);
        col.Alignment = BoxContainer.AlignmentMode.Center;
        BattleUiKit.AddFilled(card, col);

        if (empty)
        {
            col.AddChild(Text("空", BattleTheme.Muted, BattleTheme.FontSmall));
        }
        else
        {
            var sp = SpeciesDatabase.Instance?.Get(speciesId);
            col.AddChild(Text(sp?.DisplayName ?? speciesId,
                              held ? BattleTheme.Ground : BattleTheme.Faction(faction),
                              BattleTheme.FontLabel));
            if (sp != null && sp.Types.Count > 0)
            {
                var chips = Row(3);
                chips.Alignment = BoxContainer.AlignmentMode.Center;
                foreach (var t in sp.Types) chips.AddChild(ElementChip(t));
                col.AddChild(chips);
            }
        }
        return card;
    }
}
