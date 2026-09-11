package jp.mcserver.core;

/**
 * 有効活動時間の判定窓（§2.1）。
 *
 * <p>仕様は「12分の窓内で pitch の総変化量が 0 なら非計上」と定めるが、
 * <b>総変化量の値はどこにも使われない</b>。したがって累積ではなく真偽で保持する。
 *
 * <p>窓はログイン時刻を起点とし、経過時間のみセッションを跨いで繰り越す。
 * pitch の変化フラグはセッションごとにリセットする（ログイン直後に一度視線を動かし、
 * 残りを放置する抜け道を塞ぐため）。
 *
 * <p><b>{@link #advance(long, boolean)} は窓長より十分短い間隔で呼ぶ必要がある</b>
 * （移動イベントごと、または毎秒）。変化の有無は窓ごとに独立して評価されるため、
 * 複数の窓を一度にまとめて進めた場合、証拠があるのは最初の窓だけとみなされる。
 */
public final class ActivityWindow {

    /** 判定窓の長さ（分）。日次上限480分をちょうど40窓で割り切る。 */
    public static final int WINDOW_MINUTES = 12;

    /** 有効活動時間の日次上限（分）＝ 8時間。 */
    public static final int DAILY_CAP_MINUTES = 480;

    private long elapsedInWindowSeconds;
    private boolean pitchMoved;
    private int countedTodayMinutes;

    /** ログイン。経過時間の端数は保持し、pitch の変化フラグのみ落とす。 */
    public void onLogin() {
        this.pitchMoved = false;
    }

    /** ログアウト。端数の経過時間は次回ログインへ繰り越す。 */
    public void onLogout() {
        this.pitchMoved = false;
    }

    /** 日付の変わり目。計上済み時間をリセットする。窓の状態は維持する。 */
    public void onDayRollover() {
        this.countedTodayMinutes = 0;
    }

    /**
     * 経過時間を進める。
     *
     * @param seconds      経過秒数
     * @param pitchChanged この区間で pitch が変化したか
     * @return この呼び出しで新たに計上された分数
     */
    public int advance(long seconds, boolean pitchChanged) {
        if (seconds < 0) {
            throw new IllegalArgumentException("経過秒数が負である: " + seconds);
        }
        if (pitchChanged) {
            this.pitchMoved = true;
        }
        this.elapsedInWindowSeconds += seconds;

        int credited = 0;
        long windowSeconds = WINDOW_MINUTES * 60L;
        while (this.elapsedInWindowSeconds >= windowSeconds) {
            this.elapsedInWindowSeconds -= windowSeconds;
            if (this.pitchMoved) {
                int room = DAILY_CAP_MINUTES - this.countedTodayMinutes;
                int add = Math.min(WINDOW_MINUTES, Math.max(0, room));
                this.countedTodayMinutes += add;
                credited += add;
            }
            // 窓が閉じたらフラグを落とす。次の窓は改めて操作を要求する。
            this.pitchMoved = false;
        }
        return credited;
    }

    public int countedTodayMinutes() {
        return countedTodayMinutes;
    }

    public long elapsedInWindowSeconds() {
        return elapsedInWindowSeconds;
    }

    public boolean pitchMoved() {
        return pitchMoved;
    }

    public boolean dailyCapReached() {
        return countedTodayMinutes >= DAILY_CAP_MINUTES;
    }
}
