package jp.mcserver.core.raid;

/**
 * 特殊「空間斬撃」（`raid_species.md` §2、第二形態から）。
 *
 * <p><b>虚刃の衛士自身の頭上</b>（地表面から{@link #SPAWN_Y_OFFSET}ブロック）に浮遊剣
 * （銅の剣）が現れ、{@link #START_DELAY_TICKS}tick待機したあと、狙った相手へ向けて直進する
 * （{@link HomingDart}）。一次実装は狙った相手の位置に召喚していたが、相手の足場（洞窟の中・
 * 段差の上など）に引きずられて出現位置が安定しなかったため、個体自身の頭上という固定点に
 * 直した。
 *
 * <p>一次実装は戦場の中心を軸とした円弧を描く方式だったが、旋回を始めた時点の位置で軌道を
 * 固定してしまうため、動く相手にほとんど当たらなかった。修正後は、移動を始めてから
 * {@link #TRACKING_DURATION_TICKS}tickのあいだ、{@link #RETARGET_INTERVAL_TICKS}tickごとに
 * 狙いを相手の現在位置へ更新し続ける。<b>命中した瞬間（防がれたかに関わらず）その場で消える。</b>
 */
public final class SpatialSlash {

    private SpatialSlash() {
    }

    /**
     * 出現高度。個体の足元（地表面）からの相対値（ブロック）。虚刃の衛士自身の頭上に
     * 出現する——狙った相手の位置ではない（実機で確認して修正）。
     */
    public static final double SPAWN_Y_OFFSET = 4.0;

    /** 剣の長さ（ブロック）。 */
    public static final double SWORD_LENGTH = 3.0;

    /** 移動速度（ブロック / 秒）。実機で確認して25→30へ修正。 */
    public static final double SPEED_BLOCKS_PER_SECOND = 30.0;

    /** 総移動距離の上限（ブロック）。これを超えて飛び続けることはない。 */
    public static final double MAX_DISTANCE_BLOCKS = 100.0;

    /**
     * 召喚してから移動を始めるまでの待機（tick）。このあいだ、虚刃の衛士の頭上に留まる
     * （個体が動けば一緒に動く）。実機で確認して5→30へ修正。
     */
    public static final int START_DELAY_TICKS = 30;

    /**
     * 移動を始めてから、相手を追尾し続ける時間（tick）。この時間が尽きるか、
     * {@link #MAX_DISTANCE_BLOCKS} に達したら止まる（先に来たほうが効く）。
     */
    public static final int TRACKING_DURATION_TICKS = 80;

    /** 狙いを相手の現在位置へ更新し直す間隔（tick）。 */
    public static final int RETARGET_INTERVAL_TICKS = 10;

    public static final double DAMAGE = 15.0;

    public static final double KNOCKBACK_BLOCKS = 5.0;

    public static final int WAVE_INTERVAL_TICKS = 5;

    /** 1回の発生で生成する本数。実機で確認して3→5へ修正。 */
    public static final int WAVE_SIZE = 5;

    /** 参加人数から生成する本数の合計。実機で確認して 参加人数*2+3 → 参加人数*3 へ修正。 */
    public static int totalCount(int participants) {
        return participants * 3;
    }

    /** 1tickあたりの歩幅（ブロック）。 */
    public static double blocksPerTick() {
        return HomingDart.blocksPerTick(SPEED_BLOCKS_PER_SECOND);
    }
}
