package jp.mcserver.core;

/**
 * 昇格（§4.4）と降格（§4.6）の判定。
 */
public final class Territory {

    private Territory() {}

    /** 判定に必要な国家の状態。 */
    public record NationState(
            int rank,
            long cumulativeHours,
            double last30dHours,
            int effectiveCitizens) {

        public NationState {
            if (rank < 0 || rank > Formulas.MAX_RANK) {
                throw new IllegalArgumentException("ランクが範囲外である: " + rank);
            }
        }
    }

    /** 活動要件（§4.5）: 直近30日の国民総活動時間 ≥ B(a) × 0.1。 */
    public static boolean meetsActivityRequirement(NationState s) {
        return s.last30dHours() >= Formulas.maintenanceActivityHours(s.rank());
    }

    /** 定員要件（§4.5）: 実効国民数 ≥ M(a)。 */
    public static boolean meetsCapacityRequirement(NationState s) {
        return s.effectiveCitizens() >= Formulas.maintenanceCapacity(s.rank());
    }

    /** 閾値の70%を切ったか（§4.5 の警告条件）。 */
    public static boolean shouldWarn(NationState s) {
        boolean activityWarn = s.last30dHours() < Formulas.maintenanceActivityHours(s.rank()) * 0.7;
        boolean capacityWarn = s.effectiveCitizens() < Math.ceil(Formulas.maintenanceCapacity(s.rank()) * 0.7);
        return activityWarn || capacityWarn;
    }

    public record PromotionDecision(boolean allowed, String reason) {}

    /**
     * 昇格の可否（§4.4）。首長の任意申請により実行されるため、本メソッドは条件判定のみ。
     *
     * @param promotedToday 当日すでに昇格しているか（1日1ランクまで）
     */
    public static PromotionDecision canPromote(NationState s, boolean promotedToday) {
        if (s.rank() >= Formulas.MAX_RANK) {
            return new PromotionDecision(false, "既に上限ランクである");
        }
        if (promotedToday) {
            return new PromotionDecision(false, "昇格は1日1ランクまでである");
        }
        int next = s.rank() + 1;
        if (s.cumulativeHours() < Formulas.cumulativeCost(next)) {
            return new PromotionDecision(false,
                    "累計活動時間が不足している: " + s.cumulativeHours() + " / " + Formulas.cumulativeCost(next));
        }
        if (s.effectiveCitizens() < Formulas.capacity(next)) {
            return new PromotionDecision(false,
                    "実効国民数が不足している: " + s.effectiveCitizens() + " / " + Formulas.capacity(next));
        }
        if (!meetsActivityRequirement(s) || !meetsCapacityRequirement(s)) {
            return new PromotionDecision(false, "維持要件を現に満たしていない");
        }
        return new PromotionDecision(true, "昇格可能");
    }

    public enum DemotionCause { NONE, ACTIVITY, CAPACITY, BOTH }

    public record DemotionDecision(int newRank, DemotionCause cause) {
        public boolean demoted() {
            return cause != DemotionCause.NONE;
        }
    }

    /**
     * 降格の判定（§4.6）。
     *
     * <p>活動要件違反は1回目 −1、2回連続以降 −8。ただし降格後のランクは
     * 「直近30日の総活動時間が維持閾値を満たす最大のランク」を下回らない。
     * 定員起因は現在の実効国民数が支える最大ランクへ即時調整する。
     * 両方に抵触した場合はより低いランクを採る。
     *
     * @param consecutiveActivityViolations 当該違反を含む連続違反回数（1 が初回）
     */
    public static DemotionDecision evaluate(NationState s, int consecutiveActivityViolations) {
        boolean activityViolation = !meetsActivityRequirement(s);
        boolean capacityViolation = !meetsCapacityRequirement(s);

        if (!activityViolation && !capacityViolation) {
            return new DemotionDecision(s.rank(), DemotionCause.NONE);
        }

        int byActivity = s.rank();
        if (activityViolation) {
            int step = consecutiveActivityViolations <= 1 ? 1 : 8;
            int floor = Formulas.rankSustainedBy(s.last30dHours());
            byActivity = Math.max(floor, s.rank() - step);
        }

        int byCapacity = s.rank();
        if (capacityViolation) {
            byCapacity = Math.max(0, Formulas.rankSupportedBy(s.effectiveCitizens()));
        }

        int newRank = Math.min(byActivity, byCapacity);
        DemotionCause cause = activityViolation && capacityViolation ? DemotionCause.BOTH
                : activityViolation ? DemotionCause.ACTIVITY : DemotionCause.CAPACITY;
        return new DemotionDecision(newRank, cause);
    }
}
