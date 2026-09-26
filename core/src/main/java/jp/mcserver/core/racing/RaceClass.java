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
}
