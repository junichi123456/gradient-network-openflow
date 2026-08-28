package jp.mcserver.core.raid;

import java.util.HashMap;
import java.util.LinkedHashSet;
import java.util.Map;
import java.util.Set;

/**
 * 部位ごとの被弾管理（§12.6）。弱点の露出、装甲の破壊、実効倍率を扱う。
 *
 * <p>設計の意図は「弱点を殴り続けるだけ」にしないことである。弱点倍率は
 * <b>パリイや妨害を成功させた直後の一定時間だけ</b>開く。装甲はそれとは別に、
 * 累積ダメージで壊すことで恒久的な弱点を開く。前者は反応、後者は蓄積で報われる。
 */
public final class PartTracker {

    /** 弱点が露出する時間（tick）。 */
    public static final int EXPOSURE_TICKS = 60;

    /** 突進を空振りさせたときの露出時間（tick）。パリイより短い。 */
    public static final int WHIFF_EXPOSURE_TICKS = 40;

    /**
     * 部位への攻撃の結果。
     *
     * @param dealt    個体の体力から引く量
     * @param absorbed 装甲が受け止めた量。<b>体力には入らない</b>
     */
    public record Result(String part, double dealt, double absorbed, boolean broke,
                         boolean immune, double multiplier) {

        /** 体力を削れたか。 */
        public boolean effective() {
            return dealt > 0;
        }

        /** 装甲に吸収されたか。 */
        public boolean absorbedByArmor() {
            return absorbed > 0;
        }

        /** 倍率が乗ったか。 */
        public boolean critical() {
            return multiplier > 1.0;
        }
    }

    private final Rig rig;
    private final long maxHealth;
    private final Map<String, Double> taken = new HashMap<>();
    private final Set<String> broken = new LinkedHashSet<>();
    private int exposure;

    public PartTracker(Rig rig, long maxHealth) {
        if (maxHealth <= 0) {
            throw new IllegalArgumentException("最大体力が0以下である: " + maxHealth);
        }
        this.rig = rig;
        this.maxHealth = maxHealth;
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

    public boolean exposed() {
        return exposure > 0;
    }

    public int exposureRemaining() {
        return exposure;
    }

    /** 破壊可能な部位がすべて壊れたか。 */
    public boolean allArmorBroken() {
        var armor = rig.breakableParts();
        return !armor.isEmpty() && armor.stream().allMatch(part -> broken.contains(part.name()));
    }

    public boolean isBroken(String part) {
        return broken.contains(part);
    }

    public Set<String> brokenParts() {
        return Set.copyOf(broken);
    }

    /** その部位に蓄積したダメージ。 */
    public double takenBy(String part) {
        return taken.getOrDefault(part, 0.0);
    }

    /** 破壊までに残っているダメージ量。破壊不可なら 0。 */
    public double remainingToBreak(String part) {
        Rig.Part target = rig.part(part);
        if (!target.breakable() || broken.contains(part)) {
            return 0;
        }
        return Math.max(0, target.breakThreshold() * maxHealth - takenBy(part));
    }

    /**
     * その部位の実効倍率。
     *
     * @param enraged 激昂中は弱点が閉じる（§12.6）。装甲破壊による弱点も閉じない例外は設けない
     */
    public double multiplier(String part, boolean enraged) {
        Rig.Part target = rig.part(part);
        if (!target.damageable() || broken.contains(part)) {
            return 0;
        }
        if (!target.isWeakPoint()) {
            return 1.0;
        }
        if (enraged) {
            return 1.0;
        }
        return switch (target.gate()) {
            case ALWAYS -> target.vulnerability();
            case ON_EXPOSURE -> exposed() ? target.vulnerability() : 1.0;
            case ON_ARMOR_BROKEN -> allArmorBroken() ? target.vulnerability() : 1.0;
        };
    }

    /**
     * 部位に攻撃を当てる。
     *
     * <p>破壊可能な部位（装甲）に当てたダメージは<b>その部位が受け止め、個体の体力には入らない</b>。
     * これがないと装甲を壊すことに代償が無く、「壊せば必ず得」になってしまう。
     * 装甲を壊すのは、体力を削る時間を先払いして恒久的な弱点を買う選択である。
     *
     * @param raw 素のダメージ量
     * @return 体力から引くべき量、装甲が受け止めた量、破壊が起きたか
     */
    public Result hit(String part, double raw, boolean enraged) {
        if (raw < 0) {
            throw new IllegalArgumentException("ダメージが負である: " + raw);
        }
        double multiplier = multiplier(part, enraged);
        if (multiplier == 0) {
            return new Result(part, 0, 0, false, true, 0);
        }
        double amount = raw * multiplier;
        Rig.Part target = rig.part(part);
        if (target.breakable()) {
            double total = takenBy(part) + amount;
            taken.put(part, total);
            boolean broke = total >= target.breakThreshold() * maxHealth;
            if (broke) {
                broken.add(part);
            }
            return new Result(part, 0, amount, broke, false, multiplier);
        }
        taken.merge(part, amount, Double::sum);
        return new Result(part, amount, 0, false, false, multiplier);
    }
}
