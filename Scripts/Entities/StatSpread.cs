using System;
using System.Collections.Generic;
using MysteryDungeon.Species;

namespace MysteryDungeon.Entities;

// 気質で上下させる能力。HP は含めない（HP に気質は掛からない）。
// JSON・通信では名前の文字列（Atk/Def/SpAtk/SpDef/Speed/None）で運ぶ。
public enum NatureStat { None, Atk, Def, SpAtk, SpDef, Speed }

// 構築で個体ごとに決める「突破値」と「気質」。
//
// 実数値 = 気質後(素の値) + 突破値（気質の後に足す）。1ポイント = 実数値+1。
// 素の値(Lv50) = 種族値 + 5。気質は整数の *11/10・*9/10（切り捨て）。
// 浮動小数の 0.9f は 50*0.9=44 になってしまうので使わない。
public sealed class StatSpread
{
    public const int PerStatMax = 25;
    public const int TotalMax = 55;

    // 自動配分で、種族値 S がこれ以上なら俊敏に振る（100 の峰の側）。
    public const int AutoSpeedThreshold = 88;

    public int Hp, Atk, Def, SpAtk, SpDef, Speed;           // 突破値
    public NatureStat NatureUp = NatureStat.None, NatureDown = NatureStat.None;

    public int Total => Hp + Atk + Def + SpAtk + SpDef + Speed;

    public StatSpread Clone() => (StatSpread)MemberwiseClone();

    public static StatSpread Zero() => new();

    // 違反ごとに1行の日本語メッセージ。空なら合法。
    public List<string> Validate()
    {
        var errors = new List<string>();
        void Check(string name, int v)
        {
            if (v < 0 || v > PerStatMax) errors.Add($"突破値 {name} は0〜{PerStatMax}（現在{v}）");
        }
        Check("HP", Hp); Check("攻撃", Atk); Check("防御", Def);
        Check("魔力", SpAtk); Check("精神", SpDef); Check("俊敏", Speed);

        if (Total > TotalMax)
            errors.Add($"突破値の合計は{TotalMax}以下（現在{Total}）");

        bool upNone = NatureUp == NatureStat.None, downNone = NatureDown == NatureStat.None;
        if (upNone != downNone)
            errors.Add("気質は上昇と下降を両方決めるか、両方なし（無補正）にする");
        else if (!upNone && NatureUp == NatureDown)
            errors.Add("気質の上昇と下降に同じ能力は選べない");
        return errors;
    }

    // HP 以外の実数値。素 = base*2*level/100+5、気質 = 素*mul10/10（切り捨て）、最後に突破値。
    public static int RealStat(int baseStat, int level, int bt, int natureMul10)
    {
        int raw = baseStat * 2 * level / 100 + 5;
        return raw * natureMul10 / 10 + bt;
    }

    public static string StatName(NatureStat s) => s switch
    {
        NatureStat.Atk => "攻撃", NatureStat.Def => "防御", NatureStat.SpAtk => "魔力",
        NatureStat.SpDef => "精神", NatureStat.Speed => "俊敏", _ => "なし",
    };

    public string NatureLabel =>
        NatureUp == NatureStat.None || NatureDown == NatureStat.None || NatureUp == NatureDown
            ? "無補正" : $"{StatName(NatureUp)}↑ {StatName(NatureDown)}↓";

    // 通信・JSON 用。未知の文字列・空は None。
    public static NatureStat ParseNature(string s) =>
        Enum.TryParse<NatureStat>(s, out var v) ? v : NatureStat.None;

    // 役割に応じた決定的な自動配分。乱数を使わない。合計は常に 55。
    //  1. 主攻撃 P: 物理攻撃=攻撃、特殊攻撃=魔力、両刀攻撃=種族値の大きい側。
    //  2. 気質: 物理攻撃 ↑攻撃↓魔力、特殊攻撃 ↑魔力↓攻撃、両刀攻撃は無補正。
    //  3. P に25。種族値 S>=88 なら俊敏に25、そうでなければ HP に25。残り5を耐久区分の能力へ。
    public static StatSpread Auto(SpeciesData sp)
    {
        var r = new StatSpread();
        if (sp == null) return r;

        bool physical = sp.Offense switch
        {
            "Physical" => true,
            "Special" => false,
            _ => sp.BaseAtk >= sp.BaseSpAtk,     // Mixed（と未設定）は種族値の大きい側
        };
        if (physical) r.Atk = PerStatMax; else r.SpAtk = PerStatMax;

        if (sp.Offense == "Physical") { r.NatureUp = NatureStat.Atk; r.NatureDown = NatureStat.SpAtk; }
        else if (sp.Offense == "Special") { r.NatureUp = NatureStat.SpAtk; r.NatureDown = NatureStat.Atk; }

        if (sp.BaseSpeed >= AutoSpeedThreshold) r.Speed = PerStatMax; else r.Hp = PerStatMax;

        int rest = TotalMax - PerStatMax * 2;    // 5
        bool defPhysical = sp.Defense switch
        {
            "Physical" => true,
            "Special" => false,
            _ => sp.BaseDef <= sp.BaseSpDef,     // Both（と未設定）
        };
        if (defPhysical) r.Def = rest; else r.SpDef = rest;
        return r;
    }
}
