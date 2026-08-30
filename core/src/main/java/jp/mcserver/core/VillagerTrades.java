package jp.mcserver.core;

import java.util.ArrayList;
import java.util.List;
import java.util.function.Function;

/**
 * 村人の取引テーブル（§3.2）。
 *
 * <p><b>村人はエンチャント本およびエンチャント済みの品を提供しない。</b>該当する取引枠は
 * 代替を置かずに削除する。司書は紙・本の買取とガラス・ランタン・時計・コンパス・名札の
 * 販売として、武器鍛冶・防具鍛冶・道具鍛冶は素材の買取とその他の販売として残る。
 *
 * <p>判定は「取引が渡す品」に対してのみ行う。支払い側にエンチャント品を要求する取引は
 * バニラに存在せず、仮にあっても供給経路ではない。
 */
public final class VillagerTrades {

    /** エンチャント本。中身が空でも品目そのものを禁じる。 */
    public static final String ENCHANTED_BOOK = "ENCHANTED_BOOK";

    private VillagerTrades() {
    }

    /**
     * 取引が渡す品。Bukkit の ItemStack をサーバー非依存の形に落としたもの。
     *
     * @param itemKey            品目（名前空間の有無・大小文字を問わない）
     * @param enchantments       品そのものに付いたエンチャントの数
     * @param storedEnchantments 本に収められたエンチャントの数
     */
    public record Offer(String itemKey, int enchantments, int storedEnchantments) {

        public Offer {
            if (itemKey == null || itemKey.isBlank()) {
                throw new IllegalArgumentException("品目が空です");
            }
            if (enchantments < 0 || storedEnchantments < 0) {
                throw new IllegalArgumentException("エンチャント数が負です");
            }
        }

        /** エンチャントの付かない品。 */
        public static Offer plain(String itemKey) {
            return new Offer(itemKey, 0, 0);
        }

        /** エンチャント済みの品（武器・防具・道具の完成品）。 */
        public static Offer enchanted(String itemKey, int enchantments) {
            return new Offer(itemKey, enchantments, 0);
        }

        /** エンチャント本。 */
        public static Offer book(int storedEnchantments) {
            return new Offer(ENCHANTED_BOOK, 0, storedEnchantments);
        }
    }

    /** その取引を取引テーブルから外すか。 */
    public static boolean blocked(Offer offer) {
        return isEnchantedBook(offer.itemKey())
                || offer.enchantments() > 0
                || offer.storedEnchantments() > 0;
    }

    /**
     * 取引の並びから、禁じた品を渡す枠を取り除く。順序と残った枠の同一性は保つ
     * （取引回数や割引の状態は枠そのものが持つため、作り直さない）。
     *
     * @param recipes 取引の並び
     * @param result  取引から「渡す品」を取り出す関数
     */
    public static <T> List<T> filter(List<T> recipes, Function<T, Offer> result) {
        List<T> kept = new ArrayList<>(recipes.size());
        for (T recipe : recipes) {
            if (!blocked(result.apply(recipe))) {
                kept.add(recipe);
            }
        }
        return kept;
    }

    /** 名前空間付き（minecraft:enchanted_book）でも通るように正規化して比べる。 */
    private static boolean isEnchantedBook(String itemKey) {
        String name = itemKey.substring(itemKey.indexOf(':') + 1);
        return name.equalsIgnoreCase(ENCHANTED_BOOK);
    }
}
