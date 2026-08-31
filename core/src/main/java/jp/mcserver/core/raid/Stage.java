package jp.mcserver.core.raid;

/**
 * レイドの戦場（§12.6）。
 *
 * <p>個体の召喚位置の x, z を中心とした<b>半径 30 ブロックの円筒</b>である。
 * 高さは問わない。個体の行動範囲であり、回旋突進が走る円でもある。
 *
 * <p>戦場は<b>個体の行動範囲</b>を定める。外へ出たら中心を一度経由して戻る。
 * 遠距離から一方的に削ることを封じるのは戦場ではなく、
 * <b>個体からの距離</b>で見る（{@link KnightDefinition#ATTACK_RANGE_BLOCKS}）。
 */
public final class Stage {

    /** 既定の半径（ブロック）。 */
    public static final double DEFAULT_RADIUS = 30.0;

    /** 中心を経由したとみなす距離（ブロック）。 */
    public static final double CENTER_TOLERANCE = 2.0;

    private final double centerX;
    private final double centerZ;
    private final double radius;

    public Stage(double centerX, double centerZ, double radius) {
        if (radius <= 0) {
            throw new IllegalArgumentException("半径が0以下である: " + radius);
        }
        this.centerX = centerX;
        this.centerZ = centerZ;
        this.radius = radius;
    }

    /** 既定の半径の戦場。 */
    public Stage(double centerX, double centerZ) {
        this(centerX, centerZ, DEFAULT_RADIUS);
    }

    public double centerX() {
        return centerX;
    }

    public double centerZ() {
        return centerZ;
    }

    public double radius() {
        return radius;
    }

    /** 中心からの水平距離。 */
    public double distanceFromCenter(double x, double z) {
        double dx = x - centerX;
        double dz = z - centerZ;
        return Math.sqrt(dx * dx + dz * dz);
    }

    /**
     * 戦場の内側か。境界上は内側とみなす。
     *
     * <p>個体は境界の円周そのものを走ることがある（回旋突進）。計算誤差で
     * 外側と判定されないよう、わずかな余裕を持たせている。
     */
    public boolean contains(double x, double z) {
        return distanceFromCenter(x, z) <= radius + 1e-6;
    }

    /** 中心に到達したとみなせるか。 */
    public boolean atCenter(double x, double z) {
        return distanceFromCenter(x, z) <= CENTER_TOLERANCE;
    }

    /**
     * 中心経由の義務（§12.6）。
     *
     * <p>個体は突進で戦場の外へ出ることがある。外へ出たら、次に暴れる前に
     * <b>一度中心へ戻る</b>。瞬間移動ではなく歩いて戻るため、その間は無防備になる。
     * 突進を釣って避けることに、位置を戻させるという意味が生まれる。
     */
    public static final class CenterVisit {

        private boolean owed;
        private int exits;

        /**
         * 現在位置を報告し、義務を更新する。
         *
         * @param stage 戦場
         * @param x     個体の x
         * @param z     個体の z
         */
        public void observe(Stage stage, double x, double z) {
            if (!stage.contains(x, z)) {
                if (!owed) {
                    exits++;
                }
                owed = true;
                return;
            }
            if (owed && stage.atCenter(x, z)) {
                owed = false;
            }
        }

        /** 中心へ戻る義務があるか。 */
        public boolean owed() {
            return owed;
        }

        /** これまでに戦場の外へ出た回数。 */
        public int exits() {
            return exits;
        }

        /** やり直す。段階の移行などで用いる。 */
        public void clear() {
            owed = false;
        }
    }
}
