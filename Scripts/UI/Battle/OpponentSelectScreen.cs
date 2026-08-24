using System.Collections.Generic;
using Godot;
using MysteryDungeon.Battle;
using static MysteryDungeon.UI.Battle.BattleUiKit;

namespace MysteryDungeon.UI.Battle;

// 相手を選ぶ画面。マッチングが実装できる段階にないので、対戦の相手は
// NPCから選ぶ。
//
// **ここで出すのは名前・主属性・合計種族値だけ。** 相手の6匹は選出画面で
// 開示される（§14の開示順）ので、この画面で見せてしまうと「構築より先に
// 相手の編成が分かる」ことになり、開示の順序が崩れる。
// 合計種族値は強さのおおよその目安として出す——並び順もこれで決まっている。
public partial class OpponentSelectScreen : Control
{
    [Signal] public delegate void OpponentConfirmedEventHandler();

    private IReadOnlyList<NpcTeam> _teams;
    private NpcTeam _chosen;

    private VBoxContainer _list;
    private Button _go;

    public NpcTeam Chosen => _chosen;

    public void Initialize(IReadOnlyList<NpcTeam> teams)
    {
        _teams = teams;
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

        root.AddChild(TopBar("対戦相手", out var bar));
        bar.AddChild(Text("弱い順に並んでいます", BattleTheme.Muted, BattleTheme.FontLabel));
        bar.AddChild(Spacer());
        _go = new Button { Text = "この相手と戦う", Disabled = true };
        _go.Pressed += () => EmitSignal(SignalName.OpponentConfirmed);
        bar.AddChild(_go);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _list = Col(6);
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_list);
        root.AddChild(scroll);

        root.AddChild(Text("相手の6匹は選出画面で開示されます。技と持ち物は開示されません。",
                           BattleTheme.Muted, BattleTheme.FontSmall));
    }

    public void Refresh()
    {
        BattleUiKit.ClearChildren(_list);
        for (int i = 0; i < _teams.Count; i++) _list.AddChild(Row(i + 1, _teams[i]));
        if (_go != null) _go.Disabled = _chosen == null;
    }

    private Control Row(int no, NpcTeam t)
    {
        bool on = t == _chosen;
        var card = ClickableCard(on ? BattleTheme.BrassBg : BattleTheme.Surface,
                                 on ? BattleTheme.Brass : BattleTheme.Line);
        card.CustomMinimumSize = new Vector2(0, 38);
        card.Pressed += () => { _chosen = t; Refresh(); };

        var row = BattleUiKit.Row(10);
        BattleUiKit.AddFilled(card, row);

        row.AddChild(Text(no.ToString(), BattleTheme.Muted, BattleTheme.FontLabel));
        row.AddChild(Text(t.Name, BattleTheme.Ink, BattleTheme.FontSmall));
        row.AddChild(ElementChip(t.MainType));
        row.AddChild(Spacer());
        row.AddChild(Text($"合計種族値 {t.TotalBst}", BattleTheme.Muted, BattleTheme.FontLabel));
        if (on) row.AddChild(Pill("選択中", BattleTheme.Brass, BattleTheme.BrassBg));
        return card;
    }
}
