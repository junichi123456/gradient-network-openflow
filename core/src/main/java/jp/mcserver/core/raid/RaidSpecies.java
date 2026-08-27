package jp.mcserver.core.raid;

import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * レイド個体の定義（§12.6）。
 *
 * <p>体型と部位（{@link Rig}）は個体で共通、モーションと行動サイクルは<b>段階ごと</b>に持つ。
 * 同じ名前のモーションでも段階によってダメージ量が変わるため、段階が自分のモーションを持つ。
 */
public final class RaidSpecies {

    /**
     * 行動サイクル（§12.6）。
     *
     * <p>待機モーションが明けたあと一定時間だけ移動し、最も近いプレイヤーに対して
     * 攻撃モーションを取る。
     */
    public record Behavior(int idleTicks, int approachTicks, double approachBlocksPer20Ticks) {

        public Behavior {
            if (idleTicks < 0 || approachTicks < 0 || approachBlocksPer20Ticks <= 0) {
                throw new IllegalArgumentException("行動サイクルの指定が不正である");
            }
        }

        /** 標準の待機40tick・移動20tick。 */
        public static Behavior standard(double blocksPer20Ticks) {
            return new Behavior(MotionSpec.DEFAULT_IDLE_TICKS, 20, blocksPer20Ticks);
        }

        public double blocksPerTick() {
            return approachBlocksPer20Ticks / 20.0;
        }

        public double blocksPerSecond() {
            return approachBlocksPer20Ticks;
        }

        /** 移動時間のあいだに詰められる距離。 */
        public double approachDistance() {
            return blocksPerTick() * approachTicks;
        }
    }

    /**
     * 段階（§12.2 のギミック要件）。攻略の手順が段階ごとに変わる。
     *
     * @param healthThreshold    この段階に入る体力の割合（%）。降順に並べる
     * @param invulnerableUnless この条件を満たさないと有効打が入らない。無条件なら null
     */
    public static final class Phase {

        private final String name;
        private final int healthThreshold;
        private final Map<String, MotionSpec> motions = new LinkedHashMap<>();
        private final String gimmick;
        private final String invulnerableUnless;
        private final Behavior behavior;

        public Phase(String name, int healthThreshold, List<MotionSpec> motions, String gimmick,
                     String invulnerableUnless, Behavior behavior) {
            if (healthThreshold < 0 || healthThreshold > 100) {
                throw new IllegalArgumentException("体力の割合が範囲外である: " + healthThreshold);
            }
            if (motions.isEmpty()) {
                throw new IllegalArgumentException("モーションが1つもない: " + name);
            }
            this.name = name;
            this.healthThreshold = healthThreshold;
            for (MotionSpec motion : motions) {
                if (this.motions.put(motion.name(), motion) != null) {
                    throw new IllegalArgumentException("モーション名が重複している: " + motion.name());
                }
            }
            this.gimmick = gimmick;
            this.invulnerableUnless = invulnerableUnless;
            this.behavior = behavior;
        }

        /** 標準の行動サイクル（待機40tick・移動20tick）を用いる段階。 */
        public Phase(String name, int healthThreshold, List<MotionSpec> motions, String gimmick,
                     String invulnerableUnless, double blocksPer20Ticks) {
            this(name, healthThreshold, motions, gimmick, invulnerableUnless,
                    Behavior.standard(blocksPer20Ticks));
        }

        public String name() {
            return name;
        }

        public int healthThreshold() {
            return healthThreshold;
        }

        public String gimmick() {
            return gimmick;
        }

        public String invulnerableUnless() {
            return invulnerableUnless;
        }

        public Behavior behavior() {
            return behavior;
        }

        public List<String> motionNames() {
            return List.copyOf(motions.keySet());
        }

        public MotionSpec motion(String name) {
            MotionSpec motion = motions.get(name);
            if (motion == null) {
                throw new IllegalArgumentException(
                        "この段階では使わないモーションである: " + this.name + " / " + name);
            }
            return motion;
        }

        public List<MotionSpec> motions() {
            return List.copyOf(motions.values());
        }

        /** パリイ可能なモーション名。 */
        public List<String> parryableMotions() {
            return motions.values().stream().filter(MotionSpec::parryable)
                    .map(MotionSpec::name).toList();
        }

        /** 1サイクルの長さ。待機 → 移動 → 攻撃モーション。 */
        public int cycleTicks(String motionName) {
            return behavior.idleTicks() + behavior.approachTicks()
                    + motion(motionName).animation().durationTicks();
        }

        /** 最も重いモーションの最大ダメージ。 */
        public double heaviestMotionMaxDamage() {
            return motions.values().stream().mapToDouble(MotionSpec::maxDamage).max().orElse(0);
        }
    }

    private final String id;
    private final String displayName;
    private final long baseHealth;
    private final Rig rig;
    private final List<Phase> phases;

    public RaidSpecies(String id, String displayName, long baseHealth, Rig rig, List<Phase> phases) {
        if (baseHealth <= 0) {
            throw new IllegalArgumentException("体力が0以下である: " + baseHealth);
        }
        if (phases.isEmpty()) {
            throw new IllegalArgumentException("段階が1つもない");
        }
        this.id = id;
        this.displayName = displayName;
        this.baseHealth = baseHealth;
        this.rig = rig;
        this.phases = List.copyOf(phases);
        validate();
    }

    private void validate() {
        if (phases.get(0).healthThreshold() != 100) {
            throw new IllegalArgumentException("最初の段階は体力100%から始まる必要がある");
        }
        for (int i = 1; i < phases.size(); i++) {
            if (phases.get(i).healthThreshold() >= phases.get(i - 1).healthThreshold()) {
                throw new IllegalArgumentException("段階の閾値が降順でない");
            }
        }
        for (Phase phase : phases) {
            for (MotionSpec motion : phase.motions()) {
                motion.validateAgainst(rig);
            }
        }
    }

    public String id() {
        return id;
    }

    public String displayName() {
        return displayName;
    }

    public long baseHealth() {
        return baseHealth;
    }

    public Rig rig() {
        return rig;
    }

    public List<Phase> phases() {
        return phases;
    }

    /** 全段階に現れるモーション名。 */
    public Set<String> allMotionNames() {
        Set<String> names = new LinkedHashSet<>();
        phases.forEach(phase -> names.addAll(phase.motionNames()));
        return names;
    }

    /** 現在の体力割合に対応する段階。 */
    public Phase phaseAt(int healthPercent) {
        if (healthPercent < 0 || healthPercent > 100) {
            throw new IllegalArgumentException("体力の割合が範囲外である: " + healthPercent);
        }
        Phase current = phases.get(0);
        for (Phase phase : phases) {
            if (healthPercent <= phase.healthThreshold()) {
                current = phase;
            }
        }
        return current;
    }

    /** 参加人数を反映した体力（§12.3）。 */
    public long healthFor(int participants) {
        return baseHealth * jp.mcserver.core.Raid.difficulty(participants).healthPercent() / 100;
    }

    /** 動く部位が最も多いモーションでの、必要な更新間隔（§12.6）。 */
    public int requiredUpdateInterval(int viewers) {
        int maxMoving = 0;
        for (Phase phase : phases) {
            for (MotionSpec motion : phase.motions()) {
                maxMoving = Math.max(maxMoving, motion.animation().animatedParts().size());
            }
        }
        return MotionBudget.requiredInterval(maxMoving, viewers);
    }
}
