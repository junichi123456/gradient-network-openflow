package jp.mcserver.core.raid;

/**
 * 第三形態で追加される「金のオノ」（`raid_species.md` §2）。特殊4種すべてに、それぞれの
 * 技の動き方はそのままに追加される共通の武器で、通常のオノと同じように命中した相手の盾を
 * 一時的に使えなくする。
 *
 * <p>降り注ぐ刃・串刺しでは、それぞれの技の動き方（自由落下・地面から生え上がる）のまま
 * このクラスのダメージだけを使う。空間斬撃・全域大旋回では、剣と同じ追尾直進の動きを
 * {@link #HOMING_SPEED_BLOCKS_PER_SECOND}（剣より遅い）で行う。
 */
public final class GoldenAxe {

    private GoldenAxe() {
    }

    public static final double DAMAGE = 10.0;

    /** 長さ（ブロック）。横幅は等比（表示側で長さに応じた一様倍率を掛ける）。 */
    public static final double LENGTH = 3.0;

    /** 空間斬撃・全域大旋回でのオノの移動速度（ブロック/秒）。剣より遅い。 */
    public static final double HOMING_SPEED_BLOCKS_PER_SECOND = 25.0;

    /**
     * 降り注ぐ刃・空間斬撃・串刺しに追加する本数（1本、技の発動ごとに1本だけ）。
     * 全域大旋回は本数そのものの式が変わるため、ここではなく {@link GrandWhirl} が持つ。
     */
    public static final int COUNT_PER_BURST = 1;

    /**
     * 通常のオノと同じように、命中した相手の盾を一時的に使えなくする時間（tick）。
     * バニラのオノと同じ100tick（5秒）とした（実機未確認）。
     */
    public static final int SHIELD_DISABLE_TICKS = 100;

    public static double homingBlocksPerTick() {
        return HomingDart.blocksPerTick(HOMING_SPEED_BLOCKS_PER_SECOND);
    }
}
