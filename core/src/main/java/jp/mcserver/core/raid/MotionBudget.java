package jp.mcserver.core.raid;

/**
 * 通信量の見積り（§12.6）。
 *
 * <p>部位ごとに表示エンティティを持つため、更新は「部位数 × 更新頻度 × 観戦者数」で増える。
 * 実装前に上限を置き、設計段階で破綻を防ぐ。
 */
public final class MotionBudget {

    private MotionBudget() {}

    /** 1秒あたりの更新件数の上限。これを超える構成は採らない。 */
    public static final int MAX_UPDATES_PER_SECOND = 4_000;

    /** 1tickあたりの更新は行わない。既定の更新間隔（tick）。 */
    public static final int DEFAULT_UPDATE_INTERVAL_TICKS = 2;

    /**
     * 毎秒の更新件数。
     *
     * @param movingParts          実際に動く部位の数（静止部位は送らない）
     * @param updateIntervalTicks  更新間隔（tick）
     * @param viewers              受信するプレイヤー数
     */
    public static int updatesPerSecond(int movingParts, int updateIntervalTicks, int viewers) {
        if (movingParts < 0 || viewers < 0) {
            throw new IllegalArgumentException("負の値である");
        }
        if (updateIntervalTicks <= 0) {
            throw new IllegalArgumentException("更新間隔が0以下である");
        }
        int updatesPerTickSecond = 20 / updateIntervalTicks;
        return movingParts * updatesPerTickSecond * viewers;
    }

    /** 上限内に収まるか。 */
    public static boolean withinBudget(int movingParts, int updateIntervalTicks, int viewers) {
        return updatesPerSecond(movingParts, updateIntervalTicks, viewers) <= MAX_UPDATES_PER_SECOND;
    }

    /**
     * 上限に収めるために必要な最小の更新間隔（tick）。
     * 静止部位を送らない前提で計算する。
     */
    public static int requiredInterval(int movingParts, int viewers) {
        if (movingParts == 0 || viewers == 0) {
            return DEFAULT_UPDATE_INTERVAL_TICKS;
        }
        for (int interval = 1; interval <= 20; interval++) {
            if (withinBudget(movingParts, interval, viewers)) {
                return interval;
            }
        }
        return 20;
    }
}
