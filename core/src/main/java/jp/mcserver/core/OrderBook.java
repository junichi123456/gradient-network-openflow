package jp.mcserver.core;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;

/**
 * オーダーブック型の板（§13）。均質品を扱う。
 *
 * <p>約定は<b>価格優先・時間優先</b>で行い、価格は板に先にあった注文（指値）で決まる。
 * 買い手の指値が約定価格より高い場合、その差額は買い手へ返る。
 */
public final class OrderBook {

    private final String itemKey;
    private final List<MarketOrder> bids = new ArrayList<>();
    private final List<MarketOrder> asks = new ArrayList<>();
    private long sequence;
    private long nextId = 1;

    public OrderBook(String itemKey) {
        this.itemKey = itemKey;
    }

    /** 発注の結果。 */
    public record Result(List<Trade> trades, Optional<MarketOrder> resting, long deposit) {

        public int filledQuantity() {
            int total = 0;
            for (Trade t : trades) {
                total += t.quantity();
            }
            return total;
        }

        /** 買い手に返る額の合計（価格改善による差額）。 */
        public long totalRefund() {
            long total = 0;
            for (Trade t : trades) {
                total += t.buyerRefund();
            }
            return total;
        }
    }

    /** 発注。約定しなかった残数量は板に載る。 */
    public Result place(String player, MarketOrder.Side side, int quantity, long unitPrice,
                        int today) {
        if (quantity <= 0) {
            throw new IllegalArgumentException("数量が0以下である: " + quantity);
        }
        MarketOrder incoming = new MarketOrder(nextId++, player, side, itemKey, quantity,
                unitPrice, today, sequence++);
        long deposit = incoming.deposit();

        List<Trade> trades = new ArrayList<>();
        List<MarketOrder> opposite = side == MarketOrder.Side.BUY ? asks : bids;
        sortBook();

        while (incoming.quantity() > 0 && !opposite.isEmpty()) {
            MarketOrder best = opposite.get(0);
            boolean crosses = side == MarketOrder.Side.BUY
                    ? incoming.unitPrice() >= best.unitPrice()
                    : incoming.unitPrice() <= best.unitPrice();
            if (!crosses) {
                break;
            }
            int filled = Math.min(incoming.quantity(), best.quantity());
            long price = best.unitPrice(); // 板にあった側の指値で約定する
            String buyer = side == MarketOrder.Side.BUY ? incoming.player() : best.player();
            String seller = side == MarketOrder.Side.BUY ? best.player() : incoming.player();
            long buyerLimit = side == MarketOrder.Side.BUY ? incoming.unitPrice() : best.unitPrice();
            trades.add(Trade.of(buyer, seller, itemKey, filled, price, buyerLimit));

            incoming = incoming.withQuantity(incoming.quantity() - filled);
            MarketOrder remainder = best.withQuantity(best.quantity() - filled);
            if (remainder.filled()) {
                opposite.remove(0);
            } else {
                opposite.set(0, remainder);
            }
        }

        Optional<MarketOrder> resting = Optional.empty();
        if (incoming.quantity() > 0) {
            (side == MarketOrder.Side.BUY ? bids : asks).add(incoming);
            sortBook();
            resting = Optional.of(incoming);
        }
        return new Result(List.copyOf(trades), resting, deposit);
    }

    /** 取消。預託の返却額を返す。 */
    public long cancel(long orderId) {
        for (List<MarketOrder> book : List.of(bids, asks)) {
            for (int i = 0; i < book.size(); i++) {
                if (book.get(i).id() == orderId) {
                    MarketOrder removed = book.remove(i);
                    return removed.deposit();
                }
            }
        }
        return 0;
    }

    /** 期限切れの一括処理（§13）。失効した注文を板から外して返す。 */
    public List<MarketOrder> expire(int today) {
        List<MarketOrder> expired = new ArrayList<>();
        for (List<MarketOrder> book : List.of(bids, asks)) {
            book.removeIf(order -> {
                if (Market.expired(order.placedDay(), today)) {
                    expired.add(order);
                    return true;
                }
                return false;
            });
        }
        return expired;
    }

    /** 最良買い気配。 */
    public Optional<MarketOrder> bestBid() {
        sortBook();
        return bids.isEmpty() ? Optional.empty() : Optional.of(bids.get(0));
    }

    /** 最良売り気配。 */
    public Optional<MarketOrder> bestAsk() {
        sortBook();
        return asks.isEmpty() ? Optional.empty() : Optional.of(asks.get(0));
    }

    /** 価格ごとの数量を集計した板情報。 */
    public Map<Long, Integer> depth(MarketOrder.Side side) {
        sortBook();
        Map<Long, Integer> result = new LinkedHashMap<>();
        for (MarketOrder o : side == MarketOrder.Side.BUY ? bids : asks) {
            result.merge(o.unitPrice(), o.quantity(), Integer::sum);
        }
        return result;
    }

    public int openOrders() {
        return bids.size() + asks.size();
    }

    private void sortBook() {
        bids.sort(Comparator.comparingLong(MarketOrder::unitPrice).reversed()
                .thenComparingLong(MarketOrder::sequence));
        asks.sort(Comparator.comparingLong(MarketOrder::unitPrice)
                .thenComparingLong(MarketOrder::sequence));
    }
}
