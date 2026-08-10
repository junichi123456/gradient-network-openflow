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
// 盤面のごく一部だけを扱うので、実際の上下関係のまま2つのゾーンを
// 向かい合わせて表示する。相手の陣形は既に決まっているものとして上、
// 自分の6マスを下。2マス空くので、どこを空けるかが最初の読み合いになる。
//
// 画面は常に自分が下。相手側では盤面が180度回って相手自身が下に見える
// （BattleBoard.ToViewOf が担当。ロジックの論理座標は1つだけ）。
public partial class DeployScreen : Control
{
    private BattleDeployment _mine;
    private IReadOnlyDictionary<Vector2I, string> _foeView;   // マス → 種族ID

    private GridContainer _foeZone, _myZone;
    private Label _hint;
    private Button _start;

    public void Initialize(BattleDeployment mine, IReadOnlyDictionary<Vector2I, string> foeView)
    {
        _mine = mine;
        _foeView = foeView;
        BuildLayout();
        Refresh();
    }

    private void BuildLayout()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = BattleTheme.Ground };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var root = Col(10);
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);

        root.AddChild(TopBar("配置", out var bar));
        _hint = Text("2マスを空ける", BattleTheme.Muted, BattleTheme.FontLabel);
        bar.AddChild(_hint);
        bar.AddChild(Spacer());
        _start = new Button { Text = "この配置で開始" };
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
        foreach (var c in zone.GetChildren()) c.QueueFree();

        bool mine = faction == Faction.Player;
        foreach (var tile in BattleDeployment.AvailableTiles(faction))
        {
            string speciesId = mine
                ? _mine.Placements.FirstOrDefault(p => p.Value == tile).Key?.SpeciesId
                : (_foeView.TryGetValue(tile, out var s) ? s : null);

            zone.AddChild(Cell(speciesId, faction));
        }
    }

    // 1マス。空きマスは破線ではなく沈めた面＋「空」で示す。
    // どこが空いているかも情報なので、埋まっているマスと同じ重みで見せる。
    private static Control Cell(string speciesId, Faction faction)
    {
        bool empty = string.IsNullOrEmpty(speciesId);
        var card = Card(empty ? BattleTheme.Sunk : BattleTheme.FactionBg(faction),
                        empty ? BattleTheme.Line : BattleTheme.Faction(faction), 3);
        card.CustomMinimumSize = new Vector2(64, 64);

        var col = Col(2);
        col.Alignment = BoxContainer.AlignmentMode.Center;
        card.AddChild(col);

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
