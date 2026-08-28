package jp.mcserver.core.raid;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Random;

/**
 * 状況に応じたモーションの選択（§12.6）。
 *
 * <p>固定順で技を回すと、二度目の挑戦から先は暗記になる。ここでは
 * <b>対象との距離</b>と<b>近接圏内のプレイヤー数</b>で候補を絞り、
 * 出したばかりの技を再選択しないよう待ち時間を持たせたうえで、重みつきで選ぶ。
 *
 * <p>候補が空になる状況でも必ず1つ返す。緩め方には順序があり、
 * <b>距離帯を守ることを最優先</b>にする。連続の上限 → 待ち時間 → 距離、の順に外す。
 * 距離を先に外すと、間合いの外で近接技を空振りする挙動になってしまう。
 */
public final class MotionSelector {

    /** 選択の状況。 */
    public record Situation(double distance, int surrounding, boolean enraged) {

        public Situation {
            if (distance < 0 || surrounding < 0) {
                throw new IllegalArgumentException("状況の指定が不正である");
            }
        }
    }

    /** 選択の結果。緩和が起きたかを含む。 */
    public record Choice(MotionSpec motion, boolean relaxed) {}

    private final Random random;
    private final Map<String, Integer> readyAt = new HashMap<>();
    private String last;
    private int consecutive;

    public MotionSelector(long seed) {
        this.random = new Random(seed);
    }

    /** 再現性が不要な実行時用。 */
    public MotionSelector() {
        this.random = new Random();
    }

    /**
     * 次のモーションを選ぶ。
     *
     * @param now 現在の tick。待ち時間の判定に使う
     */
    public Choice select(RaidSpecies.Phase phase, Situation situation, int now) {
        // 緩める順序。距離帯は最後まで守る
        boolean[][] passes = {
            {false, false, false},
            {true, false, false},
            {true, true, false},
            {true, true, true},
        };
        List<MotionSpec> candidates = List.of();
        boolean relaxed = false;
        for (int i = 0; i < passes.length; i++) {
            candidates = eligible(phase.motions(), situation, now,
                    passes[i][0], passes[i][1], passes[i][2]);
            if (!candidates.isEmpty()) {
                relaxed = i > 0;
                break;
            }
        }
        if (candidates.isEmpty()) {
            candidates = new ArrayList<>(phase.motions());
            relaxed = true;
        }
        MotionSpec chosen = pick(candidates);
        record(chosen, now);
        return new Choice(chosen, relaxed);
    }

    private List<MotionSpec> eligible(List<MotionSpec> motions, Situation situation, int now,
                                      boolean ignoreConsecutive, boolean ignoreCooldown,
                                      boolean ignoreRange) {
        List<MotionSpec> result = new ArrayList<>();
        for (MotionSpec motion : motions) {
            MotionSpec.Usage usage = motion.usage();
            if (usage.enragedOnly() && !situation.enraged()) {
                continue;
            }
            if (!ignoreRange
                    && !usage.matches(situation.distance(), situation.surrounding(),
                            situation.enraged())) {
                continue;
            }
            if (!ignoreCooldown && now < readyAt.getOrDefault(motion.name(), 0)) {
                continue;
            }
            if (!ignoreConsecutive && motion.name().equals(last)
                    && consecutive >= usage.maxConsecutive()) {
                continue;
            }
            result.add(motion);
        }
        return result;
    }

    private MotionSpec pick(List<MotionSpec> candidates) {
        int total = candidates.stream().mapToInt(motion -> motion.usage().weight()).sum();
        int roll = random.nextInt(total);
        int cursor = 0;
        for (MotionSpec motion : candidates) {
            cursor += motion.usage().weight();
            if (roll < cursor) {
                return motion;
            }
        }
        return candidates.get(candidates.size() - 1);
    }

    private void record(MotionSpec motion, int now) {
        if (motion.name().equals(last)) {
            consecutive++;
        } else {
            last = motion.name();
            consecutive = 1;
        }
        readyAt.put(motion.name(), now + motion.usage().cooldownTicks());
    }

    /** 直前に選んだモーション名。まだ選んでいなければ null。 */
    public String last() {
        return last;
    }

    /** 直前のモーションが連続した回数。 */
    public int consecutive() {
        return consecutive;
    }

    /** そのモーションが待ち時間中か。 */
    public boolean onCooldown(String name, int now) {
        return now < readyAt.getOrDefault(name, 0);
    }

    /** 形態移行でやり直す。段階が変わればモーションの一覧も変わる。 */
    public void reset() {
        readyAt.clear();
        last = null;
        consecutive = 0;
    }
}
