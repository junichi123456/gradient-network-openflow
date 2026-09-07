package jp.mcserver.core.raid;

/**
 * 特殊「串刺し」（`raid_species.md` §2、第二形態から。足元ギミック）。
 *
 * <p>狙ったプレイヤーの足元が発光し（20tick）、直後に発光地点の中心から
 * 刀身部分（ダイヤモンドの剣、長さ4ブロック）が3tickで一気に生え上がる。
 * 柄部分は地中に留まり、見えるのは刀身だけである。
 */
public final class GroundSpike {

    private GroundSpike() {
    }

    /** 発光の半径（ブロック）。 */
    public static final double TELEGRAPH_RADIUS = 1.2;

    /** 発光してから刃が生え始めるまでの時間（tick）。 */
    public static final int TELEGRAPH_TICKS = 20;

    /** 全長（ブロック）。柄も含めた長さで、実際に地上へ出るのはこの一部（刀身）である。 */
    public static final double SWORD_LENGTH = 4.0;

    /** 生え上がって全長へ到達するまでの時間（tick）。 */
    public static final int RISE_TICKS = 3;

    public static final double DAMAGE = 20.0;

    public static final double KNOCKBACK_BLOCKS = 10.0;

    public static final int WAVE_INTERVAL_TICKS = 10;

    public static final int WAVE_SIZE = 3;

    /** 参加人数から生成する本数の合計。1人では発生しない（式の性質上0以下になるため）。 */
    public static int totalCount(int participants) {
        return Math.max(0, participants * 2 - 2);
    }

    /**
     * 発光を始めてからの経過 tick における、生えた刀身の高さ（ブロック）。
     * 発光中（{@link #TELEGRAPH_TICKS} まで）は0。そこから {@link #RISE_TICKS} かけて全長へ達する。
     */
    public static double risenHeight(int ticksSinceTelegraph) {
        int sinceRiseStart = ticksSinceTelegraph - TELEGRAPH_TICKS;
        if (sinceRiseStart <= 0) {
            return 0;
        }
        double progress = Math.min(1.0, (double) sinceRiseStart / RISE_TICKS);
        return SWORD_LENGTH * progress;
    }

    /** 刀身が生え終わっているか（この tick 以降は当たり判定の高さが一定になる）。 */
    public static boolean fullyRisen(int ticksSinceTelegraph) {
        return ticksSinceTelegraph - TELEGRAPH_TICKS >= RISE_TICKS;
    }
}
