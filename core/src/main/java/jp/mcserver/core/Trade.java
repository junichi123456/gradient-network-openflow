package jp.mcserver.core;

/**
 * 約定（§13）。
 *
 * @param gross          約定代金（買い手が支払う額）
 * @param fee            世界政府へ渡る手数料
 * @param sellerProceeds 売り手の手取り
 * @param buyerRefund    指値と約定価格の差により買い手へ返る額
 */
public record Trade(String buyer, String seller, String itemKey, int quantity, long price,
                    long gross, long fee, long sellerProceeds, long buyerRefund) {

    static Trade of(String buyer, String seller, String itemKey, int quantity, long price,
                    long buyerLimitPrice) {
        long gross = (long) quantity * price;
        long fee = Market.fee(gross);
        long refund = (buyerLimitPrice - price) * quantity;
        return new Trade(buyer, seller, itemKey, quantity, price, gross, fee,
                gross - fee, Math.max(0, refund));
    }
}
