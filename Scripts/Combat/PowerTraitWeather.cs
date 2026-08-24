using MysteryDungeon.Dungeon;
using MysteryDungeon.Entities;

namespace MysteryDungeon.Combat;

// 「〇〇のちから」に付いた天候ぶんの効果。
//
// 本体（同属性の味方の技威力+10%）は党全体を数える処理なので AttackAction
// 側にあるが、天候で条件が開くこちら側は**保持者本人にだけ**掛かる素直な
// 条件判定なので、6種ぶんをここへ集めた。
//
// 集めた理由は検証のため。命中ランク・回避ランクは命中判定の乱数の奥にあり、
// 実際に撃たせて当たった回数で確かめようとすると試行回数ぶんブレる——
// 条件判定だけを関数として取り出せば、天候と特性を渡して戻り値を見るだけで
// 乱数なしに正確に確かめられる（BattleTestScene.VerifyPowerTraitWeather）。
//
// 天候効果が定義されていない3種（無・雷・竜のちから）はここに現れない。
// 本体の威力+10%だけを持つ、という現状のままで変えていない。
public static class PowerTraitWeather
{
    public const string Dark = "dark_power";
    public const string Fire = "fire_power";
    public const string Water = "water_power";
    public const string Grass = "grass_power";
    public const string Ground = "ground_power";
    public const string Ice = "ice_power";

    // 闇のちから: 天候がきりのとき、自分のターン終了時にHPを5%回復する。
    // 戻り値は最大HPに対する割合（0 = 回復しない）。
    public const float DarkFogHealRatio = 0.05f;

    public static float TurnEndHealRatio(Entity e, WeatherType weather) =>
        e != null && e.Stats.Trait == Dark && weather == WeatherType.Fog ? DarkFogHealRatio : 0f;

    // 炎のちから: 天候がはれのとき、**水属性の相手に対する**自身の炎属性の
    // わざの威力+10。相手の属性は実際の Type1/Type2 で見る（相性計算の都合で
    // 書き換わった型ではなく「水属性の相手」という素の条件のため）。
    public const float FireSunnyBonus = 10f;

    public static float FlatPowerBonus(Entity attacker, Entity target,
                                       string effectiveMoveType, WeatherType weather)
    {
        if (attacker == null || target == null) return 0f;
        if (weather != WeatherType.Sunny || attacker.Stats.Trait != Fire) return 0f;
        if (effectiveMoveType != "Fire") return 0f;
        bool targetIsWater = target.Stats.Type1 == "Water" || target.Stats.Type2 == "Water";
        return targetIsWater ? FireSunnyBonus : 0f;
    }

    // 水のちから: 天候があめのとき、自身の優先度+1。**技ではなく個体に付く**
    // ので、その個体が出す手すべて（移動を含む）が同じだけ速くなる。
    public static int PriorityBonus(Entity actor, WeatherType weather) =>
        actor != null && actor.Stats.Trait == Water && weather == WeatherType.Rain ? 1 : 0;

    // 草のちから: 天候がはれのとき、自身が使用する草技の命中ランク+1。
    // 属性は EffectiveMoveType を渡す（おしえで草に化けた無属性技も同じ扱い）。
    public static int AccuracyBonus(Entity attacker, string effectiveMoveType, WeatherType weather) =>
        attacker != null && attacker.Stats.Trait == Grass
        && weather == WeatherType.Sunny && effectiveMoveType == "Grass" ? 1 : 0;

    // 地のちから: 天候がすなあらしのとき、自身の回避ランク+1。
    // 氷のちから: 天候がゆきのとき、**対象範囲が部屋またはフロア全体の技に
    // 対してだけ**回避ランク+2（単体・直線・範囲には効かない）。
    // 両方を1つの加算枠にまとめて返すので、複数条件が同時に立っても
    // ランク表側の頭打ちがそのまま効く。
    public static int EvasionBonus(Entity target, MoveRange range, WeatherType weather)
    {
        if (target == null) return 0;
        int bonus = 0;
        if (weather == WeatherType.Sandstorm && target.Stats.Trait == Ground) bonus += 1;
        if (weather == WeatherType.Snow && target.Stats.Trait == Ice
            && range is MoveRange.Room or MoveRange.FullFloor) bonus += 2;
        return bonus;
    }
}
