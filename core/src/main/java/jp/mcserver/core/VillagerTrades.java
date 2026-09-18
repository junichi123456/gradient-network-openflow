package jp.mcserver.core;

import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import java.util.Optional;
import java.util.function.Function;
import java.util.random.RandomGenerator;

/**
 * 村人の取引テーブル（§3.2）。
 *
 * <p><b>村人はエンチャント済みの品を提供しない。</b>ただし、エンチャント済みの品を出していた
 * 枠のうち定めたものは、レベル1のエンチャント本または効能付きの矢に差し替える（§3.2 の代替表）。
 * 代替を定めていない枠は、埋め合わせを置かずに削除する。
 *
 * <p>判定は「取引が渡す品」に対してのみ行う。支払い側にエンチャント品を要求する取引は
 * バニラに存在せず、仮にあっても供給経路ではない。
 */
public final class VillagerTrades {

    /** エンチャント本。中身が空でも品目そのものを禁じる。 */
    public static final String ENCHANTED_BOOK = "ENCHANTED_BOOK";

    /** 効能付きの矢。 */
    public static final String TIPPED_ARROW = "TIPPED_ARROW";

    /** 代替で渡す本のエンチャントは必ずレベル1。 */
    public static final int SUBSTITUTE_ENCHANTMENT_LEVEL = 1;

    /** 代替の取引の使用回数。バニラの司書のエンチャント本の枠に合わせた。 */
    public static final int SUBSTITUTE_MAX_USES = 12;

    /** 需要による値上がり率。バニラの取引と同じ。 */
    public static final double PRICE_MULTIPLIER = 0.2;

    /** 効能付きの矢の枠（矢師の達人枠と同じ構成）。 */
    public static final int ARROW_EMERALDS = 2;
    public static final int ARROW_INGREDIENT = 5;
    public static final int ARROW_RESULT = 5;

    /** 効能付きの矢の効果。個体ごとに1つ引く（§22 で調整対象）。 */
    public static final List<String> ARROW_EFFECTS =
            List.of("poison", "slowness", "weakness", "harming", "healing", "regeneration");

    /**
     * 代替表（§3.2）。エンチャント済みの品を出していた枠を差し替える。
     *
     * <p>職業と村人レベルで引く。1レベルにつき1枠まで。同じレベルに該当枠が2つある場合、
     * 残る1つは削除される。
     */
    public static final List<Substitute> SUBSTITUTES = List.of(
            // 防具鍛冶: 熟練=耐久力I, 達人=ダメージ軽減I
            Substitute.book("armorer", 4, "unbreaking"),
            Substitute.book("armorer", 5, "protection"),
            // 道具鍛冶: 一人前=耐久力I, 熟練=効率強化I, 達人=幸運I
            Substitute.book("toolsmith", 3, "unbreaking"),
            Substitute.book("toolsmith", 4, "efficiency"),
            Substitute.book("toolsmith", 5, "fortune"),
            // 武器鍛冶: 達人=ダメージ増加I
            Substitute.book("weaponsmith", 5, "sharpness"),
            // 矢師: 熟練=効能付きの矢×5（達人枠に元からある取引と同じ構成）
            Substitute.arrows("fletcher", 4));

    private VillagerTrades() {
    }

    /**
     * 品に付いたエンチャント。
     *
     * @param key   エンチャントの名前空間キー（列挙の定数名は版によって変わる）
     * @param level 強化レベル
     */
    public record Enchant(String key, int level) {

        public Enchant {
            if (key == null || key.isBlank()) {
                throw new IllegalArgumentException("エンチャントのキーが空です");
            }
            if (level < 1) {
                throw new IllegalArgumentException("エンチャントのレベルが1未満です");
            }
        }

        /** 名前空間の有無・大小文字を問わずに比べる。 */
        public boolean is(String other) {
            return normalize(key).equals(normalize(other));
        }
    }

    /**
     * 取引が渡す品。Bukkit の ItemStack をサーバー非依存の形に落としたもの。
     *
     * @param itemKey            品目（名前空間の有無・大小文字を問わない）
     * @param enchantments       品そのものに付いたエンチャント
     * @param storedEnchantments 本に収められたエンチャント
     */
    public record Offer(String itemKey, List<Enchant> enchantments, List<Enchant> storedEnchantments) {

        public Offer {
            if (itemKey == null || itemKey.isBlank()) {
                throw new IllegalArgumentException("品目が空です");
            }
            enchantments = List.copyOf(enchantments);
            storedEnchantments = List.copyOf(storedEnchantments);
        }

        /** エンチャントの付かない品。 */
        public static Offer plain(String itemKey) {
            return new Offer(itemKey, List.of(), List.of());
        }

        /** エンチャント済みの品（武器・防具・道具の完成品）。 */
        public static Offer enchanted(String itemKey, String enchantment, int level) {
            return new Offer(itemKey, List.of(new Enchant(enchantment, level)), List.of());
        }

        /** エンチャント本。 */
        public static Offer book(String enchantment, int level) {
            return new Offer(ENCHANTED_BOOK, List.of(), List.of(new Enchant(enchantment, level)));
        }

        /** 中身のないエンチャント本。 */
        public static Offer emptyBook() {
            return new Offer(ENCHANTED_BOOK, List.of(), List.of());
        }

        /** 品目が一致するか。名前空間付きの表記でも通る。 */
        public boolean is(String other) {
            return normalize(itemKey).equals(normalize(other));
        }
    }

    /**
     * 代替として置く取引（§3.2）。
     *
     * @param profession  職業の名前空間キー（armorer, toolsmith, weaponsmith, fletcher）
     * @param level       村人レベル 1〜5（見習い〜達人）
     * @param itemKey     渡す品
     * @param enchantment 本に収めるエンチャント。本でない場合は null
     */
    public record Substitute(String profession, int level, String itemKey, String enchantment) {

        public Substitute {
            if (profession == null || profession.isBlank()) {
                throw new IllegalArgumentException("職業が空です");
            }
            if (level < 1 || level > 5) {
                throw new IllegalArgumentException("村人レベルは1〜5です: " + level);
            }
            if (itemKey == null || itemKey.isBlank()) {
                throw new IllegalArgumentException("品目が空です");
            }
        }

        static Substitute book(String profession, int level, String enchantment) {
            return new Substitute(profession, level, ENCHANTED_BOOK, enchantment);
        }

        static Substitute arrows(String profession, int level) {
            return new Substitute(profession, level, TIPPED_ARROW, null);
        }

        /** エンチャント本を渡す代替か。 */
        public boolean book() {
            return enchantment != null;
        }
    }

    // ------------------------------------------------------------------ 判定

    /** その取引を取引テーブルから外すか（代替を認めない場合）。 */
    public static boolean blocked(Offer offer) {
        return blocked(offer, List.of());
    }

    /**
     * その取引を取引テーブルから外すか。
     *
     * @param allowed その村人に認めた代替。これらが渡す品は残す
     */
    public static boolean blocked(Offer offer, Collection<Substitute> allowed) {
        boolean enchanted = offer.is(ENCHANTED_BOOK)
                || !offer.enchantments().isEmpty()
                || !offer.storedEnchantments().isEmpty();
        if (!enchanted) {
            return false;
        }
        for (Substitute substitute : allowed) {
            if (provides(offer, substitute)) {
                return false;
            }
        }
        return true;
    }

    /** その取引が、その代替そのものを渡しているか。取り除きと追加を何度繰り返しても増えないための鍵。 */
    public static boolean provides(Offer offer, Substitute substitute) {
        if (!offer.is(substitute.itemKey())) {
            return false;
        }
        if (!substitute.book()) {
            return true;
        }
        // レベルを上げた本は代替ではない。ちょうどレベル1が1つだけ入っているものを認める
        return offer.enchantments().isEmpty()
                && offer.storedEnchantments().size() == 1
                && offer.storedEnchantments().get(0).is(substitute.enchantment())
                && offer.storedEnchantments().get(0).level() == SUBSTITUTE_ENCHANTMENT_LEVEL;
    }

    /**
     * 取引の並びから、禁じた品を渡す枠を取り除く。順序と残った枠の同一性は保つ
     * （取引回数や割引の状態は枠そのものが持つため、作り直さない）。
     */
    public static <T> List<T> filter(List<T> recipes, Function<T, Offer> result) {
        return filter(recipes, result, List.of());
    }

    /** 認めた代替は残したうえで絞り込む。 */
    public static <T> List<T> filter(List<T> recipes, Function<T, Offer> result,
                                     Collection<Substitute> allowed) {
        List<T> kept = new ArrayList<>(recipes.size());
        for (T recipe : recipes) {
            if (!blocked(result.apply(recipe), allowed)) {
                kept.add(recipe);
            }
        }
        return kept;
    }

    // ------------------------------------------------------------------ 代替表

    /** その職業・そのレベルに定めた代替。 */
    public static Optional<Substitute> substituteAt(String profession, int level) {
        for (Substitute substitute : SUBSTITUTES) {
            if (normalize(substitute.profession()).equals(normalize(profession))
                    && substitute.level() == level) {
                return Optional.of(substitute);
            }
        }
        return Optional.empty();
    }

    /** そのレベルまでに得ているはずの代替。下のレベルの取引は上のレベルでも残るため。 */
    public static List<Substitute> substitutesUpTo(String profession, int level) {
        List<Substitute> found = new ArrayList<>();
        for (int l = 1; l <= level; l++) {
            substituteAt(profession, l).ifPresent(found::add);
        }
        return found;
    }

    // ------------------------------------------------------------------ 値段

    /**
     * エンチャント本の値段（エメラルド）。バニラの司書の式に合わせる。
     *
     * <pre>2 + 3×エンチャントレベル + 乱数[0, 5 + 10×エンチャントレベル)</pre>
     *
     * <p>秘蔵のエンチャントは2倍、上限は64。レベル1・非秘蔵なら5〜19となる。
     */
    public static int bookPrice(int enchantmentLevel, boolean treasure, RandomGenerator random) {
        if (enchantmentLevel < 1) {
            throw new IllegalArgumentException("エンチャントのレベルが1未満です");
        }
        int price = 2 + 3 * enchantmentLevel + random.nextInt(5 + enchantmentLevel * 10);
        if (treasure) {
            price *= 2;
        }
        return Math.min(64, price);
    }

    /** その取引を1回使ったときに村人が得る経験値。バニラのレベル帯に合わせた。 */
    public static int tradeExperience(int villagerLevel) {
        return switch (villagerLevel) {
            case 1 -> 2;
            case 2 -> 10;
            case 3 -> 20;
            case 4, 5 -> 30;
            default -> throw new IllegalArgumentException("村人レベルは1〜5です: " + villagerLevel);
        };
    }

    /** 名前空間の有無と大小文字を無視して比べるための正規化。 */
    private static String normalize(String key) {
        return key.substring(key.indexOf(':') + 1).toLowerCase(java.util.Locale.ROOT);
    }
}
