package jp.mcserver.core;

/**
 * 仕様書 §4.1 の数式群と、そこから派生する定数。
 *
 * <p>本クラスは Bukkit API に依存しない。サーバーを起動せずに検証できる。
 */
public final class Formulas {

    private Formulas() {}

    /** 有効活動時間 1 時間あたりの exp 換算レート（§6.3）。日次上限 30,000 exp ÷ 8h。 */
    public static final int EXP_PER_HOUR = 3_750;

    /** 国家ランクの上限（§4.1）。 */
    public static final int MAX_RANK = 25;

    /** 昇格所要時間 B(a) = (a+2)(a+5)（§4.1）。 */
    public static long promotionCost(int rank) {
        requireRank(rank);
        return (long) (rank + 2) * (rank + 5);
    }

    /** 累計所要時間 C(n) = n(n+4)(n+5)/3（§4.1）。rank n に到達するまでの総活動時間。 */
    public static long cumulativeCost(int rank) {
        requireRank(rank);
        return (long) rank * (rank + 4) * (rank + 5) / 3;
    }

    /** 定員 = max(3, a+2)（§4.1）。 */
    public static int capacity(int rank) {
        requireRank(rank);
        return Math.max(3, rank + 2);
    }

    /** 維持に必要な実効国民数 M(a) = max(3, ceil(0.75 × (a+2)))（§4.5）。 */
    public static int maintenanceCapacity(int rank) {
        requireRank(rank);
        return Math.max(3, (int) Math.ceil(0.75 * (rank + 2)));
    }

    /** 保有チャンク数。野営地=1、都市国家(rank0)=3、国家 rank a = 16a（§4.2）。 */
    public static int chunks(int rank) {
        requireRank(rank);
        return rank == 0 ? 3 : 16 * rank;
    }

    /** 維持に必要な直近30日の国民総活動時間 = B(a) × 0.1（§4.5）。 */
    public static double maintenanceActivityHours(int rank) {
        return promotionCost(rank) * 0.1;
    }

    /**
     * 実効国民数 N が支えられる最大ランク a' = min(25, ⌊4N/3⌋ − 2)（§4.6）。
     * M(a) ≤ N を満たす最大の a に一致する。
     */
    public static int rankSupportedBy(int effectiveCitizens) {
        if (effectiveCitizens < 3) {
            return -1; // 都市国家すら維持できない
        }
        return Math.min(MAX_RANK, Math.max(0, (4 * effectiveCitizens) / 3 - 2));
    }

    /**
     * 直近30日の総活動時間 A が維持できる最大ランク（§4.6 の降格下限）。
     * B(a) × 0.1 ≤ A を満たす最大の a。
     */
    public static int rankSustainedBy(double last30dHours) {
        if (last30dHours < 0) {
            throw new IllegalArgumentException("活動時間が負である: " + last30dHours);
        }
        // a² + 7a + 10 ≤ 10A  →  a ≤ (−7 + √(9 + 40A)) / 2
        double a = (-7 + Math.sqrt(9 + 40 * last30dHours)) / 2;
        return Math.min(MAX_RANK, Math.max(0, (int) Math.floor(a + 1e-9)));
    }

    /** シュルカーボックスの価格 30,000 + 2,000e（§14）。 */
    public static long shulkerPrice(int purchaseCount) {
        if (purchaseCount < 0) {
            throw new IllegalArgumentException("購入回数が負である: " + purchaseCount);
        }
        return 30_000L + 2_000L * purchaseCount;
    }

    /** シュルカーボックスを k 個買うまでの累計費用（e のリセットが無い場合、§14）。 */
    public static long shulkerCumulativeCost(int count) {
        long total = 0;
        for (int e = 0; e < count; e++) {
            total += shulkerPrice(e);
        }
        return total;
    }

    /** 国家単位のシュルカーボックス保有上限 = 2 × 定員（§14）。 */
    public static int shulkerLimit(int rank) {
        return 2 * capacity(rank);
    }

    private static void requireRank(int rank) {
        if (rank < 0 || rank > MAX_RANK) {
            throw new IllegalArgumentException("ランクが範囲外である: " + rank);
        }
    }
}
