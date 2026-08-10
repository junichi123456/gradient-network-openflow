using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Battle;
using MysteryDungeon.Entities;
using MysteryDungeon.Species;
using static MysteryDungeon.UI.Battle.BattleUiKit;

namespace MysteryDungeon.UI.Battle;

// 選出画面。相手の6匹を見て、自分の6匹から4匹を選ぶ（50秒）。
//
// この画面の主役は「相手について分からないこと」。相手側のカードは技と
// 持ち物の欄を破線と ? で明示的に空けてある。情報が無いのではなく
// 意図的に隠されていると分かることが、読み合いの入口になる。
public partial class SelectionScreen : Control
{
    private BattleTeam _team;
    private IReadOnlyList<PublicEntryView> _foe;
    private BattleClock _clock;

    private readonly HashSet<BattleEntry> _picked = new();
    private VBoxContainer _foeList, _selfList;
    private Label _timer, _selfState, _foeState;
    private ProgressBar _gauge;

    public IReadOnlyList<BattleEntry> Picked => _picked.ToList();

    public void Initialize(BattleTeam team, IReadOnlyList<PublicEntryView> foe, BattleClock clock)
    {
        _team = team;
        _foe = foe;
        _clock = clock;
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

        root.AddChild(TopBar("選出", out var bar));
        _selfState = Pill("自分 0 / 4", BattleTheme.Muted, BattleTheme.Sunk);
        _foeState = Pill("相手 選択中…", BattleTheme.Muted, BattleTheme.Sunk);
        bar.AddChild(_selfState);
        bar.AddChild(_foeState);
        bar.AddChild(Spacer());

        _gauge = new ProgressBar
        {
            CustomMinimumSize = new Vector2(110, 5), ShowPercentage = false,
            MaxValue = BattleClock.SelectionLimitSeconds,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        bar.AddChild(_gauge);
        _timer = Text("0:50", BattleTheme.Ink, BattleTheme.FontTitle);
        bar.AddChild(_timer);

        var duo = Row(14);
        duo.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(duo);

        var l = Col(8); l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        l.AddChild(Text("相手のパーティ — 種族のみ開示", BattleTheme.Muted, BattleTheme.FontLabel));
        _foeList = Col(6);
        l.AddChild(_foeList);
        duo.AddChild(l);

        var r = Col(8); r.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        r.AddChild(Text("自分のパーティ — 4匹を選ぶ", BattleTheme.Muted, BattleTheme.FontLabel));
        _selfList = Col(6);
        r.AddChild(_selfList);
        duo.AddChild(r);
    }

    public void Refresh()
    {
        foreach (var c in _foeList.GetChildren()) c.QueueFree();
        for (int i = 0; i < _foe.Count; i++) _foeList.AddChild(FoeRow(i + 1, _foe[i]));

        foreach (var c in _selfList.GetChildren()) c.QueueFree();
        for (int i = 0; i < _team.Entries.Count; i++) _selfList.AddChild(SelfRow(i + 1, _team.Entries[i]));

        _selfState.Text = $"自分 {_picked.Count} / {BattleTeam.SelectionSize}";
        RefreshTimer();
    }

    public void RefreshTimer()
    {
        double remain = _clock.Selection.Remaining;
        _timer.Text = $"{(int)remain / 60}:{(int)remain % 60:D2}";
        _gauge.Value = remain;

        // 残り10秒を切ったら赤へ。帯の減りでも残量が読める。
        bool urgent = remain <= 10.0;
        _timer.AddThemeColorOverride("font_color", urgent ? BattleTheme.Crit : BattleTheme.Ink);
    }

    // 相手の1匹。技と持ち物は「?」で埋め、伏せられている事実を見せる。
    private Control FoeRow(int no, PublicEntryView v)
    {
        var card = Card(BattleTheme.Surface, BattleTheme.Line2);
        var row = Row(8);
        card.AddChild(row);

        var sp = SpeciesDatabase.Instance?.Get(v.SpeciesId);
        row.AddChild(Text(no.ToString(), BattleTheme.Muted, BattleTheme.FontLabel));
        row.AddChild(Text(sp?.DisplayName ?? v.SpeciesId, BattleTheme.Ink, BattleTheme.FontSmall));
        if (sp != null) foreach (var t in sp.Types) row.AddChild(ElementChip(t));
        row.AddChild(Spacer());
        row.AddChild(Text("技 ?", BattleTheme.Muted, BattleTheme.FontLabel));
        row.AddChild(Text("持ち物 ?", BattleTheme.Muted, BattleTheme.FontLabel));
        return card;
    }

    private Control SelfRow(int no, BattleEntry e)
    {
        bool on = _picked.Contains(e);
        var card = Card(on ? BattleTheme.BrassBg : BattleTheme.Surface,
                        on ? BattleTheme.Brass : BattleTheme.Line);
        var row = Row(8);
        card.AddChild(row);

        var sp = e.Species;
        row.AddChild(Text(no.ToString(), BattleTheme.Muted, BattleTheme.FontLabel));
        row.AddChild(Text(sp?.DisplayName ?? e.SpeciesId, BattleTheme.Ink, BattleTheme.FontSmall));
        if (sp != null) foreach (var t in sp.Types) row.AddChild(ElementChip(t));
        row.AddChild(Spacer());
        if (sp != null)
            row.AddChild(Text($"BST {sp.BaseHP + sp.BaseAtk + sp.BaseDef}",
                              BattleTheme.Muted, BattleTheme.FontLabel));
        if (on) row.AddChild(Pill("選出", BattleTheme.Brass, BattleTheme.BrassBg));
        return card;
    }

    // 4匹を超えて選べないようにする。上限に達したら以降は無視する。
    public bool Toggle(BattleEntry e)
    {
        if (_picked.Contains(e)) { _picked.Remove(e); Refresh(); return true; }
        if (_picked.Count >= BattleTeam.SelectionSize) return false;
        _picked.Add(e);
        Refresh();
        return true;
    }

    public void SetOpponentSubmitted(bool done)
    {
        _foeState.Text = done ? "相手 決定済み" : "相手 選択中…";
        _foeState.AddThemeColorOverride("font_color", done ? BattleTheme.Brass : BattleTheme.Muted);
    }
}
