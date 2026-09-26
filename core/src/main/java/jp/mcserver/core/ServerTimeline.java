package jp.mcserver.core;

/**
 * サーバー稼働日数に紐づく解禁・停止の規定（§5, §10, §11.1, §19）。
 *
 * <p>日は 1 始まり（開始当日が Day 1）。
 */
public final class ServerTimeline {

    private ServerTimeline() {}

    /** 新規プレイヤー係数を停止する日数（§5）。 */
    public static final int NEW_PLAYER_SUSPENSION_DAYS = 30;

    /** 制裁と戦争を実施できない日数（§10, §11.1）。 */
    public static final int CONFLICT_EMBARGO_DAYS = 60;

    /** 制裁・戦争が解禁されているか。 */
    public static boolean conflictAllowed(int serverDay) {
        if (serverDay < 1) {
            throw new IllegalArgumentException("稼働日が不正である: " + serverDay);
        }
        return serverDay > CONFLICT_EMBARGO_DAYS;
    }

    /** 解禁まであと何日か。解禁済みなら 0。 */
    public static int daysUntilConflictAllowed(int serverDay) {
        return Math.max(0, CONFLICT_EMBARGO_DAYS - serverDay + 1);
    }
}
