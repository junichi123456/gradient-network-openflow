package jp.mcserver.core.racing;

/**
 * 1頭が保有できる通常・上位特性の枠数（`minecraft_server_spec.md` §27.7.2）。
 *
 * <p>固有特性・レジェンド特性・絆特性の枠（各1）は今回未実装のため対象外
 * （§23）。通常特性は最大{@value #MAX_TRAIT_SLOTS}枠で、そのうち信頼レベルに
 * 応じて最大{@value #MAX_UPPER_SLOTS}枠までを上位特性に進化させられる
 * （信頼レベル1＝1個、2＝2個、3＝3個）。上位特性は通常特性と同じ枠を使う
 * （進化元を置き換える）ため、通常特性数＋上位特性数ではなく、**保有系統数**
 * が{@value #MAX_TRAIT_SLOTS}の対象になる。
 */
public final class TraitLoadout {

    private TraitLoadout() {}

    /** 保有できる特性の系統数の上限（§27.7.2）。 */
    public static final int MAX_TRAIT_SLOTS = 8;

    /** 上位特性に進化させられる系統数の上限（信頼レベル3で到達、§27.7.2）。 */
    public static final int MAX_UPPER_SLOTS = 3;

    /**
     * 信頼レベルに応じた、上位特性に進化させられる系統数の上限。
     * 信頼レベル0で0、1で1、2で2、3以上で{@value #MAX_UPPER_SLOTS}に頭打ちになる。
     */
    public static int maxUpperSlots(int trustLevel) {
        if (trustLevel < 0) {
            throw new IllegalArgumentException("信頼レベルが負である: " + trustLevel);
        }
        return Math.min(trustLevel, MAX_UPPER_SLOTS);
    }

    /**
     * 保有する系統数・うち上位特性に進化済みの数・信頼レベルの組み合わせが、
     * 枠の制約内に収まっているか。
     */
    public static boolean isValid(int totalLineages, int upperLineages, int trustLevel) {
        if (totalLineages < 0) {
            throw new IllegalArgumentException("保有系統数が負である: " + totalLineages);
        }
        if (upperLineages < 0) {
            throw new IllegalArgumentException("上位特性の系統数が負である: " + upperLineages);
        }
        if (upperLineages > totalLineages) {
            return false;
        }
        return totalLineages <= MAX_TRAIT_SLOTS && upperLineages <= maxUpperSlots(trustLevel);
    }
}
