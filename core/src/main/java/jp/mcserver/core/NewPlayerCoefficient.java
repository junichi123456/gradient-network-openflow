package jp.mcserver.core;

/**
 * 新規プレイヤー係数（§5）。
 *
 * <p>サーバー初参加から30日以内のプレイヤーの有効活動時間を1.5倍として計上する。
 * 適用先は国家累計 C(n) のみであり、維持要件（直近30日）には適用しない。
 *
 * <p>サーバー開始から30日間は係数を停止する。開始直後は全員が新規であり、
 * 係数が格差是正として機能しないためである。
 */
public final class NewPlayerCoefficient {

    private NewPlayerCoefficient() {}

    public static final double MULTIPLIER = 1.5;

    /** 係数を停止するサーバー稼働日数。 */
    public static final int SERVER_SUSPENSION_DAYS = 30;

    /** 個人の適用窓の長さ（初参加日 + 30日まで）。 */
    public static final int PERSONAL_WINDOW_DAYS = 30;

    /**
     * @param serverDay 1 始まりのサーバー稼働日
     * @param joinDay   そのプレイヤーが初参加したサーバー稼働日
     */
    public static boolean applies(int serverDay, int joinDay) {
        if (serverDay < 1 || joinDay < 1 || serverDay < joinDay) {
            throw new IllegalArgumentException("日付が不正である: serverDay=" + serverDay + " joinDay=" + joinDay);
        }
        if (serverDay <= SERVER_SUSPENSION_DAYS) {
            return false;
        }
        return serverDay <= joinDay + PERSONAL_WINDOW_DAYS;
    }

    /** 国家累計へ加算する時間。適用外ならそのまま返す。 */
    public static double toCumulative(double hours, int serverDay, int joinDay) {
        return applies(serverDay, joinDay) ? hours * MULTIPLIER : hours;
    }
}
