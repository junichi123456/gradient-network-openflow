package jp.mcserver.core.raid;

import java.util.HashMap;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;

/**
 * 姿勢のつなぎ（§12.6）。
 *
 * <p>キーフレームに緩急を付けても、<b>モーションが切り替わる境目</b>は滑らかにならない。
 * 攻撃モーションの最終姿勢から待機の姿勢へは、次の更新で一気に飛ぶためである。
 * ここでは切り替わりの前後を混ぜ、指定の時間をかけて移り変わらせる。
 *
 * <p>混ぜる対象は「切り替え前に実際に適用していた姿勢」と「新しいモーションの姿勢」である。
 * 片方にしか現れない部位は、静止時の姿勢（{@link Transform#IDENTITY}）を相手とみなす。
 */
public final class PoseTransition {

    /** つなぎに要する時間（tick）。 */
    public static final int DEFAULT_TICKS = 6;

    private final int durationTicks;
    private final Animation.Easing easing;
    private Map<String, Transform> from = Map.of();
    private int elapsed;
    private boolean running;

    public PoseTransition(int durationTicks, Animation.Easing easing) {
        if (durationTicks <= 0) {
            throw new IllegalArgumentException("つなぎの時間が0以下である: " + durationTicks);
        }
        if (easing == null) {
            throw new IllegalArgumentException("緩急が null である");
        }
        this.durationTicks = durationTicks;
        this.easing = easing;
    }

    public PoseTransition() {
        this(DEFAULT_TICKS, Animation.DEFAULT_EASING);
    }

    /**
     * つなぎを開始する。切り替えの直前に適用していた姿勢を渡す。
     *
     * @param currentPose 直前に適用していた姿勢。空なら静止時の姿勢から始まる
     */
    public void begin(Map<String, Transform> currentPose) {
        this.from = Map.copyOf(currentPose);
        this.elapsed = 0;
        this.running = true;
    }

    /** つなぎの最中か。 */
    public boolean running() {
        return running;
    }

    /** つなぎの進み具合（0〜1）。 */
    public double progress() {
        return running ? Math.min(1.0, (double) elapsed / durationTicks) : 1.0;
    }

    /**
     * 目標の姿勢につなぎを反映して返し、指定 tick ぶん進める。
     *
     * @param target      新しいモーションの姿勢
     * @param elapsedTicks 前回の呼び出しからの経過 tick
     */
    public Map<String, Transform> apply(Map<String, Transform> target, int elapsedTicks) {
        if (!running) {
            return target;
        }
        // 先に時間を進める。進めずに混ぜると、切り替えた最初の1回が前の姿勢のまま止まる
        elapsed += Math.max(1, elapsedTicks);
        double ratio = easing.apply(progress());
        Set<String> parts = new HashSet<>(from.keySet());
        parts.addAll(target.keySet());
        Map<String, Transform> blended = new HashMap<>(parts.size());
        for (String part : parts) {
            Transform start = from.getOrDefault(part, Transform.IDENTITY);
            Transform end = target.getOrDefault(part, Transform.IDENTITY);
            blended.put(part, start.lerp(end, ratio));
        }
        if (elapsed >= durationTicks) {
            running = false;
            from = Map.of();
        }
        return blended;
    }

    /** つなぎを打ち切る。 */
    public void clear() {
        running = false;
        from = Map.of();
        elapsed = 0;
    }
}
