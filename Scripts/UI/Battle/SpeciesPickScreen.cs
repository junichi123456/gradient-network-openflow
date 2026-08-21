using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Battle;
using MysteryDungeon.Combat;
using MysteryDungeon.Species;
using static MysteryDungeon.UI.Battle.BattleUiKit;

namespace MysteryDungeon.UI.Battle;

// 種族選択。構築画面の1枠に入れるパルを287種から選ぶ。
//
// 287件を素で並べても選べないので、**属性で絞る**のと**並び順を変える**の
// 2つだけ用意した。検索窓を置かないのは、対戦の構築で探すものが
// 「炎の物理型で種族値が高いもの」のような**条件**であって名前ではないため。
//
// 既にパーティに居る種族は押せない（同一種族の重複は規則違反）。誰が
// 使っているかまで出すので、入れ替えたい相手を探しに戻らなくて済む。
public partial class SpeciesPickScreen : Control
{
    [Signal] public delegate void SpeciesChosenEventHandler(string speciesId);
    [Signal] public delegate void CancelledEventHandler();

    private static readonly string[] Elements =
        { "Fire", "Water", "Grass", "Electric", "Ground", "Ice", "Dragon", "Dark", "Neutral" };

    private BattleTeam _team;
    private int _slot;
    private string _filter;              // null = 全属性
    private bool _byBst = true;          // 種族値の高い順 / 図鑑順

    private GridContainer _grid;
    private HBoxContainer _filters;
    private Label _count;

    public void Initialize(BattleTeam team, int slot)
    {
        _team = team;
        _slot = slot;
        BuildLayout();
        Refresh();
    }

    private void BuildLayout()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = BattleTheme.Ground };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var root = Col(8);
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.OffsetLeft = 10; root.OffsetTop = 6;
        root.OffsetRight = -10; root.OffsetBottom = -6;
        AddChild(root);

        root.AddChild(TopBar($"種族を選ぶ — 枠 {_slot + 1}", out var bar));
        _count = Text("", BattleTheme.Muted, BattleTheme.FontLabel);
        bar.AddChild(_count);
        bar.AddChild(Spacer());

        var order = new Button { Text = "種族値順" };
        order.Pressed += () => { _byBst = !_byBst; order.Text = _byBst ? "種族値順" : "図鑑順"; Refresh(); };
        bar.AddChild(order);

        var back = new Button { Text = "戻る" };
        back.Pressed += () => EmitSignal(SignalName.Cancelled);
        bar.AddChild(back);

        _filters = Row(5);
        root.AddChild(_filters);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _grid = new GridContainer { Columns = 4 };
        _grid.AddThemeConstantOverride("h_separation", 6);
        _grid.AddThemeConstantOverride("v_separation", 6);
        _grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_grid);
        root.AddChild(scroll);

        root.AddChild(Text("パーティに居る種族は選べません（同一種族の重複は不可）。",
                           BattleTheme.Muted, BattleTheme.FontSmall));
    }

    public void SetFilter(string element) { _filter = element; Refresh(); }

    public void Refresh()
    {
        BattleUiKit.ClearChildren(_filters);
        _filters.AddChild(FilterChip(null, "すべて"));
        foreach (var e in Elements) _filters.AddChild(FilterChip(e, BattleTheme.ElementLabel(e)));

        var all = SpeciesDatabase.Instance?.All.Values.Where(s => s != null)
                  ?? Enumerable.Empty<SpeciesData>();

        var list = all.Where(s => _filter == null || s.Types.Any(t => t.ToString() == _filter));
        list = _byBst
            ? list.OrderByDescending(s => s.BaseHP + s.BaseAtk + s.BaseDef).ThenBy(s => s.SpeciesId)
            : list.OrderBy(s => s.SpeciesId);

        var shown = list.ToList();
        BattleUiKit.ClearChildren(_grid);
        foreach (var s in shown) _grid.AddChild(SpeciesCard(s));

        _count.Text = $"{shown.Count} 種";
    }

    private Control FilterChip(string element, string label)
    {
        bool on = _filter == element;
        var b = ClickableCard(on ? BattleTheme.BrassBg : BattleTheme.Sunk,
                              on ? BattleTheme.Brass : BattleTheme.Line, 99);
        b.CustomMinimumSize = new Vector2(46, 24);
        string pick = element;
        b.Pressed += () => SetFilter(pick);
        var row = Row(0);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        BattleUiKit.AddFilled(b, row, margin: 2);
        row.AddChild(Text(label,
                          element == null ? BattleTheme.Ink : BattleTheme.Element(element),
                          BattleTheme.FontLabel));
        return b;
    }

    // 1種ぶん。種族値の内訳まで出す（HAD式なので、合計が同じでも
    // HP寄りと攻撃寄りでは役割が違う）。
    private Control SpeciesCard(SpeciesData sp)
    {
        var owner = _team.Entries.FirstOrDefault(e => e.SpeciesId == sp.SpeciesId);
        bool mine = owner != null && owner == _team.Entries.ElementAtOrDefault(_slot);
        bool taken = owner != null && !mine;

        var card = ClickableCard(mine ? BattleTheme.BrassBg : BattleTheme.Surface,
                                 mine ? BattleTheme.Brass : BattleTheme.Line);
        card.CustomMinimumSize = new Vector2(0, 62);
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.Disabled = taken;
        string id = sp.SpeciesId;
        card.Pressed += () => EmitSignal(SignalName.SpeciesChosen, id);

        var col = Col(2);
        BattleUiKit.AddFilled(card, col);

        var head = Row(5);
        head.AddChild(Text(sp.DisplayName, taken ? BattleTheme.Line2 : BattleTheme.Ink,
                           BattleTheme.FontSmall));
        foreach (var t in sp.Types) head.AddChild(ElementChip(t));
        head.AddChild(Spacer());
        head.AddChild(Text($"BST {sp.BaseHP + sp.BaseAtk + sp.BaseDef}",
                           taken ? BattleTheme.Line2 : BattleTheme.Ink2, BattleTheme.FontLabel));
        col.AddChild(head);

        var stats = Row(8);
        stats.AddChild(Text($"HP {sp.BaseHP}", BattleTheme.Muted, 10));
        stats.AddChild(Text($"攻 {sp.BaseAtk}", BattleTheme.Muted, 10));
        stats.AddChild(Text($"防 {sp.BaseDef}", BattleTheme.Muted, 10));
        stats.AddChild(Spacer());
        var trait = TraitDatabase.Get(sp.Trait);
        if (trait != null) stats.AddChild(Text(trait.Name, BattleTheme.Muted, 10));
        col.AddChild(stats);

        if (taken)
            col.AddChild(Text("パーティに登録済み", BattleTheme.Warn, 10));
        return card;
    }
}
