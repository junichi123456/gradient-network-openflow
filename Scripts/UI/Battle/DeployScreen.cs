using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Battle;
using MysteryDungeon.Entities;
using MysteryDungeon.Species;
using static MysteryDungeon.UI.Battle.BattleUiKit;

namespace MysteryDungeon.UI.Battle;

// 配置画面。自陣6マスへ4匹を自由に置く。
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
public partial class DeployScreen : Control
{
    // 自陣のマスが押された。空マスなら「ここへ置く」、埋まっていれば
    // 「どかす」。どちらの意味かは呼び出し側が決める。
    [Signal] public delegate void TileClickedEventHandler(Vector2I tile);
    [Signal] public delegate void DeployConfirmedEventHandler();

    private BattleDeployment _mine;

    private GridContainer _foeZone, _myZone;
    private Label _hint;
    private Button _start;

    // 相手の配置は引数に取らない。渡す口が無ければ漏れようがない。
    public void Initialize(BattleDeployment mine)
    {
        _mine = mine;
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
        AddChild(root);

        root.AddChild(TopBar("配置", out var bar));
        _hint = Text("2マスを空ける", BattleTheme.Muted, BattleTheme.FontLabel);
        bar.AddChild(_hint);
        bar.AddChild(Spacer());
        _start = new Button { Text = "この配置で開始" };
        _start.Pressed += () => EmitSignal(SignalName.DeployConfirmed);
        bar.AddChild(_start);

        var center = Col(2);
        center.SizeFlagsVertical = SizeFlags.ExpandFill;
        center.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(center);

        _foeZone = Zone();
        center.AddChild(Wrap(_foeZone));
        center.AddChild(Text("▲ 相手 ／ 自分 ▼", BattleTheme.Muted, BattleTheme.FontLabel));
        _myZone = Zone();
        center.AddChild(Wrap(_myZone));

        center.AddChild(Text("相手の選出と配置は対戦開始まで分かりません。",
                             BattleTheme.Muted, BattleTheme.FontSmall));
        center.AddChild(Text("画面は常に自分が下。相手側では盤面が180度回っています。",
                             BattleTheme.Muted, BattleTheme.FontSmall));
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

        int placed = _mine.Placements.Count;
        int free = BattleBoard.FormationWidth * BattleBoard.FormationHeight - placed;
        _hint.Text = $"{free}マスを空ける";

        var errs = _mine.Validate();
        _start.Disabled = errs.Count > 0;
    }

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
            string speciesId = _mine.Placements.FirstOrDefault(p => p.Value == tile).Key?.SpeciesId;
            var cell = Cell(speciesId, faction);
            var here = tile;
            cell.Pressed += () => EmitSignal(SignalName.TileClicked, here);
            zone.AddChild(cell);
        }
    }

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
    private static Button Cell(string speciesId, Faction faction)
    {
        bool empty = string.IsNullOrEmpty(speciesId);
        var card = ClickableCard(empty ? BattleTheme.Sunk : BattleTheme.FactionBg(faction),
                                 empty ? BattleTheme.Line : BattleTheme.Faction(faction), 3);
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
                              BattleTheme.Faction(faction), BattleTheme.FontLabel));
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
