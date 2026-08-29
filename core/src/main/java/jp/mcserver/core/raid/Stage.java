package jp.mcserver.core.raid;

/**
 * レイドの戦場（§12.6）。
 *
 * <p>個体の召喚位置の x, z を中心とした<b>半径 30 ブロックの円筒</b>である。
 * 高さは問わない。足場を組んで上から撃つことを封じるためではなく、
 * <b>戦場の外から一方的に削ることを封じる</b>のが目的だからである。
 *
 * <p>外から放たれた攻撃を通さないことで、遠距離から安全に削る組み立てを成立させない。
 * 削るには戦場に入る必要があり、入れば個体の攻撃も届く。
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

    /** 戦場の内側か。境界上は内側とみなす。 */
    public boolean contains(double x, double z) {
        return distanceFromCenter(x, z) <= radius;
    }

    /**
     * その位置から放たれた攻撃を受け付けるか。
     *
     * <p>判定するのは<b>攻撃が放たれた位置</b>である。矢であれば射手ではなく発射地点で見る。
     * 外から撃って内側へ踏み込む、という抜け道を残さないためである。
     */
    public boolean allowsAttackFrom(double x, double z) {
        return contains(x, z);
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
