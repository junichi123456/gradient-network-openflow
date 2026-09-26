package jp.mcserver.core.racing;

/**
 * 競走馬の能力値のスケールと成長上限（`minecraft_server_spec.md` §27.3・§27.4）。
 *
 * <p>各能力値（スピード・スタミナ・パワー・根性・賢さ）は0〜{@value #MAX_VALUE}の
 * 範囲を持つ。産駒誕生時の値は、出生時点で{@value #BIRTH_CAP}
 * （{@value #MAX_VALUE}の72%）を超えない。それより上は、調教（生涯合計で
 * 最大{@value #MAX_TRAINING_GROWTH}まで）と、特性（§27.7.2）の能力プラス補正
 * （上限にさらに加算し、{@value #MAX_VALUE}を超えることもある）でのみ到達できる。
 */
public final class AbilityValue {

    private AbilityValue() {}

    /** 能力値の基準上限（特性による加算を含まない、§27.3）。 */
    public static final int MAX_VALUE = 100;

    /** 出生時点で能力値が超えられない上限（{@value #MAX_VALUE}の72%、§27.3）。 */
    public static final int BIRTH_CAP = 72;

    /** 調教による生涯合計の成長上限（§27.3）。 */
    public static final int MAX_TRAINING_GROWTH = 20;

    /** スピード0のときの最高移動速度（m/s、§27.4）。 */
    public static final double MIN_SPEED_MS = 10.0;

    /** スピード{@value #MAX_VALUE}のときの最高移動速度（m/s、§27.4）。 */
    public static final double MAX_SPEED_MS = 25.0;

    /** 出生時点の能力値として有効か（{@value #BIRTH_CAP}以下か）。 */
    public static boolean birthValueValid(int value) {
        if (value < 0) {
            throw new IllegalArgumentException("能力値が負である: " + value);
        }
        return value <= BIRTH_CAP;
    }

    /** 調教による生涯合計の成長量が、上限（{@value #MAX_TRAINING_GROWTH}）以内か。 */
    public static boolean trainingGrowthValid(int totalTrainingGrowth) {
        if (totalTrainingGrowth < 0) {
            throw new IllegalArgumentException("調教による成長量が負である: " + totalTrainingGrowth);
        }
        return totalTrainingGrowth <= MAX_TRAINING_GROWTH;
    }

    /**
     * スピードの値（特性による能力プラス補正を含む、{@value #MAX_VALUE}を超えうる）から、
     * 最高移動速度（m/s）を線形換算する（§27.4）。スピード0で{@value #MIN_SPEED_MS}、
     * {@value #MAX_VALUE}で{@value #MAX_SPEED_MS}になる傾きを、{@value #MAX_VALUE}を
     * 超えた領域にもそのまま外挿する。
     */
    public static double baseSpeedMetersPerSecond(double speed) {
        if (speed < 0) {
            throw new IllegalArgumentException("スピードが負である: " + speed);
        }
        return MIN_SPEED_MS + speed / MAX_VALUE * (MAX_SPEED_MS - MIN_SPEED_MS);
    }
}
