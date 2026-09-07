package jp.mcserver.core.raid;

import java.util.List;

/**
 * 特殊「全域大旋回」（`raid_species.md` §2）。
 *
 * <p>段階移行（体力3分の2・3分の1を切った瞬間）に1回ずつ、計2回だけ発動する
 * 移行専用のモーション。空間斬撃と同じ弧の幾何（{@link ArcSweep}）を使うが、
 * 戦場全域を高速で飛び回るぶん距離・本数・素材が異なる。
 */
public final class GrandWhirl {

    private GrandWhirl() {
    }

    /**
     * 出現高度の範囲。個体の召喚位置の足元Yからの相対値（ブロック）。この範囲から
     * 個体ごとにばらけて出現する。
     *
     * <p><b>絶対座標のYではなく、召喚位置基準。</b>足元Y=1の会場を基準に決めた値
     * （Y=10〜15、空間斬撃と同じ考え方）をオフセットへ直した（10−1=9、15−1=14）。
     * 実際の高度は {@code 召喚位置の足元Y + この値}。
     */
    public static final double SPAWN_Y_MIN_OFFSET = 9.0;

    public static final double SPAWN_Y_MAX_OFFSET = 14.0;

    /** 到達する高度。同じく召喚位置の足元Yからの相対値（-1-1=-2）。 */
    public static final double END_Y_OFFSET = -2.0;

    public static final double SWORD_LENGTH = 3.0;

    public static final double SPEED_BLOCKS_PER_SECOND = 25.0;

    public static final double DISTANCE_BLOCKS = 55.0;

    /** 召喚してから旋回を始めるまでの待機（tick）。 */
    public static final int START_DELAY_TICKS = 10;

    /** 旋回の半径（戦場の中心から召喚位置までの距離、ブロック）。**仮の値。** */
    public static final double ARC_RADIUS = 20.0;

    /**
     * ノックバック（ブロック）。**仮の値。** ダメージ以外は指定が無いため、同じ弧の幾何を
     * 持つ空間斬撃（5.0）をそのまま流用する。
     */
    public static final double KNOCKBACK_BLOCKS = SpatialSlash.KNOCKBACK_BLOCKS;

    /**
     * 素材とダメージの並び。生成した順に、この5つを繰り返し割り当てる
     * （金14・銅15・鉄16・ダイヤモンド17・ネザライト18）。
     */
    public enum Blade {
        GOLD(14.0),
        COPPER(15.0),
        IRON(16.0),
        DIAMOND(17.0),
        NETHERITE(18.0);

        private final double damage;

        Blade(double damage) {
            this.damage = damage;
        }

        public double damage() {
            return damage;
        }
    }

    private static final List<Blade> SEQUENCE = List.of(Blade.values());

    /** 一斉に生成する本数。参加人数の4倍。待機・波はなく、一度にすべて出す。 */
    public static int totalCount(int participants) {
        return participants * 4;
    }

    /** 生成した順（0始まり）から、割り当てる素材とダメージ。 */
    public static Blade bladeAt(int spawnIndex) {
        return SEQUENCE.get(spawnIndex % SEQUENCE.size());
    }

    public static int durationTicks() {
        return ArcSweep.durationTicks(DISTANCE_BLOCKS, SPEED_BLOCKS_PER_SECOND);
    }

    public static double sweepRadians(double radius) {
        return ArcSweep.sweepRadians(radius, DISTANCE_BLOCKS);
    }
}
