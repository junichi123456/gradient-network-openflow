package jp.mcserver.core.raid;

import java.util.Random;

/**
 * 特殊系統（浮遊する剣・オノ）が、生成されてから行動を始めるまでの待機（tick）
 * （`raid_species.md` §2「軌道の修正」）。
 *
 * <p>4種（降り注ぐ刃・空間斬撃・串刺し・全域大旋回）すべてで共通のこの範囲から、
 * 生成のたびにランダムに1つ選ぶ——毎回同じ拍子で来ないようにするための、実機の指摘による
 * 修正である。**金のオノだけは固定値**（{@link #AXE_WAIT_TICKS}）を使う。読みやすさを
 * 優先し、オノが来るときだけはっきり分かるようにするためである。
 */
public final class SpecialWaveTiming {

    private SpecialWaveTiming() {
    }

    public static final int MIN_WAIT_TICKS = 30;

    public static final int MAX_WAIT_TICKS = 80;

    /** 金のオノの待機（tick）。ランダムにせず、固定値。 */
    public static final int AXE_WAIT_TICKS = 90;

    /** [{@link #MIN_WAIT_TICKS}, {@link #MAX_WAIT_TICKS}] からランダムに1つ選ぶ。 */
    public static int randomWaitTicks(Random random) {
        return MIN_WAIT_TICKS + random.nextInt(MAX_WAIT_TICKS - MIN_WAIT_TICKS + 1);
    }
}
