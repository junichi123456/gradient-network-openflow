package jp.mcserver.core.raid;

/**
 * 追従の遅れ（§12.6）。
 *
 * <p><b>待機中の個体は、相手の「少し前の位置」を向く。</b>その場で相手に張り付くように
 * 向き直る個体は、重さが無く反応の隙も読めない。遅れを入れると、横へ回り込んだ相手を
 * 追いきれない瞬間が生まれ、<b>背後を取る動きに意味が出る</b>。
 *
 * <p>毎tick位置を押し込み、読み出しでは指定tick前の値を返す。溜まりきる前は
 * <b>最も古い値</b>を返すので、出現直後に向きが飛ぶことはない。
 *
 * <p>遅らせるのは<b>狙う位置</b>であって、回れる角度ではない。角度の上限は
 * {@link KnightDefinition#MAX_TURN_DEGREES} が別に持つ。両方を混ぜると、
 * 遅れを詰めたいときにどちらを触ればよいか分からなくなる。
 */
public final class TrackingDelay {

    private final int delayTicks;
    private final double[] xs;
    private final double[] zs;
    /** 押し込んだ総数。位置の割り出しに使う */
    private long count;

    /**
     * @param delayTicks 遅らせるtick数。0 なら遅れ無し
     */
    public TrackingDelay(int delayTicks) {
        if (delayTicks < 0) {
            throw new IllegalArgumentException("遅れが負である: " + delayTicks);
        }
        this.delayTicks = delayTicks;
        this.xs = new double[delayTicks + 1];
        this.zs = new double[delayTicks + 1];
    }

    public int delayTicks() {
        return delayTicks;
    }

    /** 1tickぶんの位置を押し込む。 */
    public void push(double x, double z) {
        int slot = (int) (count % xs.length);
        xs[slot] = x;
        zs[slot] = z;
        count++;
    }

    /** 読み出せる値があるか。 */
    public boolean has() {
        return count > 0;
    }

    /** 遅れた側の X。 */
    public double x() {
        return xs[oldest()];
    }

    /** 遅れた側の Z。 */
    public double z() {
        return zs[oldest()];
    }

    /** 溜めた履歴を捨てる。狙う相手が変わったときに呼ぶ。 */
    public void reset() {
        count = 0;
    }

    /**
     * 最も古い値の位置。
     *
     * <p>溜まりきっていれば「次に上書きする枠」が最も古い。溜まりきる前は先頭が最も古い。
     */
    private int oldest() {
        return count >= xs.length ? (int) (count % xs.length) : 0;
    }
}
