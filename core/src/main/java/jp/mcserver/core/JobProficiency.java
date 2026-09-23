package jp.mcserver.core;

/**
 * ジョブ経験値と熟練度（§25）。
 *
 * <p>player level（§2、生涯累計）とは別に、「今、何を実践しているか」だけを測る指標。
 * 8カテゴリ（{@link Category}）が、それぞれ独立したジョブ経験値とレベルを持つ。
 * ジョブ経験値は§2.1の有効活動時間の判定窓が「計上」となっている時間帯にのみ発生する
 * （判定は{@link ActivityWindow}に相乗りし、新たな不正対策は設けない）。
 *
 * <p>各レベルのノード選択（8カテゴリ×10レベル、約150ノード）は、プレイヤーに見せる
 * UIカタログであり、アルゴリズムを持たないコンテンツデータのため本クラスの対象外とする。
 * ここでは純粋な数式・判定ロジック（レベル閾値・降格・カテゴリ別の経済効果上限）のみを
 * 扱う。
 */
public final class JobProficiency {

    private JobProficiency() {}

    /** ジョブのカテゴリ（§25.1）。 */
    public enum Category { AGRICULTURE, FISHING, HUSBANDRY, EXPLORATION, COMBAT, TRADE, DIPLOMACY, RESEARCH }

    /** ジョブレベルの上限。 */
    public static final int MAX_LEVEL = 10;

    /** 降格が始まるまでの無活動猶予日数（§25.5）。 */
    public static final int DEGRADE_GRACE_DAYS = 14;

    /** 猶予後、降格が1段階進む間隔（日）。 */
    public static final int DEGRADE_STEP_DAYS = 3;

    /** レベル閾値 L(n) = 10,000 × n²（§25.3）。 */
    public static long threshold(int level) {
        requireLevel(level);
        return 10_000L * level * level;
    }

    /** 累計ジョブXPから到達レベルを求める（{@link #threshold} の逆関数）。 */
    public static int levelFor(long cumulativeXp) {
        if (cumulativeXp < 0) {
            throw new IllegalArgumentException("累計XPが負である: " + cumulativeXp);
        }
        int level = 0;
        for (int n = 1; n <= MAX_LEVEL; n++) {
            if (cumulativeXp >= threshold(n)) {
                level = n;
            }
        }
        return level;
    }

    /**
     * 無活動が続いた場合の降格後レベル（§25.5）。
     *
     * <p>直近14日間は猶予され、以降3日ごとに1段階ずつ下がる。再びそのレベルまで
     * 戻れば、選択したノードは改めて提示される（降格と再上昇が事実上の再選択となる。
     * respec 専用機能は設けない）。
     */
    public static int levelAfterInactivity(int baseLevel, long daysSinceLastActivity) {
        if (baseLevel < 0 || baseLevel > MAX_LEVEL) {
            throw new IllegalArgumentException("レベルが範囲外である: " + baseLevel);
        }
        if (daysSinceLastActivity < 0) {
            throw new IllegalArgumentException("経過日数が負である: " + daysSinceLastActivity);
        }
        if (daysSinceLastActivity <= DEGRADE_GRACE_DAYS) {
            return baseLevel;
        }
        long overDays = daysSinceLastActivity - DEGRADE_GRACE_DAYS;
        long steps = overDays / DEGRADE_STEP_DAYS;
        return (int) Math.max(0, baseLevel - steps);
    }

    // ---- カテゴリ別の経済効果上限（§25.6・§25.8） ----
    // §14.3の金融政策（XP倍率2.5〜3.5倍、手数料率3〜7%）を意図せず超えないよう、
    // 通貨に触れる恩恵はカテゴリごとに文書化された上限の範囲内に限る。

    /** 農業：収穫による獲得exp上限（+12%）。 */
    public static final double AGRICULTURE_HARVEST_EXP_BONUS_CAP = 0.12;
    /** 農業：収穫量+1の確率上限（+40%）。 */
    public static final double AGRICULTURE_YIELD_CHANCE_BONUS_CAP = 0.40;
    /** 農業：突然変異率の上乗せ上限（基準0.1%の3倍＝+0.2%pt）。 */
    public static final double AGRICULTURE_MUTATION_RATE_BONUS_CAP = 0.002;
    /** 農業：育種の形質値ドリフト幅の上乗せ上限（±10。基本-2〜+8が-12〜+18になる）。 */
    public static final int AGRICULTURE_TRAIT_DRIFT_BONUS_CAP = 10;

    /** 探検：採掘・伐採速度の上限（各+10%）。 */
    public static final double EXPLORATION_SPEED_BONUS_CAP = 0.10;
    /** 探検：即時破壊確率の上限（各+6%）。 */
    public static final double EXPLORATION_INSTANT_BREAK_BONUS_CAP = 0.06;
    /** 探検：耐久非消耗確率の上限（各+30%）。 */
    public static final double EXPLORATION_DURABILITY_SAVE_BONUS_CAP = 0.30;

    /** 戦闘：クリティカルダメージ上限（+20%）。レイド次元では全効果が無効化される（§12.1・§26.4）。 */
    public static final double COMBAT_CRITICAL_DAMAGE_BONUS_CAP = 0.20;
    /** 戦闘：通常ダメージ上限（+16%）。 */
    public static final double COMBAT_NORMAL_DAMAGE_BONUS_CAP = 0.16;
    /** 戦闘：薙ぎ払いダメージ上限（+2）。 */
    public static final int COMBAT_SWEEP_DAMAGE_BONUS_CAP = 2;

    /** 商売：取引手数料の割引上限（-0.4%pt。§14.3の政策範囲3〜7%に対する割合）。 */
    public static final double TRADE_FEE_DISCOUNT_CAP = 0.004;

    /** 外交：貢献度換算の上限（+10%）。首長・リーダー在任中のみ有効。 */
    public static final double DIPLOMACY_CONTRIBUTION_BONUS_CAP = 0.10;
    /** 外交：対外支払い割引の上限（-4%）。首長・リーダー在任中のみ有効。 */
    public static final double DIPLOMACY_PAYMENT_DISCOUNT_CAP = 0.04;

    /** 畜産：騎乗中の怪我率低減の上限（-15%）。馬に騎乗している場合にのみ作用する（§26.7）。 */
    public static final double HUSBANDRY_RIDING_INJURY_REDUCTION_CAP = 0.15;
    /** 畜産：騎乗時の旋回性上乗せの上限（+15）。 */
    public static final int HUSBANDRY_TURNING_BONUS_CAP = 15;

    private static void requireLevel(int level) {
        if (level < 1 || level > MAX_LEVEL) {
            throw new IllegalArgumentException("レベルが範囲外である: " + level);
        }
    }
}
