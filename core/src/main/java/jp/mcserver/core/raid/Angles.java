package jp.mcserver.core.raid;

/**
 * 角度の規約（§12.6）。
 *
 * <p><b>個体の正面から見て真右を 0 度、正面を 90 度</b>とする。したがって角度は
 * 正面を向いて反時計回りに増える。左は 180 度、真後ろは 270 度である。
 */
public final class Angles {

    private Angles() {}

    /** 真右。 */
    public static final double RIGHT = 0;

    /** 正面。 */
    public static final double FRONT = 90;

    /** 左。 */
    public static final double LEFT = 180;

    /** 真後ろ。 */
    public static final double BACK = 270;

    /**
     * 正面からの右向きのずれ（度）に変換する。回転値を組む際に用いる。
     *
     * <p>正面 90 度 → 0、真右 0 度 → +90、左 180 度 → −90。
     */
    public static double rightOffsetFromFront(double conventionDegrees) {
        return FRONT - conventionDegrees;
    }

    /** 2つの角度のあいだを振る量（度）。符号は向きを表す。 */
    public static double sweep(double fromDegrees, double toDegrees) {
        return toDegrees - fromDegrees;
    }

    /** 振り幅の絶対値（度）。 */
    public static double sweepMagnitude(double fromDegrees, double toDegrees) {
        return Math.abs(sweep(fromDegrees, toDegrees));
    }
}
