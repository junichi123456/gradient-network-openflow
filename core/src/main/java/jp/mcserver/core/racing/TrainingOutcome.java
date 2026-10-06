package jp.mcserver.core.racing;

/**
 * 1日1回の調教（`minecraft_server_spec.md` §27.3）の結果と、その量。
 *
 * <p>毎日0:30更新で1回だけ行える調教（休養を除く）は、この3段階の結果を持ち、
 * 対象ステータスと疲労度がそれぞれ変化する。確率分布はコンディション・疲労度に
 * 連動するが、具体的な式は実装段階で定める（§23）。
 */
public enum TrainingOutcome {
    FAILURE(1, 20),
    SUCCESS(2, 15),
    GREAT_SUCCESS(4, 15);

    private final int statGain;
    private final int fatigueGain;

    TrainingOutcome(int statGain, int fatigueGain) {
        this.statGain = statGain;
        this.fatigueGain = fatigueGain;
    }

    /** この結果で対象ステータスに加わる値。 */
    public int statGain() {
        return statGain;
    }

    /** この結果で加わる疲労度。 */
    public int fatigueGain() {
        return fatigueGain;
    }
}
