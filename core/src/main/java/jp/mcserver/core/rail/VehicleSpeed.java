package jp.mcserver.core.rail;

/**
 * 乗り物速度制御機能（`rail_infra_spec.md` F-05）。
 *
 * <p>実際に速度を変更する処理（{@code Minecart}/{@code Boat} の属性書き換え）は
 * Bukkit 依存のためプラグイン側が持つ。ここは規定値の置き場である。
 */
public final class VehicleSpeed {

    private VehicleSpeed() {
    }

    /** バニラのトロッコの最高速度（block/s）。引き上げ前の既定値。 */
    public static final double MINECART_DEFAULT_MAX_SPEED = 8.0;

    /** トロッコの最高速度の引き上げ後の値（block/s）。 */
    public static final double MINECART_MAX_SPEED = 13.0;

    /**
     * 氷上（氷・薄氷・氷塊・青氷）でのボートの最高速度（block/s）。減速補正であり、
     * バニラの通常時の最高速度と同値まで制限する。
     */
    public static final double BOAT_ON_ICE_MAX_SPEED = 8.0;

    /** ボートを減速させる対象ブロック（要件定義書の表記のまま）。 */
    public static final java.util.List<String> BOAT_SLOWING_ICE_BLOCKS =
            java.util.List.of("氷", "薄氷", "氷塊", "青氷");
}
