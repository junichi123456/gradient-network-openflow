using Godot;
using MysteryDungeon.Combat;

namespace MysteryDungeon.UI.Battle;

// 4画面で使い回す小物。BattleHud が先にローカルで持っていたものを、
// 構築・選出・配置でも同じ見た目になるよう切り出した。
public static class BattleUiKit
{
    public static Label Text(string s, Color c, int size)
    {
        var l = new Label { Text = s };
        l.AddThemeColorOverride("font_color", c);
        l.AddThemeFontSizeOverride("font_size", size);
        return l;
    }

    public static Label Pill(string s, Color fg, Color bg)
    {
        var l = Text(s, fg, BattleTheme.FontLabel);
        var sb = BattleTheme.Panel(bg, bg, 99);
        sb.SetContentMarginAll(3);
        l.AddThemeStyleboxOverride("normal", sb);
        return l;
    }

    // 属性チップ。枠線が文字色と同じという設計案の形。
    public static Label ElementChip(string type)
    {
        var c = BattleTheme.Element(type);
        var l = Text(BattleTheme.ElementLabel(type), c, BattleTheme.FontLabel);
        var sb = BattleTheme.Panel(new Color(c, 0.10f), c, 2);
        sb.SetContentMarginAll(2);
        l.AddThemeStyleboxOverride("normal", sb);
        return l;
    }

    public static Label ElementChip(Element e) => ElementChip(e.ToString());

    // 分類チップ。物理=赤 / 特殊=青 / 変化=灰。属性色と混ざらないよう
    // 塗りつぶしにして、属性チップ（枠線）と形で区別する。
    public static Label CategoryChip(MoveCategory cat)
    {
        var (txt, bg) = cat switch
        {
            MoveCategory.Physical => ("物", BattleTheme.Foe),
            MoveCategory.Special => ("特", BattleTheme.Ally),
            _ => ("変", BattleTheme.Muted),
        };
        var l = Text(txt, new Color("ffffff"), 10);
        var sb = BattleTheme.Panel(bg, bg, 2);
        sb.SetContentMarginAll(2);
        l.AddThemeStyleboxOverride("normal", sb);
        return l;
    }

    // 押せるカード。Button を土台にするので、フォーカス枠とキーボード操作が
    // ただで付いてくる。見た目は Card と揃えたいので、4状態すべてに
    // stylebox を入れて Godot 既定のボタン外観を完全に上書きする。
    public static Button ClickableCard(Color bg, Color border, int radius = 5)
    {
        var b = new Button { Flat = false, ClipText = false };
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
        {
            var tint = state switch
            {
                "hover" => bg.Lerp(BattleTheme.Ink, 0.06f),
                "pressed" => bg.Lerp(BattleTheme.Ink, 0.12f),
                _ => bg,
            };
            var sb = BattleTheme.Panel(tint, state == "focus" ? BattleTheme.Brass : border, radius);
            sb.SetContentMarginAll(6);
            b.AddThemeStyleboxOverride(state, sb);
        }
        return b;
    }

    public static PanelContainer Card(Color bg, Color border, int radius = 5)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", BattleTheme.Panel(bg, border, radius));
        return p;
    }

    public static Control Spacer() =>
        new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

    public static HBoxContainer Row(int separation = 6)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", separation);
        return h;
    }

    public static VBoxContainer Col(int separation = 6)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", separation);
        return v;
    }

    // 画面上部の共通バー。4画面で同じ位置に同じ形で出す。
    public static PanelContainer TopBar(string title, out HBoxContainer row)
    {
        var bar = Card(BattleTheme.Raised, BattleTheme.Line, 0);
        row = Row(12);
        row.AddChild(Text(title, BattleTheme.Muted, BattleTheme.FontLabel));
        bar.AddChild(row);
        return bar;
    }

    // 規則の充足を出す行。満たしていれば緑のチェック、欠けていれば赤。
    public static HBoxContainer RuleChip(string label, bool pass)
    {
        var r = Row(5);
        r.AddChild(Text(pass ? "✓" : "✕", pass ? BattleTheme.Ok : BattleTheme.Crit, BattleTheme.FontSmall));
        r.AddChild(Text(label, pass ? BattleTheme.Ink2 : BattleTheme.Crit, BattleTheme.FontSmall));
        return r;
    }
}
