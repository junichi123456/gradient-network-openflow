package jp.mcserver.core.raid;

import java.util.HashMap;
import java.util.Map;

/**
 * 部位ごとの被弾管理（§12.6）。弱点の露出と実効倍率を扱う。
 *
 * <p>設計の意図は「弱点を殴り続けるだけ」にしないことである。弱点倍率は
 * <b>パリイ・妨害・空振りを成立させた直後の一定時間だけ</b>開く。
 * 倍率は反応で買うものであり、部位を覚えることで買えるものではない。
 */
public final class PartTracker {

    /** 弱点が露出する時間（tick）。 */
    public static final int EXPOSURE_TICKS = 60;

    /** 突進を空振りさせたときの露出時間（tick）。パリイより短い。 */
    public static final int WHIFF_EXPOSURE_TICKS = 40;

    /** 部位への攻撃の結果。 */
    public record Result(String part, double dealt, boolean immune, double multiplier) {

        /** 体力を削れたか。 */
        public boolean effective() {
            return dealt > 0;
        }

        /** 倍率が乗ったか。 */
        public boolean critical() {
            return multiplier > 1.0;
        }
    }

    private final Rig rig;
    private final Map<String, Double> taken = new HashMap<>();
    private int exposure;

    public PartTracker(Rig rig) {
        this.rig = rig;
    }

    /** 1tick 進める。露出は自然に閉じる。 */
    public void tick() {
        if (exposure > 0) {
            exposure--;
        }
    }

    /** 弱点を露出させる。すでに露出している場合は長いほうを採る。 */
    public void expose(int ticks) {
        if (ticks < 0) {
            throw new IllegalArgumentException("露出時間が負である: " + ticks);
        }
        exposure = Math.max(exposure, ticks);
    }

    /** 露出を閉じる。形態移行などで使う。 */
    public void closeExposure() {
        exposure = 0;
    }

    public boolean exposed() {
        return exposure > 0;
    }

    public int exposureRemaining() {
        return exposure;
    }

    /** その部位に蓄積したダメージ。 */
    public double takenBy(String part) {
        return taken.getOrDefault(part, 0.0);
    }

    /**
     * その部位の実効倍率。
     *
     * @param enraged 激昂中は露出型の弱点が閉じる（§12.6）
     */
    public double multiplier(String part, boolean enraged) {
        Rig.Part target = rig.part(part);
        if (!target.damageable()) {
            return 0;
        }
        if (!target.isWeakPoint() || enraged) {
            return 1.0;
        }
        return switch (target.gate()) {
            case ALWAYS -> target.vulnerability();
            case ON_EXPOSURE -> exposed() ? target.vulnerability() : 1.0;
        };
    }

    /**
     * 部位に攻撃を当てる。
     *
     * @param raw 素のダメージ量
     * @return 個体の体力から引くべき量
     */
    public Result hit(String part, double raw, boolean enraged) {
        if (raw < 0) {
            throw new IllegalArgumentException("ダメージが負である: " + raw);
        }
        double multiplier = multiplier(part, enraged);
        if (multiplier == 0) {
            return new Result(part, 0, true, 0);
        }
        double amount = raw * multiplier;
        taken.merge(part, amount, Double::sum);
        return new Result(part, amount, false, multiplier);
    }
}
