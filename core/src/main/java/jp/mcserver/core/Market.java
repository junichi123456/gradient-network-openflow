package jp.mcserver.core;

/**
 * GUI市場の共通規定（§13）。
 *
 * <p>均質品はオーダーブック型（{@link OrderBook}）、個体差のある品はフリマ型
 * （{@link FleaMarket}）で扱う。通貨は exp。
 */
public final class Market {

    private Market() {}

    /** 取引手数料（%）。売却額から差し引き、世界政府へ（§13, §15）。 */
    public static final int FEE_PERCENT = 5;

    /** 出品・注文の有効期限（日）。 */
    public static final int EXPIRY_DAYS = 14;

    /** 手数料。端数は切り上げ、世界政府に寄せる。 */
    public static long fee(long gross) {
        if (gross < 0) {
            throw new IllegalArgumentException("約定代金が負である: " + gross);
        }
        return (gross * FEE_PERCENT + 99) / 100;
    }

    /** 出品者の手取り。 */
    public static long proceeds(long gross) {
        return gross - fee(gross);
    }

    /** 期限切れか（§13）。出品・注文とも14日で失効し、預託は自動返却される。 */
    public static boolean expired(int placedDay, int today) {
        return today - placedDay >= EXPIRY_DAYS;
    }

    /**
     * 自己取引（wash trading）で失われる額。
     *
     * <p>手数料が売却額から差し引かれるため、自分で買って自分で売るだけで
     * 5%を失う。価格操作は自動的に割に合わなくなる（§13）。
     */
    public static long washTradingLoss(long gross) {
        return fee(gross);
    }
}
