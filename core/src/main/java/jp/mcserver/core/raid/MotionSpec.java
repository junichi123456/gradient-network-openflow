package jp.mcserver.core.raid;

import java.util.List;
import java.util.Optional;

/**
 * 攻撃モーションの戦闘仕様（§12.6）。
 *
 * <p>{@link Animation} が部位の動きを持つのに対し、本レコードは
 * ダメージ判定・移動・ノックバック・妨害・パリイ可否といった戦闘上の性質を持つ。
 *
 * @param idleAfterTicks モーション後に差し込む待機モーションの長さ（tick）
 * @param parryable      パリイ可能か
 * @param tracksTarget   武器の先端が常に対象を向くか
 */
public record MotionSpec(String name, Animation animation, int idleAfterTicks, boolean parryable,
                         List<DamageWindow> damageWindows, Optional<Interrupt> interrupt,
                         Optional<Charge> charge, Optional<Orbit> orbit,
                         Optional<Knockback> knockback, Optional<AreaEffect> area,
                         boolean tracksTarget) {

    /** 標準の待機モーションの長さ（tick）。すべての個体で共通（§12.6）。 */
    public static final int DEFAULT_IDLE_TICKS = 40;

    /**
     * ダメージ量。範囲で与えると、その範囲の一様乱数になる。
     */
    public record Damage(double min, double max) {

        public Damage {
            if (min <= 0 || max < min) {
                throw new IllegalArgumentException("ダメージ量が不正である: " + min + "-" + max);
            }
        }

        /** 固定値。 */
        public static Damage of(double fixed) {
            return new Damage(fixed, fixed);
        }

        public boolean random() {
            return max > min;
        }

        public double average() {
            return (min + max) / 2;
        }

        @Override
        public String toString() {
            return random() ? min + "〜" + max : String.valueOf(min);
        }
    }

    /**
     * ダメージ判定が発生する区間。
     *
     * @param part 判定を持つ部位
     */
    public record DamageWindow(String part, int fromTick, int toTick, Damage damage) {

        public DamageWindow {
            if (fromTick < 0 || toTick < fromTick) {
                throw new IllegalArgumentException("判定区間が不正である: " + fromTick + "-" + toTick);
            }
        }

        public int durationTicks() {
            return toTick - fromTick;
        }
    }

    /**
     * 妨害。指定の部位に攻撃が当たるとモーションが中断され、待機モーションが入る。
     *
     * @param beforeTick この tick までに当たれば成立する
     * @param idleTicks  中断後に入る待機モーションの長さ
     */
    public record Interrupt(String part, int beforeTick, int idleTicks) {

        public Interrupt {
            if (beforeTick <= 0 || idleTicks < 0) {
                throw new IllegalArgumentException("妨害の指定が不正である");
            }
        }
    }

    /** 直線の突進。 */
    public record Charge(double blocks, int perTicks) {

        public Charge {
            if (blocks <= 0 || perTicks <= 0) {
                throw new IllegalArgumentException("突進の指定が不正である");
            }
        }

        public double blocksPerTick() {
            return blocks / perTicks;
        }

        public double blocksPerSecond() {
            return blocksPerTick() * 20;
        }
    }

    /** 円周上の移動。 */
    public record Orbit(double diameterBlocks, double laps, int ticks) {

        public Orbit {
            if (diameterBlocks <= 0 || laps <= 0 || ticks <= 0) {
                throw new IllegalArgumentException("回旋の指定が不正である");
            }
        }

        /** 移動距離（ブロック）。 */
        public double pathLength() {
            return Math.PI * diameterBlocks * laps;
        }

        public double blocksPerTick() {
            return pathLength() / ticks;
        }

        public double blocksPerSecond() {
            return blocksPerTick() * 20;
        }
    }

    /** ノックバック。 */
    public record Knockback(double upBlocks, double backBlocks) {}

    /** 範囲攻撃。 */
    public record AreaEffect(double radiusBlocks, double heightBlocks, Damage damage) {

        public AreaEffect {
            if (radiusBlocks <= 0 || heightBlocks <= 0) {
                throw new IllegalArgumentException("範囲の指定が不正である");
            }
        }
    }

    public MotionSpec {
        if (idleAfterTicks < 0) {
            throw new IllegalArgumentException("待機モーションが負である: " + idleAfterTicks);
        }
        damageWindows = List.copyOf(damageWindows);
        for (DamageWindow window : damageWindows) {
            if (window.toTick() > animation.durationTicks()) {
                throw new IllegalArgumentException(
                        "判定区間がモーションの長さを超えている: " + name + " / " + window.part());
            }
        }
        interrupt.ifPresent(value -> {
            if (value.beforeTick() > animation.durationTicks()) {
                throw new IllegalArgumentException("妨害の期限がモーションの長さを超えている: " + name);
            }
        });
    }

    /** 標準の待機モーションを伴う、追加要素のないモーション。 */
    public static MotionSpec simple(Animation animation) {
        return new MotionSpec(animation.name(), animation, DEFAULT_IDLE_TICKS, false,
                List.of(), Optional.empty(), Optional.empty(), Optional.empty(),
                Optional.empty(), Optional.empty(), false);
    }

    /** 骨格と整合するか検証する。 */
    public void validateAgainst(Rig rig) {
        animation.validateAgainst(rig);
        for (DamageWindow window : damageWindows) {
            rig.part(window.part());
        }
        interrupt.ifPresent(value -> rig.part(value.part()));
    }

    /** モーション開始から待機モーション終了までの合計 tick。 */
    public int totalTicks() {
        return animation.durationTicks() + idleAfterTicks;
    }

    /** すべての判定が命中した場合の合計ダメージ（範囲攻撃を含む、平均値）。 */
    public double totalDamageIfAllHit() {
        double total = 0;
        for (DamageWindow window : damageWindows) {
            total += window.damage().average();
        }
        total += area.map(a -> a.damage().average()).orElse(0.0);
        return total;
    }

    /** 最大ダメージ（乱数の上限で命中した場合）。 */
    public double maxDamage() {
        double total = 0;
        for (DamageWindow window : damageWindows) {
            total += window.damage().max();
        }
        total += area.map(a -> a.damage().max()).orElse(0.0);
        return total;
    }
}
