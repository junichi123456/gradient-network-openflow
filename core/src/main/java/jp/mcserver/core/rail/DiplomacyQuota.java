package jp.mcserver.core.rail;

/**
 * 外交連携および月間設置上限計算（`rail_infra_spec.md` F-02）。
 */
public final class DiplomacyQuota {

    private DiplomacyQuota() {
    }

    /** 基本設置枠（ブロック / 月）。 */
    public static final int BASE_LIMIT = 100;

    /** 同盟国1か国につき加算される率（基本枠に対する割合）。 */
    public static final double ALLIANCE_BONUS_RATE = 0.10;

    /** 属国（自国が宗主国側）1か国につき加算される率（基本枠に対する割合）。 */
    public static final double VASSAL_BONUS_RATE = 0.20;

    private static long bonusPerCountry(double rate) {
        return Math.round(BASE_LIMIT * rate);
    }

    /** 同盟国1か国あたりの加算（ブロック）。 */
    public static long allianceBonus() {
        return bonusPerCountry(ALLIANCE_BONUS_RATE);
    }

    /** 属国（宗主国側）1か国あたりの加算（ブロック）。 */
    public static long vassalBonus() {
        return bonusPerCountry(VASSAL_BONUS_RATE);
    }

    /**
     * 実効上限枠（ブロック / 月）。
     *
     * <p><b>属国が宗主国から枠を継承することはない</b>（自国が属国であっても、
     * 宗主国を持つこと自体からの加算は 0）。ここで数えるのは<b>自国が宗主国として
     * 持っている属国の数</b>である。
     *
     * @param allianceCount 自国が結んでいる同盟の数
     * @param suzerainOfVassalCount 自国が宗主国として持つ属国の数
     */
    public static long effectiveLimit(int allianceCount, int suzerainOfVassalCount) {
        if (allianceCount < 0 || suzerainOfVassalCount < 0) {
            throw new IllegalArgumentException("同盟・属国の数が負である");
        }
        return BASE_LIMIT + allianceCount * allianceBonus() + suzerainOfVassalCount * vassalBonus();
    }
}
