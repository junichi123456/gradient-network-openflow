using MysteryDungeon.Entities;

namespace MysteryDungeon.Combat;

// 両刀攻撃の調査用「2つめの特性」(Stats.Trait2)のうち、威力・蓄積に関わる3種
// (ワイドバースト・こわざじょうず・しみこみ)の条件判定。
// 地形特性3種は FloorController.ApplyFieldOnEntry が扱う。
//
// 乱数を使わない純粋な判定にしてあるのは PowerTraitWeather と同じ理由
// (戻り値を回帰スイートで直接確かめるため)。仕様書:
// docs/design/drafts/2026-10-28-mixed-traits.md §3-2。
public static class SecondaryTraits
{
    public const string WideBurst = "wide_burst";
    public const string KowazaJouzu = "kowaza_jouzu";
    public const string Shimikomi = "shimikomi";

    public const float WideBurstBonus = 10f;
    public const float KowazaBonus = 20f;
    public const int KowazaPowerCap = 50;

    // ワイドバースト: 射程が Room で威力>0 の技に +10(物理・特殊を問わない)。
    // こわざじょうず: 特殊技で 0 < 威力 <= 50 に +20。判定は技データの威力
    // (move.Power)で、他の加算を足す前の値(パープルヘイズの前例と同じ)。
    public static float FlatPowerBonus(Entity attacker, MoveData move)
    {
        if (attacker == null || move == null) return 0f;
        float bonus = 0f;
        if (attacker.Stats.HasTrait(WideBurst)
            && move.Range == MoveRange.Room && move.Power > 0)
            bonus += WideBurstBonus;
        if (attacker.Stats.HasTrait(KowazaJouzu)
            && move.Category == MoveCategory.Special
            && move.Power > 0 && move.Power <= KowazaPowerCap)
            bonus += KowazaBonus;
        return bonus;
    }

    // しみこみ: 自分の技が相手に与える蓄積(AccumulateOnHit)を2倍にする。
    // 自分自身に入る蓄積・特性の固定加算(AccumulateFlat)は対象外。
    public static int AccumulationMultiplier(Entity attacker, Entity target)
    {
        if (attacker == null || target == null || target == attacker) return 1;
        return attacker.Stats.HasTrait(Shimikomi) ? 2 : 1;
    }
}
