package jp.mcserver.core.raid;

/**
 * 特殊「空間斬撃」（`raid_species.md` §2、第二形態から）。
 *
 * <p>Y=6 から浮遊剣（銅の剣）が現れ、5tick待機したあと、戦場の中心を軸とした
 * 弧を描きながら Y=-1 へ向けて旋回する。軌道の幾何は {@link ArcSweep} を使う。
 */
public final class SpatialSlash {

    private SpatialSlash() {
    }

    /** 出現高度（Y）。 */
    public static final double SPAWN_Y = 6.0;

    /** 到達する高度（Y）。 */
    public static final double END_Y = -1.0;

    /** 剣の長さ（ブロック）。 */
    public static final double SWORD_LENGTH = 3.0;

    /** 旋回の速さ（ブロック / 秒）。 */
    public static final double SPEED_BLOCKS_PER_SECOND = 25.0;

    /** 水平方向の移動距離の基準（ブロック）。実際は ±1 ブロックの幅を持つ。 */
    public static final double DISTANCE_BLOCKS = 35.0;

    public static final double DISTANCE_JITTER = 1.0;

    /** 召喚してから旋回を始めるまでの待機（tick）。このあいだ軌道を通りうる相手を追尾する。 */
    public static final int START_DELAY_TICKS = 5;

    /**
     * 旋回の半径（戦場の中心から召喚位置までの距離、ブロック）。**仮の値。**
     *
     * <p>{@link ArcSweep} は召喚位置ごとの実際の中心距離を半径として使うため、この定数は
     * 「召喚位置をどれだけ中心から離すか」の目安として使う——半径が大きいほど弧は緩やかに、
     * 小さいほど渦を巻くように旋回する。
     */
    public static final double ARC_RADIUS = 12.0;

    public static final double DAMAGE = 15.0;

    public static final double KNOCKBACK_BLOCKS = 5.0;

    public static final int WAVE_INTERVAL_TICKS = 5;

    public static final int WAVE_SIZE = 3;

    public static int totalCount(int participants) {
        return participants * 2 + 3;
    }

    /** ±1ブロックの幅の中から、この回の移動距離を決める。 */
    public static double distanceFor(double jitter) {
        double clamped = Math.max(-DISTANCE_JITTER, Math.min(DISTANCE_JITTER, jitter));
        return DISTANCE_BLOCKS + clamped;
    }

    /** 移動距離から求まる旋回の尺（tick）。待機の5tickは含まない。 */
    public static int durationTicks(double distanceBlocks) {
        return ArcSweep.durationTicks(distanceBlocks, SPEED_BLOCKS_PER_SECOND);
    }

    public static double sweepRadians(double radius, double distanceBlocks) {
        return ArcSweep.sweepRadians(radius, distanceBlocks);
    }
}
