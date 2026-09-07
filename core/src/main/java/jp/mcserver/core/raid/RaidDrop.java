package jp.mcserver.core.raid;

import java.util.ArrayList;
import java.util.List;
import java.util.random.RandomGenerator;

/**
 * 討伐のドロップ品（§12.4）。
 *
 * <p><b>恒久的な性能を残さない。</b>旗模様・鍛治型・頭は性能を持たない装飾であり、
 * 馬鎧は通常と同等である。強さで釣るのではなく、<b>集める理由</b>で再挑戦の意義を作る。
 *
 * <p>配布は<b>確定贈与</b>である。抽選で外れる枠は無い。唯一の分岐は馬鎧と頭の
 * どちらが出るかであり、それも必ず1つ出る。
 *
 * <p>配る相手は「体力の {@code 1/(参加人数×1.5)} 以上のダメージを与えた者」である。
 * 参加人数で割るのは、大人数のときに一人あたりの必要量を下げるためで、
 * さらに 1.5 で割るのは<b>全員が均等に殴らなくても届く</b>ようにするためである。
 * 均等割り（{@code 1/参加人数}）にすると、少し出遅れた者が漏れる。
 */
public final class RaidDrop {

    private RaidDrop() {
    }

    /**
     * 必要量を緩める割り。
     *
     * <p>{@code 1/(参加人数×1.5)} の 1.5 がこれである。均等割りより<b>3分の1だけ軽い</b>。
     */
    public static final double CONTRIBUTION_DIVISOR = 1.5;

    /**
     * 配布に必要なダメージ量。
     *
     * @param maxHealth    その個体の最大体力（参加人数で決まる。§12.3）
     * @param participants 参加人数
     */
    public static double requiredDamage(long maxHealth, int participants) {
        if (maxHealth <= 0) {
            throw new IllegalArgumentException("最大体力が0以下である: " + maxHealth);
        }
        if (participants <= 0) {
            throw new IllegalArgumentException("参加人数が0以下である: " + participants);
        }
        return maxHealth / (participants * CONTRIBUTION_DIVISOR);
    }

    /** 配布の条件を満たしているか。 */
    public static boolean qualifies(double dealt, long maxHealth, int participants) {
        return dealt >= requiredDamage(maxHealth, participants);
    }

    /**
     * ドロップ品1種。
     *
     * @param id          識別子。実際の品への割り当ては描画側が持つ
     * @param displayName 表示名
     * @param min         最小個数
     * @param max         最大個数
     * @param weight      抽選の重み。確定枠では 0
     * @param copyable    複製できるか。鍛治型だけ false
     */
    public record Item(String id, String displayName, int min, int max, int weight,
                       boolean copyable) {

        public Item {
            if (min <= 0 || max < min) {
                throw new IllegalArgumentException("個数の範囲が不正である: " + id);
            }
        }

        static Item fixed(String id, String displayName, int min, int max) {
            return new Item(id, displayName, min, max, 0, true);
        }

        /** 個数を決める。範囲が1つならそのまま返す。 */
        public int amount(RandomGenerator random) {
            return min == max ? min : min + random.nextInt(max - min + 1);
        }
    }

    /** 確定で配る枠。 */
    public static final List<Item> GUARANTEED = List.of(
            Item.fixed("banner_pattern", "反骨裂きの旗模様", 1, 2),
            // 複製できないことで、4部位を揃えるのに4回の討伐が要る。それが再挑戦の意義になる
            new Item("smithing_template", "反骨裂きの鍛治型", 1, 1, 0, false),
            Item.fixed("solo_permit", "反骨裂きの単身討伐許可証", 1, 1));

    /**
     * どちらか1つが出る枠。
     *
     * <p>頭のほうが出にくいのは、<b>出にくいことが再挑戦の意義になる</b>からである。
     * 性能はどちらも持たない。
     */
    public static final List<Item> CHOICE = List.of(
            new Item("horse_armor", "反骨裂きの馬鎧", 1, 1, 80, true),
            new Item("trophy_head", "反骨裂きの頭", 1, 1, 20, true));

    /** 抽選枠の重みの合計。 */
    public static final int CHOICE_WEIGHT =
            CHOICE.stream().mapToInt(Item::weight).sum();

    /**
     * 実際に渡すもの。
     *
     * @param id          {@link Item#id()}
     * @param displayName 表示名
     * @param amount      個数
     * @param copyable    複製できるか
     */
    public record Grant(String id, String displayName, int amount, boolean copyable) {
    }

    /** 1人ぶんのドロップを決める。確定枠 + 抽選枠1つ。 */
    public static List<Grant> roll(RandomGenerator random) {
        List<Grant> grants = new ArrayList<>();
        for (Item item : GUARANTEED) {
            grants.add(new Grant(item.id(), item.displayName(), item.amount(random),
                    item.copyable()));
        }
        Item chosen = pick(random);
        grants.add(new Grant(chosen.id(), chosen.displayName(), chosen.amount(random),
                chosen.copyable()));
        return grants;
    }

    /** 抽選枠を1つ引く。 */
    public static Item pick(RandomGenerator random) {
        int roll = random.nextInt(CHOICE_WEIGHT);
        int seen = 0;
        for (Item item : CHOICE) {
            seen += item.weight();
            if (roll < seen) {
                return item;
            }
        }
        return CHOICE.get(CHOICE.size() - 1);
    }

    /** その識別子の品を複製できるか。知らない識別子は複製できるものとして扱う。 */
    public static boolean copyable(String id) {
        for (Item item : all()) {
            if (item.id().equals(id)) {
                return item.copyable();
            }
        }
        return true;
    }

    /** 全種。 */
    public static List<Item> all() {
        List<Item> items = new ArrayList<>(GUARANTEED);
        items.addAll(CHOICE);
        return items;
    }
}
