package jp.mcserver.core;

/**
 * 貢献度（§6.3）。自動離脱順と役職の自動就任判定に用いる複合指標。
 *
 * <p>貢献度 = 直近10日の有効活動時間（h） ＋ 直近30日の国庫納入額 ÷ 3,750
 *
 * <p>ただし納入による加算は、当該プレイヤーの直近10日の有効活動時間を上限とする。
 * 上限がなければ、役職の自動就任（§6.1）と統一時の自動離脱順（§8.3）を資金で
 * 操作できてしまう。
 */
public final class Contribution {

    private Contribution() {}

    public static double score(double activityHours10d, long donationExp30d) {
        if (activityHours10d < 0 || donationExp30d < 0) {
            throw new IllegalArgumentException("負の入力である");
        }
        double fromDonation = (double) donationExp30d / Formulas.EXP_PER_HOUR;
        return activityHours10d + Math.min(fromDonation, activityHours10d);
    }

    /** 納入による加算が上限に達しているか。 */
    public static boolean donationCapped(double activityHours10d, long donationExp30d) {
        return (double) donationExp30d / Formulas.EXP_PER_HOUR > activityHours10d;
    }
}
