package jp.mcserver.core.rail;

/**
 * 地下鉄インフラのレール種別（`rail_infra_spec.md` F-01）。
 */
public enum RailType {

    RAIL(50),
    POWERED_RAIL(150),
    DETECTOR_RAIL(80),
    ACTIVATOR_RAIL(90);

    private final long basePrice;

    RailType(long basePrice) {
        this.basePrice = basePrice;
    }

    /** 基本単価（設置1個あたり）。 */
    public long basePrice() {
        return basePrice;
    }
}
