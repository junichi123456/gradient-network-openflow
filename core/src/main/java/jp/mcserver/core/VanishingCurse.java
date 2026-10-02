package jp.mcserver.core;

import java.util.Map;

/**
 * 『消滅の呪い』の全面付与（§3.1）。
 *
 * <p><b>手段を問わず、いかなるエンチャントが付与された時点で同時付与する。</b>
 * これにより装備は完全なフロー資産となり、蓄積が不可能になる。
 * エンチャント消費が通貨の最大のシンクとして機能する。
 *
 * <p><b>漏れが1経路でもあれば、そこが蓄積の抜け道になる。</b>判定はここに集約し、
 * 経路ごとの捕捉点（エンチャントテーブル・金床・ルートテーブル・釣り・拾得）は
 * どれもこの判定を通す。
 *
 * <p>付与しないもの
 * <ul>
 *   <li><b>エンチャント本</b>。本の付与は<b>要らない</b>（§3.1 の整理）。
 *       本は装備ではなく、道具へ移した時点で金床の経路が捕捉する</li>
 *   <li><b>エンチャントが増えていない金床の作業</b>。修理と命名は対象外である（§3.1）</li>
 * </ul>
 *
 * <p>砥石は『消滅の呪い』を除去できない（バニラの仕様）。呪いを外して装備を蓄積する
 * 経路は最初から存在しない。
 */
public final class VanishingCurse {

    private VanishingCurse() {
    }

    /** 付与するエンチャントの名前。描画側（Bukkit の {@code Enchantment}）で解決する。 */
    public static final String CURSE = "VANISHING_CURSE";

    /** 付与しない品目。 */
    public static final String ENCHANTED_BOOK = "ENCHANTED_BOOK";

    /**
     * その品に呪いを足すべきか。
     *
     * @param itemKey      品目の名前（{@code DIAMOND_SWORD} のような列挙名）
     * @param enchantments いま付いているエンチャント。名前 → 水準
     */
    public static boolean needs(String itemKey, Map<String, Integer> enchantments) {
        if (ENCHANTED_BOOK.equals(itemKey)) {
            return false;
        }
        if (enchantments.containsKey(CURSE)) {
            return false;
        }
        // 呪いだけが付いた品は無い（上で弾いている）ので、1つでもあれば対象である
        return !enchantments.isEmpty();
    }

    /**
     * エンチャントが増えたか。<b>金床の作業を切り分けるための判定である。</b>
     *
     * <p>新しい種類が付いた場合と、同じ種類の水準が上がった場合を「増えた」とする。
     * 修理・命名では増えないため、そのまま対象外になる（§3.1）。
     * 呪いそのものの増減は数えない。呪いを足したことが次の付与を招かないようにするためである。
     *
     * @param before 作業前のエンチャント
     * @param after  作業後のエンチャント
     */
    public static boolean gained(Map<String, Integer> before, Map<String, Integer> after) {
        for (Map.Entry<String, Integer> entry : after.entrySet()) {
            if (CURSE.equals(entry.getKey())) {
                continue;
            }
            int had = before.getOrDefault(entry.getKey(), 0);
            if (entry.getValue() > had) {
                return true;
            }
        }
        return false;
    }

    /**
     * 経路の一覧（§3.1 / `implementation_feasibility.md` の2節）。
     *
     * <p><b>捕捉点をここに並べておく。</b>1経路でも漏れれば抜け道になるため、
     * 実装と検証の両方でこの一覧を突き合わせる。
     */
    public enum Route {
        /** エンチャントテーブル。エンチャント確定の時点で結果へ足す */
        ENCHANTING_TABLE("エンチャントテーブル"),
        /** 金床（本＋道具）。<b>エンチャントが増えた場合のみ</b>。修理・命名は対象外 */
        ANVIL("金床"),
        /** 構造物チェスト。ルートテーブルの生成時 */
        LOOT_CHEST("構造物チェスト"),
        /** モブドロップ。死亡時の落とし物 */
        MOB_DROP("モブドロップ"),
        /** 釣り。エンチャント本や既エンチャント品が出る */
        FISHING("釣り"),
        /** 拾得。上のどれでも捕まえられなかったものの受け皿 */
        PICKUP("拾得");

        private final String label;

        Route(String label) {
            this.label = label;
        }

        public String label() {
            return label;
        }
    }
}
