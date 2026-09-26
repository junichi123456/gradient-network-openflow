package jp.mcserver.core;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * フリマ型の出品（§13）。個体差のある品を1点ずつ固定価格で扱う。
 *
 * <p>エンチャント品のように個体が同一でないものは板取引にできないため、
 * オーダーブックと併用する。
 */
public final class FleaMarket {

    /**
     * 出品。
     *
     * @param nationName 統一を達成した国家の名。出品者欄に常時表示する（§8.3, §13）。無ければ null
     */
    public record Listing(long id, String seller, String itemLabel, long price, int placedDay,
                          String nationName) {

        public Listing {
            if (price <= 0) {
                throw new IllegalArgumentException("価格が0以下である: " + price);
            }
        }

        /** 出品者欄の表示。 */
        public String sellerDisplay() {
            return nationName == null ? seller : "[" + nationName + "] " + seller;
        }
    }

    /** 購入の結果。 */
    public record Purchase(boolean ok, Trade trade, String message) {}

    private final Map<Long, Listing> listings = new LinkedHashMap<>();
    private long nextId = 1;

    public Listing list(String seller, String itemLabel, long price, int today, String nationName) {
        Listing listing = new Listing(nextId++, seller, itemLabel, price, today, nationName);
        listings.put(listing.id(), listing);
        return listing;
    }

    /** 購入。手数料は売却額から差し引き、世界政府へ渡る（§13）。 */
    public Purchase buy(long listingId, String buyer, long paid, int today) {
        Listing listing = listings.get(listingId);
        if (listing == null) {
            return new Purchase(false, null, "この出品は存在しません");
        }
        if (Market.expired(listing.placedDay(), today)) {
            listings.remove(listingId);
            return new Purchase(false, null, "この出品は期限切れです");
        }
        if (paid < listing.price()) {
            return new Purchase(false, null,
                    "支払いが不足しています（" + paid + " / " + listing.price() + "）");
        }
        listings.remove(listingId);
        Trade trade = Trade.of(buyer, listing.seller(), listing.itemLabel(), 1,
                listing.price(), paid);
        return new Purchase(true, trade, "購入しました。Hub内の受取窓口で受領してください");
    }

    /** 期限切れの一括処理。預託アイテムは自動返却される（§13）。 */
    public List<Listing> expire(int today) {
        List<Listing> expired = new ArrayList<>();
        listings.values().removeIf(listing -> {
            if (Market.expired(listing.placedDay(), today)) {
                expired.add(listing);
                return true;
            }
            return false;
        });
        return expired;
    }

    public int openListings() {
        return listings.size();
    }
}
