package jp.mcserver.core.raid;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * レイド個体の定義（§12.6）。
 *
 * <p>体型と部位（{@link Rig}）、攻撃モーション（{@link Animation}）、段階（{@link Phase}）を持つ。
 * バニラのモブを流用しないため、これらはすべて個体ごとに定める。
 */
public final class RaidSpecies {

    /**
     * 段階（§12.2 のギミック要件）。攻略の手順が段階ごとに変わる。
     *
     * @param name                段階名
     * @param healthThreshold     この段階に入る体力の割合（%）。降順に並べる
     * @param animations          この段階で使うモーション名
     * @param gimmick             攻略手順の要点
     * @param invulnerableUnless  この条件を満たさないと有効打が入らない。無条件なら null
     */
    public record Phase(String name, int healthThreshold, List<String> animations,
                        String gimmick, String invulnerableUnless) {

        public Phase {
            if (healthThreshold < 0 || healthThreshold > 100) {
                throw new IllegalArgumentException("体力の割合が範囲外である: " + healthThreshold);
            }
            animations = List.copyOf(animations);
        }
    }

    private final String id;
    private final String displayName;
    private final long baseHealth;
    private final Rig rig;
    private final Map<String, Animation> animations = new LinkedHashMap<>();
    private final List<Phase> phases;

    public RaidSpecies(String id, String displayName, long baseHealth, Rig rig,
                       List<Animation> animations, List<Phase> phases) {
        if (baseHealth <= 0) {
            throw new IllegalArgumentException("体力が0以下である: " + baseHealth);
        }
        if (animations.isEmpty()) {
            throw new IllegalArgumentException("モーションが1つもない");
        }
        if (phases.isEmpty()) {
            throw new IllegalArgumentException("段階が1つもない");
        }
        this.id = id;
        this.displayName = displayName;
        this.baseHealth = baseHealth;
        this.rig = rig;
        for (Animation animation : animations) {
            animation.validateAgainst(rig);
            if (this.animations.put(animation.name(), animation) != null) {
                throw new IllegalArgumentException("モーション名が重複している: " + animation.name());
            }
        }
        this.phases = List.copyOf(phases);
        validatePhases();
    }

    private void validatePhases() {
        if (phases.get(0).healthThreshold() != 100) {
            throw new IllegalArgumentException("最初の段階は体力100%から始まる必要がある");
        }
        for (int i = 1; i < phases.size(); i++) {
            if (phases.get(i).healthThreshold() >= phases.get(i - 1).healthThreshold()) {
                throw new IllegalArgumentException("段階の閾値が降順でない");
            }
        }
        for (Phase phase : phases) {
            for (String animation : phase.animations()) {
                if (!animations.containsKey(animation)) {
                    throw new IllegalArgumentException(
                            "存在しないモーションを参照している: " + phase.name() + " → " + animation);
                }
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

    public Animation animation(String name) {
        Animation animation = animations.get(name);
        if (animation == null) {
            throw new IllegalArgumentException("存在しないモーションである: " + name);
        }
        return animation;
    }

    public List<String> animationNames() {
        return new ArrayList<>(animations.keySet());
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
        for (Animation animation : animations.values()) {
            maxMoving = Math.max(maxMoving, animation.animatedParts().size());
        }
        return MotionBudget.requiredInterval(maxMoving, viewers);
    }
}
