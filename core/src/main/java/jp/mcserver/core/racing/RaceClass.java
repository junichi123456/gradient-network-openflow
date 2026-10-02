package jp.mcserver.core.racing;

/**
 * 競馬専用ワールドのクラス区分（`minecraft_server_spec.md` §27.5）。
 *
 * <p>新馬戦（500 exp）からG1（レースごとの個別額、60,000〜275,000 exp）まで、
 * 1着賞金が550倍の開きを持つ一直線の階段になっている。レース賞金は§2の日次exp
 * 上限（30,000）の対象外であるため、下位クラスは堅実だが小さい実入りに、重賞
 * （G3以上、§27.10の国別登録制の対象）は一攫千金にする、苛烈な競争として設計した。
 * G1のみレースごとに額が異なるため（§27.8.2、{@link ScheduledRace#prizeMoneyExp}を
 * 参照）、{@link #flatPrizeMoneyExp}は定額を持たない。
 */
public enum RaceClass {
    NEWCOMER(500),
    MAIDEN(800),
    ONE_WIN(1500),
    TWO_WIN(2500),
    OPEN(5000),
    G3(10_000),
    G2(25_000),
    G1(-1);

    private final long flatPrizeMoneyExp;

    RaceClass(long flatPrizeMoneyExp) {
        this.flatPrizeMoneyExp = flatPrizeMoneyExp;
    }

    /**
     * このクラスの1着賞金（exp）。{@link #G1}はレースごとの個別額を持つため
     * 定額が無く、呼び出すと例外になる（§27.8.2の{@link ScheduledRace}を使うこと）。
     */
    public long flatPrizeMoneyExp() {
        if (this == G1) {
            throw new IllegalStateException("G1は定額を持たない。レースごとの個別額（§27.8.2）を使うこと");
        }
        return flatPrizeMoneyExp;
    }

    /**
     * 通算の出走回数・1着回数から、現在所属するクラス（新馬戦〜オープン）を求める
     * （§27.5）。直前のクラスで1着を取るたびに1段階ずつ上がる——勝ち星0で出走歴
     * なしなら{@link #NEWCOMER}、勝ち星0で出走歴ありなら{@link #MAIDEN}、勝ち星1で
     * {@link #ONE_WIN}、勝ち星2で{@link #TWO_WIN}、勝ち星3以上で{@link #OPEN}。
     *
     * <p>重賞（{@link #G3}以上）への昇級は、この関数の対象外——出走・勝利の回数
     * ではなく「オープン戦の出走歴」と「累積賞金」で決まるため、
     * {@link #gradedEligible}で別途判定する。
     */
    public static RaceClass currentClass(int totalStarts, int totalWins) {
        if (totalStarts < 0) {
            throw new IllegalArgumentException("通算出走回数が負である: " + totalStarts);
        }
        if (totalWins < 0 || totalWins > totalStarts) {
            throw new IllegalArgumentException("通算1着回数が不正である: " + totalWins);
        }
        if (totalStarts == 0) {
            return NEWCOMER;
        }
        return switch (Math.min(totalWins, 3)) {
            case 0 -> MAIDEN;
            case 1 -> ONE_WIN;
            case 2 -> TWO_WIN;
            default -> OPEN;
        };
    }

    /** 重賞（G3以上）昇級に必要な、オープンクラスでの出走回数の下限（§27.5）。 */
    public static final int GRADED_ELIGIBILITY_MIN_OPEN_STARTS = 1;

    /** 重賞（G3以上）昇級に必要な、生涯累積賞金の下限（exp、§27.5）。 */
    public static final long GRADED_ELIGIBILITY_MIN_CUMULATIVE_PRIZE_EXP = 5000;

    /**
     * オープンクラスでの出走回数・生涯累積賞金から、重賞（{@link #G3}以上）への
     * 出走資格を判定する（§27.5）。オープン戦に出走歴があり、かつ累積賞金が
     * {@value #GRADED_ELIGIBILITY_MIN_CUMULATIVE_PRIZE_EXP} exp以上であることの
     * 両方を満たす必要がある——勝ち星3つの最短到達（最大でも4,500 exp程度）だけでは
     * 超えられない、意図した壁である。§27.10の国別登録枠は別途必要。
     */
    public static boolean gradedEligible(int openClassStarts, long cumulativePrizeExp) {
        if (openClassStarts < 0) {
            throw new IllegalArgumentException("オープンクラスでの出走回数が負である: " + openClassStarts);
        }
        if (cumulativePrizeExp < 0) {
            throw new IllegalArgumentException("累積賞金が負である: " + cumulativePrizeExp);
        }
        return openClassStarts >= GRADED_ELIGIBILITY_MIN_OPEN_STARTS
                && cumulativePrizeExp >= GRADED_ELIGIBILITY_MIN_CUMULATIVE_PRIZE_EXP;
    }
}
