using Godot;

namespace MysteryDungeon.UI.Battle;

// 対戦UIの配色トークン。設計案で確定した値をここ1箇所に置く。
//
// 原則: 色を持つものは「意味を持つもの」だけ。面と枠は無彩色に寄せ、
// 彩度を持つのは陣営(青/赤)と属性9色に限る。盤面の情報量が多いので、
// 色が散ると視線が迷う。
//
// 属性9色は move_editor / learnset ビューアと同じ値。3つのツールで
// 見た目が揃うよう、ここでも同じ16進を使っている。
public static class BattleTheme
{
    // ── 地と面 ──
    public static readonly Color Ground = new("12151a");
    public static readonly Color Surface = new("1b2029");
    public static readonly Color Raised = new("232a35");
    public static readonly Color Sunk = new("151920");

    // ── 文字 ──
    public static readonly Color Ink = new("e8ecf2");
    public static readonly Color Ink2 = new("b0bac7");
    public static readonly Color Muted = new("7f8b9a");

    // ── 枠 ──
    public static readonly Color Line = new("2b333f");
    public static readonly Color Line2 = new("3b4552");

    // ── アクセント(操作できるもの) ──
    public static readonly Color Brass = new("d9a441");
    public static readonly Color BrassBg = new("2f2716");

    // ── 陣営。試合中ずっと変わらない identity なので他の意味色と分ける ──
    public static readonly Color Ally = new("4d9be6");
    public static readonly Color AllyBg = new("152736");
    public static readonly Color Foe = new("e0664c");
    public static readonly Color FoeBg = new("2f1a15");

    // ── 状態。陣営色とは別系統 ──
    public static readonly Color Ok = new("5fbf7a");
    public static readonly Color Warn = new("e0a83c");
    public static readonly Color Crit = new("e8604c");

    // ── 属性9色 ──
    public static Color Element(string type) => type switch
    {
        "Neutral"  => new Color("a3abb4"),
        "Fire"     => new Color("e8734b"),
        "Water"    => new Color("5aa6e0"),
        "Grass"    => new Color("6cbf68"),
        "Electric" => new Color("d8b53a"),
        "Ground"   => new Color("c09a63"),
        "Ice"      => new Color("5fc0d4"),
        "Dragon"   => new Color("a084e8"),
        "Dark"     => new Color("9b87b5"),
        _          => new Color("a3abb4"),
    };

    public static string ElementLabel(string type) => type switch
    {
        "Neutral" => "無", "Fire" => "炎", "Water" => "水", "Grass" => "草",
        "Electric" => "電", "Ground" => "地", "Ice" => "氷", "Dragon" => "竜",
        "Dark" => "闇", _ => "無",
    };

    // SpeciesData.Types は Element enum なので、そのまま渡せる口も用意する。
    public static Color Element(Combat.Element e) => Element(e.ToString());
    public static string ElementLabel(Combat.Element e) => ElementLabel(e.ToString());

    public static Color Faction(Entities.Faction f) =>
        f == Entities.Faction.Player ? Ally : Foe;

    public static Color FactionBg(Entities.Faction f) =>
        f == Entities.Faction.Player ? AllyBg : FoeBg;

    // HP残量で色が変わる。数値を読まなくても危険域が分かるようにする。
    public static Color HpColor(float ratio) =>
        ratio <= 0.25f ? Crit : ratio <= 0.55f ? Warn : Ok;

    // ── 字送り ──
    public const int FontBody = 14;
    public const int FontSmall = 12;
    public const int FontLabel = 11;
    public const int FontTitle = 17;

    // 面を作るヘルパ。角丸と枠を毎回書かずに済むようにする。
    public static StyleBoxFlat Panel(Color bg, Color border, int radius = 5, int borderWidth = 1)
    {
        var sb = new StyleBoxFlat { BgColor = bg };
        sb.SetCornerRadiusAll(radius);
        sb.SetBorderWidthAll(borderWidth);
        sb.BorderColor = border;
        sb.SetContentMarginAll(0);
        return sb;
    }
}
