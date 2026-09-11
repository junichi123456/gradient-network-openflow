package jp.mcserver.core;

/**
 * オーダーブックの注文（§13）。
 *
 * @param id        注文の識別子
 * @param player    注文者
 * @param side      売買の別
 * @param itemKey   均質品の種別
 * @param quantity  残数量
 * @param unitPrice 指値（1個あたりの exp）
 * @param placedDay 発注日
 * @param sequence  同一価格内の時間優先を決める通し番号
 */
public record MarketOrder(long id, String player, MarketOrder.Side side, String itemKey,
                          int quantity, long unitPrice, int placedDay, long sequence) {

    public enum Side { BUY, SELL }

    public MarketOrder {
        if (quantity < 0) {
            throw new IllegalArgumentException("数量が負である: " + quantity);
        }
        if (unitPrice <= 0) {
            throw new IllegalArgumentException("指値が0以下である: " + unitPrice);
        }
    }

    public MarketOrder withQuantity(int newQuantity) {
        return new MarketOrder(id, player, side, itemKey, newQuantity, unitPrice, placedDay, sequence);
    }

    public boolean filled() {
        return quantity == 0;
    }

    /** 買い注文が発注時に預託する exp。 */
    public long deposit() {
        return side == Side.BUY ? (long) quantity * unitPrice : 0;
    }
}
