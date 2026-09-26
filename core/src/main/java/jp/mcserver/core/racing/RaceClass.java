package jp.mcserver.core.racing;

/**
 * 競馬専用ワールドのクラス区分（`minecraft_server_spec.md` §27.5）。
 *
 * <p>新馬戦（500 exp）からG1（レースごとの個別額、9,000〜40,000 exp）まで、
 * 1着賞金が一直線に増える階段になっている。G1のみレースごとに額が異なるため
 * （§27.8.2、{@link ScheduledRace#prizeMoneyExp}を参照）、{@link #flatPrizeMoneyExp}
 * は定額を持たない。
 */
public enum RaceClass {
    NEWCOMER(500),
    MAIDEN(700),
    ONE_WIN(1000),
    TWO_WIN(1500),
    OPEN(2200),
    G3(3000),
    G2(6000),
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
