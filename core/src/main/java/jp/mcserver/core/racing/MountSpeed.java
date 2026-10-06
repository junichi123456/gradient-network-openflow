package jp.mcserver.core.racing;

/**
 * 記録した競走馬をゲーム内の馬として出すときの、移動速度属性（{@code movement_speed}）
 * への換算（`minecraft_server_spec.md` §27.4）。
 *
 * <p>出す場所によって2通りの換算を使い分ける。
 * <ul>
 *   <li><b>国家ワールド</b>（{@link #vanillaScaledAttribute}）— スピード0〜100を、バニラの
 *       馬の属性の範囲（{@value #VANILLA_MIN_ATTRIBUTE}〜{@value #VANILLA_MAX_ATTRIBUTE}）に
 *       縮めて当てはめる。挙動はバニラの馬と変わらず、能力値の差だけが体感できる
 *   <li><b>競馬専用次元</b>（{@link #specAttribute}）— 仕様書どおりの最高速度
 *       （{@link AbilityValue#baseSpeedMetersPerSecond}、スピード0で10m/s〜100で25m/s）を
 *       そのまま属性値に直す。バニラの馬の上限を大きく超える
 * </ul>
 */
public final class MountSpeed {

    private MountSpeed() {}

    /** バニラの馬が取りうる移動速度属性の下限。 */
    public static final double VANILLA_MIN_ATTRIBUTE = 0.1125;

    /** バニラの馬が取りうる移動速度属性の上限。 */
    public static final double VANILLA_MAX_ATTRIBUTE = 0.3375;

    /**
     * 移動速度属性1.0あたりの、地上での速さ（m/s）。プレイヤーの歩行（属性0.1）が
     * 約4.317m/sであることから逆算した近似値で、騎乗中の馬も同じ移動の式に従う。
     * バニラの馬の範囲はおよそ4.86〜14.57m/sになる。実機で要確認（§22）。
     */
    public static final double METERS_PER_SECOND_PER_ATTRIBUTE = 43.17;

    /**
     * 国家ワールド用。スピード0〜{@value AbilityValue#MAX_VALUE}をバニラの馬の範囲へ線形に
     * 縮める。{@value AbilityValue#MAX_VALUE}を超える分（特性による能力プラス補正）は
     * バニラの上限で頭打ちにする。
     */
    public static double vanillaScaledAttribute(double speed) {
        if (speed < 0) {
            throw new IllegalArgumentException("スピードが負である: " + speed);
        }
        double clamped = Math.min(speed, AbilityValue.MAX_VALUE);
        return VANILLA_MIN_ATTRIBUTE
                + (VANILLA_MAX_ATTRIBUTE - VANILLA_MIN_ATTRIBUTE) * clamped / AbilityValue.MAX_VALUE;
    }

    /** 競馬専用次元用。仕様書どおりの最高速度（m/s）を属性値に直す。 */
    public static double specAttribute(double speed) {
        return AbilityValue.baseSpeedMetersPerSecond(speed) / METERS_PER_SECOND_PER_ATTRIBUTE;
    }
}
