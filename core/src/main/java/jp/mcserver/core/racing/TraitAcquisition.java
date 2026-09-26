package jp.mcserver.core.racing;

/**
 * 通常特性の獲得・上位特性への進化の判定（`minecraft_server_spec.md` §27.7.2）。
 *
 * <p>原案は特性ごとに専用の育成施設（「大歓声シミュレーション」等）を経由する
 * 進化条件も持つが、このワールドの調教メニューは§27.3の5種のみで対応する専用
 * 施設が無いため、**発動条件と同じ条件での勝利数**による経路のみを実装する
 * （原案が併記する「概ね2勝以上、または初挑戦初勝利」の一般則）。ランダム抽選の
 * 具体的な確率は実装段階で定める（§22・§23）。
 */
public final class TraitAcquisition {

    private TraitAcquisition() {}

    /** 通常特性の獲得に必要な、発動条件下での勝利数（初挑戦初勝利を除く）。 */
    public static final int NORMAL_ACQUISITION_WINS = 2;

    /** 上位特性への進化に必要な、発動条件下でのさらなる勝利数。 */
    public static final int UPPER_EVOLUTION_WINS = 2;

    /**
     * 通常特性を獲得できるか。発動条件下で{@value #NORMAL_ACQUISITION_WINS}勝以上、
     * または初挑戦で1着（初挑戦初勝利）のいずれかを満たせばよい。
     */
    public static boolean normalTraitAcquired(int startsUnderCondition, int winsUnderCondition) {
        validate(startsUnderCondition, winsUnderCondition);
        boolean firstAttemptWin = startsUnderCondition == 1 && winsUnderCondition == 1;
        return winsUnderCondition >= NORMAL_ACQUISITION_WINS || firstAttemptWin;
    }

    /**
     * 通常特性を保有した状態から、上位特性へ進化できるか。発動条件下でさらに
     * {@value #UPPER_EVOLUTION_WINS}勝を重ねれば進化する（初挑戦初勝利の特例は無い）。
     */
    public static boolean upperTraitEvolved(int winsUnderConditionSinceAcquired) {
        if (winsUnderConditionSinceAcquired < 0) {
            throw new IllegalArgumentException("勝利数が負である: " + winsUnderConditionSinceAcquired);
        }
        return winsUnderConditionSinceAcquired >= UPPER_EVOLUTION_WINS;
    }

    private static void validate(int starts, int wins) {
        if (starts < 0) {
            throw new IllegalArgumentException("出走数が負である: " + starts);
        }
        if (wins < 0 || wins > starts) {
            throw new IllegalArgumentException("勝利数が不正である: " + wins);
        }
    }
}
