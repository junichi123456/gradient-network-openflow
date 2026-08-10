using System.Linq;
using Godot;
using MysteryDungeon.Battle;
using MysteryDungeon.Combat;
using MysteryDungeon.Species;
using static MysteryDungeon.UI.Battle.BattleUiKit;

namespace MysteryDungeon.UI.Battle;

// 構築画面。対戦中に変更できない項目（6匹・技4つ・持ち物1つ）をすべて決める。
//
// 1匹ぶんの全情報を1枚のカードに収めて横に並べる。カードを見比べれば
// 属性の偏りと威力帯の穴が分かる形にしている。構築の禁止事項は下部に
// 常時表示し、違反した瞬間に赤へ転じる（BattleTeam.Validate をそのまま出す）。
public partial class TeamBuildScreen : Control
{
    // 構築を確定して選出へ進む。規則を満たしていないと押せない。
    [Signal] public delegate void BuildConfirmedEventHandler();

    private BattleTeam _team;
    private GridContainer _roster;
    private HBoxContainer _rules;
    private Label _count;
    private Button _ready;

    public void Initialize(BattleTeam team)
    {
        _team = team;
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

        root.AddChild(TopBar("パーティ構築", out var bar));
        _count = Text("0 / 6 匹", BattleTheme.Muted, BattleTheme.FontLabel);
        bar.AddChild(_count);
        bar.AddChild(Spacer());
        _ready = new Button { Text = "対戦を受け付ける" };
        _ready.Pressed += () => EmitSignal(SignalName.BuildConfirmed);
        bar.AddChild(_ready);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _roster = new GridContainer { Columns = 3 };
        _roster.AddThemeConstantOverride("h_separation", 10);
        _roster.AddThemeConstantOverride("v_separation", 10);
        scroll.AddChild(_roster);
        root.AddChild(scroll);

        _rules = Row(14);
        root.AddChild(_rules);
    }

    public void Refresh()
    {
        BattleUiKit.ClearChildren(_roster);
        foreach (var e in _team.Entries) _roster.AddChild(SlotCard(e));

        _count.Text = $"{_team.Entries.Count} / {BattleTeam.RosterSize} 匹";

        // 規則の充足は Validate() の結果をそのまま出す。UI側で判定を
        // 書き直すと、生成器と検証器が食い違ったときと同じ事故になる。
        var errors = _team.Validate();
        BattleUiKit.ClearChildren(_rules);
        _rules.AddChild(RuleChip("同一種族なし", !errors.Any(m => m.Contains("同一種族"))));
        _rules.AddChild(RuleChip("持ち物の重複なし", !errors.Any(m => m.Contains("持ち物の重複"))));
        _rules.AddChild(RuleChip("全員が learnset 内の技4つ",
                                 !errors.Any(m => m.Contains("learnset外") || m.Contains("つまで"))));
        _rules.AddChild(RuleChip($"{BattleTeam.RosterSize}匹の登録",
                                 !errors.Any(m => m.Contains("匹ちょうど"))));

        _ready.Disabled = errors.Count > 0;
    }

    // 1匹ぶん。種族 → 技4行 → 持ち物 の順で、上から重要度どおりに積む。
    private Control SlotCard(BattleEntry entry)
    {
        var card = Card(BattleTheme.Surface, BattleTheme.Line);
        card.CustomMinimumSize = new Vector2(258, 0);
        var col = Col(0);
        card.AddChild(col);

        var sp = entry.Species;

        var head = Row(7);
        head.AddChild(Text(sp?.DisplayName ?? entry.SpeciesId, BattleTheme.Ink, BattleTheme.FontBody));
        if (sp != null)
            foreach (var t in sp.Types) head.AddChild(ElementChip(t));
        head.AddChild(Spacer());
        if (sp != null)
            head.AddChild(Text($"BST {sp.BaseHP + sp.BaseAtk + sp.BaseDef}",
                               BattleTheme.Muted, BattleTheme.FontLabel));
        col.AddChild(head);

        var body = Col(5);
        foreach (var mid in entry.MoveIds)
        {
            var m = MoveDatabase.Get(mid);
            if (m == null) continue;
            var r = Row(6);
            r.AddChild(ElementChip(m.Type));
            r.AddChild(CategoryChip(m.Category));
            r.AddChild(Text(m.Name, BattleTheme.Ink, BattleTheme.FontSmall));
            r.AddChild(Spacer());
            r.AddChild(Text(m.Power > 0 ? m.Power.ToString() : "—",
                            BattleTheme.Muted, BattleTheme.FontSmall));
            body.AddChild(r);
        }

        var item = Row(6);
        item.AddChild(Text("持ち物", BattleTheme.Muted, 10));
        var it = string.IsNullOrEmpty(entry.ItemId) ? null : ItemDatabase.Get(entry.ItemId);
        item.AddChild(Text(it?.Name ?? "なし",
                           it == null ? BattleTheme.Muted : BattleTheme.Ink2, BattleTheme.FontSmall));
        body.AddChild(item);

        col.AddChild(body);
        return card;
    }
}
