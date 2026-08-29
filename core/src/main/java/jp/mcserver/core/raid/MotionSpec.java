package jp.mcserver.core.raid;

import java.util.List;
import java.util.Optional;

/**
 * 攻撃モーションの戦闘仕様（§12.6）。
 *
 * <p>{@link Animation} が部位の動きを持つのに対し、本レコードは
 * ダメージ判定・移動・ノックバック・妨害・パリイ可否といった戦闘上の性質を持つ。
 *
 * @param idleAfter      モーション後に差し込む待機モーションの長さ
 * @param parry          パリイの成立条件。持たないモーションは空
 * @param tracksTarget   武器の先端が常に対象を向くか
 * @param usage          どんな状況で選ばれるモーションか（§12.6）
 */
public record MotionSpec(String name, Animation animation, Idle idleAfter,
                         Optional<Parry> parry,
                         List<DamageWindow> damageWindows, Optional<Interrupt> interrupt,
                         Optional<Charge> charge, Optional<Orbit> orbit,
                         Optional<Knockback> knockback, Optional<AreaEffect> area,
                         boolean tracksTarget, Usage usage, Optional<Leap> leap) {


    /**
     * 使用条件（§12.6）。モーションを固定順で回すと攻略が暗記になるため、
     * <b>距離と囲まれ具合</b>で候補を絞り、重みで選ぶ。
     *
     * @param minRange       この距離以上で使う（ブロック）
     * @param maxRange       この距離以下で使う（ブロック）
     * @param minSurrounding 近接圏内のプレイヤーがこの数以上のときだけ使う
     * @param weight         候補が複数あるときの相対的な出やすさ
     * @param cooldownTicks  一度使ったあと、この時間は選ばれない
     * @param maxConsecutive 同じモーションを連続で使える上限
     * @param enragedOnly    激昂中にだけ使うか
     */
    public record Usage(double minRange, double maxRange, int minSurrounding, int weight,
                        int cooldownTicks, int maxConsecutive, boolean enragedOnly) {

        /** 「囲まれている」と判定する半径（ブロック）。 */
        public static final double CROWD_RADIUS = 6.0;

        /** 条件を持たないモーション。 */
        public static final Usage ANY = new Usage(0, 64, 0, 10, 0, 2, false);

        public Usage {
            if (minRange < 0 || maxRange < minRange) {
                throw new IllegalArgumentException("距離の指定が不正である: " + minRange + "-" + maxRange);
            }
            if (minSurrounding < 0 || weight <= 0 || cooldownTicks < 0 || maxConsecutive < 1) {
                throw new IllegalArgumentException("使用条件の指定が不正である");
            }
        }

        /** 距離と重みだけを指定する。 */
        public static Usage at(double minRange, double maxRange, int weight, int cooldownTicks) {
            return new Usage(minRange, maxRange, 0, weight, cooldownTicks, 1, false);
        }

        /** 囲まれているときに出す技。 */
        public static Usage crowd(double maxRange, int minSurrounding, int weight, int cooldownTicks) {
            return new Usage(0, maxRange, minSurrounding, weight, cooldownTicks, 1, false);
        }

        /** 距離と囲まれ具合の条件を満たすか。 */
        public boolean matches(double distance, int surrounding, boolean enraged) {
            if (enragedOnly && !enraged) {
                return false;
            }
            return distance >= minRange && distance <= maxRange && surrounding >= minSurrounding;
        }
    }

    /**
     * モーション後に差し込む待機モーションの長さ（§12.6）。
     *
     * <p>幅を持たせられるのは、大技のあとの隙を毎回同じ長さにしないためである。
     * 同じ長さだと、隙の終わりを秒読みできてしまう。
     */
    public record Idle(int minTicks, int maxTicks) {

        public Idle {
            if (minTicks < 0 || maxTicks < minTicks) {
                throw new IllegalArgumentException("待機モーションの長さが不正である: "
                        + minTicks + "-" + maxTicks);
            }
        }

        /** 長さの決まった待機。 */
        public static Idle of(int ticks) {
            return new Idle(ticks, ticks);
        }

        /** 幅のある待機。 */
        public static Idle between(int minTicks, int maxTicks) {
            return new Idle(minTicks, maxTicks);
        }

        public boolean fixed() {
            return minTicks == maxTicks;
        }

        /** 幅の中から1つ選ぶ。 */
        public int pick(java.util.random.RandomGenerator random) {
            return fixed() ? minTicks : minTicks + random.nextInt(maxTicks - minTicks + 1);
        }

        @Override
        public String toString() {
            return fixed() ? minTicks + "tick" : minTicks + "〜" + maxTicks + "tick";
        }
    }

    /**
     * 大きく跳んで着地する移動（§12.6）。
     *
     * <p>水平方向は等速、垂直方向は放物線を描く。着地点は戦場の中心であり、
     * <b>個体の位置を戦場の中心へ戻す手段を兼ねる</b>。
     *
     * @param startTick   跳び上がる tick
     * @param flightTicks 滞空時間
     * @param apexBlocks  弧の頂点の高さ（跳び上がる位置からの相対）
     */
    public record Leap(int startTick, int flightTicks, double apexBlocks) {

        public Leap {
            if (startTick < 0 || flightTicks <= 0 || apexBlocks <= 0) {
                throw new IllegalArgumentException("跳躍の指定が不正である");
            }
        }

        /** 着地する tick。 */
        public int landingTick() {
            return startTick + flightTicks;
        }

        /** 跳び上がってからの経過に対する進み具合（0〜1）。 */
        public double progress(int tickSinceStart) {
            return Math.max(0, Math.min(1.0, (double) tickSinceStart / flightTicks));
        }

        /**
         * 弧の高さ（着地点と跳び上がり点を結んだ線からの持ち上がり）。
         *
         * <p>頂点で {@code apexBlocks} に達し、両端で 0 になる放物線である。
         */
        public double archHeight(int tickSinceStart) {
            double t = progress(tickSinceStart);
            return apexBlocks * 4 * t * (1 - t);
        }
    }

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

    /**
     * 直線の突進（§12.6）。
     *
     * <p>等速ではなく<b>後ずさり → 加速 → 一定距離を走り切る</b>という形をとる。
     * 後ずさりは予備動作として見せる時間であり、加速は避ける方向を決める時間である。
     * 走る距離を決めておくのは、<b>途中で止まらないことを保証する</b>ためである。
     *
     * @param startTick             モーション開始から突進の一連が始まる tick
     * @param backstepBlocks        突進の前に後ずさりする距離
     * @param backstepTicks         後ずさりに要する tick
     * @param startSpeedPer20Ticks  走り出しの速度（20tickあたりのブロック数）
     * @param topSpeedPer20Ticks    到達する速度（20tickあたりのブロック数）
     * @param accelerationTicks     走り出しから到達速度までの tick
     * @param distanceBlocks        開始位置から走る距離
     */
    public record Charge(int startTick, double backstepBlocks, int backstepTicks,
                         double startSpeedPer20Ticks, double topSpeedPer20Ticks,
                         int accelerationTicks, double distanceBlocks) {

        public Charge {
            if (startTick < 0 || backstepBlocks < 0 || backstepTicks < 0) {
                throw new IllegalArgumentException("突進の予備動作の指定が不正である");
            }
            if (startSpeedPer20Ticks < 0 || topSpeedPer20Ticks < startSpeedPer20Ticks) {
                throw new IllegalArgumentException("突進の速度の指定が不正である");
            }
            if (accelerationTicks < 0 || distanceBlocks <= 0) {
                throw new IllegalArgumentException("突進の距離の指定が不正である");
            }
            if (backstepBlocks > 0 && backstepTicks == 0) {
                throw new IllegalArgumentException("後ずさりの時間が0である");
            }
        }

        /** 加速せず一定速度で走る突進。 */
        public static Charge steady(int startTick, double speedPer20Ticks, double distanceBlocks) {
            return new Charge(startTick, 0, 0, speedPer20Ticks, speedPer20Ticks, 0,
                    distanceBlocks);
        }

        /** 後ずさりが終わり、走り出す tick。 */
        public int runFromTick() {
            return startTick + backstepTicks;
        }

        /** 後ずさりの1tickあたりの距離。 */
        public double backstepPerTick() {
            return backstepTicks == 0 ? 0 : backstepBlocks / backstepTicks;
        }

        /**
         * 走り出しから数えた tick 時点の速度（1tickあたりのブロック数）。
         *
         * @param tickSinceRun 走り出しからの経過 tick（1以上）
         */
        public double speedAt(int tickSinceRun) {
            if (tickSinceRun <= 0) {
                return 0;
            }
            double start = startSpeedPer20Ticks / 20.0;
            double top = topSpeedPer20Ticks / 20.0;
            if (accelerationTicks <= 0) {
                return top;
            }
            double ratio = Math.min(1.0, (double) tickSinceRun / accelerationTicks);
            return start + (top - start) * ratio;
        }

        /** 走り出しから指定 tick までに進む距離。走る距離で頭打ちになる。 */
        public double distanceAfter(int tickSinceRun) {
            double total = 0;
            for (int t = 1; t <= tickSinceRun; t++) {
                total += speedAt(t);
                if (total >= distanceBlocks) {
                    return distanceBlocks;
                }
            }
            return total;
        }

        /** 走り切るのに要する tick。 */
        public int runTicks() {
            double total = 0;
            for (int t = 1; t <= 400; t++) {
                total += speedAt(t);
                if (total >= distanceBlocks) {
                    return t;
                }
            }
            throw new IllegalStateException("走り切れない突進である: " + distanceBlocks);
        }

        /** 突進の一連（後ずさりを含む）が終わる tick。 */
        public int endTick() {
            return runFromTick() + runTicks();
        }

        /** 到達速度（毎秒のブロック数）。 */
        public double topBlocksPerSecond() {
            return topSpeedPer20Ticks;
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

    /**
     * パリイ（§12.6）。
     *
     * <p>盾で受けることでは成立しない。<b>その区間に個体へ与えたダメージ</b>で判定する。
     * 受け身の防御ではなく、踏み込んで打ち返すことを成立条件に置く。
     *
     * <p>必要なダメージは<b>パリイに成功するたびに増える</b>。1回目は軽く、
     * 繰り返すほど重くなる。個体の体力に比例させないのは、人数が増えるほど
     * 成立しにくくなる形を避けるためである。回数で重くするなら、
     * 少人数でも大人数でも「何度も止め続けることはできない」という同じ制約になる。
     *
     * @param fromTick         判定が始まる tick
     * @param toTick           判定が終わる tick
     * @param baseDamage       1回目の成立に要する累積ダメージ
     * @param increasePerParry パリイ1回ごとに増える必要ダメージ
     */
    public record Parry(int fromTick, int toTick, double baseDamage, double increasePerParry) {

        public Parry {
            if (fromTick < 0 || toTick < fromTick) {
                throw new IllegalArgumentException("パリイの区間が不正である");
            }
            if (baseDamage <= 0 || increasePerParry < 0) {
                throw new IllegalArgumentException("パリイの必要ダメージが不正である");
            }
        }

        public boolean covers(int tick) {
            return tick >= fromTick && tick <= toTick;
        }

        /**
         * 成立に要するダメージ量。
         *
         * @param parriesSoFar その戦闘でこれまでに成功したパリイの回数
         */
        public double requiredDamage(int parriesSoFar) {
            if (parriesSoFar < 0) {
                throw new IllegalArgumentException("パリイの回数が負である: " + parriesSoFar);
            }
            return baseDamage + increasePerParry * parriesSoFar;
        }
    }

    /** ノックバック。 */
    public record Knockback(double upBlocks, double backBlocks) {}

    /**
     * 範囲攻撃。
     *
     * @param travelSpeedPerSecond 波が広がる速さ（毎秒のブロック数）。
     *                             0 なら<b>即時</b>に全範囲へ届く
     */
    public record AreaEffect(double radiusBlocks, double heightBlocks, Damage damage,
                             double travelSpeedPerSecond) {

        public AreaEffect {
            if (radiusBlocks <= 0 || heightBlocks <= 0) {
                throw new IllegalArgumentException("範囲の指定が不正である");
            }
            if (travelSpeedPerSecond < 0) {
                throw new IllegalArgumentException("伝播の速さが負である");
            }
        }

        /** 即時に全範囲へ届く範囲攻撃。 */
        public AreaEffect(double radiusBlocks, double heightBlocks, Damage damage) {
            this(radiusBlocks, heightBlocks, damage, 0);
        }

        /** 即時か。 */
        public boolean instant() {
            return travelSpeedPerSecond == 0;
        }

        /** 発生からの経過 tick 時点で波が届いている半径。 */
        public double radiusAt(int ticksSince) {
            if (instant()) {
                return radiusBlocks;
            }
            return Math.min(radiusBlocks, travelSpeedPerSecond / 20.0 * Math.max(0, ticksSince));
        }

        /** 波が端まで届くのに要する tick。 */
        public int ticksToFullRadius() {
            if (instant()) {
                return 0;
            }
            return (int) Math.ceil(radiusBlocks / (travelSpeedPerSecond / 20.0));
        }
    }

    public MotionSpec {
        if (usage == null) {
            throw new IllegalArgumentException("使用条件が null である: " + name);
        }
        if (idleAfter == null) {
            throw new IllegalArgumentException("待機モーションの指定が null である: " + name);
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
        parry.ifPresent(value -> {
            if (value.toTick() > animation.durationTicks()) {
                throw new IllegalArgumentException("パリイの区間がモーションの長さを超えている: " + name);
            }
        });
        leap.ifPresent(value -> {
            if (value.landingTick() > animation.durationTicks()) {
                throw new IllegalArgumentException(
                        "着地する前にモーションが終わる: " + name + " / 着地 " + value.landingTick()
                                + "tick、モーション " + animation.durationTicks() + "tick");
            }
        });
        charge.ifPresent(value -> {
            if (value.endTick() > animation.durationTicks()) {
                throw new IllegalArgumentException(
                        "突進が走り切る前にモーションが終わる: " + name + " / 必要 " + value.endTick()
                                + "tick、モーション " + animation.durationTicks() + "tick");
            }
        });
    }

    /** 使用条件を指定しないモーション。条件は {@link Usage#ANY} になる。 */
    public MotionSpec(String name, Animation animation, Idle idleAfter,
                      Optional<Parry> parry,
                      List<DamageWindow> damageWindows, Optional<Interrupt> interrupt,
                      Optional<Charge> charge, Optional<Orbit> orbit,
                      Optional<Knockback> knockback, Optional<AreaEffect> area,
                      boolean tracksTarget) {
        this(name, animation, idleAfter, parry, damageWindows, interrupt, charge, orbit,
                knockback, area, tracksTarget, Usage.ANY, Optional.empty());
    }

    /** 使用条件だけを指定するモーション。 */
    public MotionSpec(String name, Animation animation, Idle idleAfter, Optional<Parry> parry,
                      List<DamageWindow> damageWindows, Optional<Interrupt> interrupt,
                      Optional<Charge> charge, Optional<Orbit> orbit,
                      Optional<Knockback> knockback, Optional<AreaEffect> area,
                      boolean tracksTarget, Usage usage) {
        this(name, animation, idleAfter, parry, damageWindows, interrupt, charge, orbit,
                knockback, area, tracksTarget, usage, Optional.empty());
    }

    /** パリイできるモーションか。 */
    public boolean parryable() {
        return parry.isPresent();
    }

    /** 使用条件を差し替えた同じモーション。 */
    public MotionSpec using(Usage value) {
        return new MotionSpec(name, animation, idleAfter, parry, damageWindows, interrupt,
                charge, orbit, knockback, area, tracksTarget, value, leap);
    }

    /** 標準の待機モーションを伴う、追加要素のないモーション。 */
    public static MotionSpec simple(Animation animation) {
        return new MotionSpec(animation.name(), animation, Idle.of(DEFAULT_IDLE_TICKS),
                Optional.empty(),
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

    /** モーション開始から待機モーション終了までの合計 tick（待機は最短で数える）。 */
    public int totalTicks() {
        return animation.durationTicks() + idleAfter.minTicks();
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
