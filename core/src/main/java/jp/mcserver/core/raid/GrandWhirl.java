package jp.mcserver.core.raid;

import java.util.List;

/**
 * 特殊「全域大旋回」（`raid_species.md` §2）。
 *
 * <p>段階移行（体力3分の2・3分の1を切った瞬間）に1回ずつ、計2回だけ発動する
 * 移行専用のモーション。効果音のあと、空間斬撃と同じく<b>虚刃の衛士自身の頭上</b>に
 * まとめて出現し、生成ごとにランダムな待機（{@link SpecialWaveTiming}）を挟んで、
 * 空間斬撃と同じ追尾方式（{@link HomingDart}）で移動する。
 * 素材・本数・総移動距離は空間斬撃と別の値を持つ（実機で確認して分離した）。
 * <b>命中した瞬間（防がれたかに関わらず）その場で消える。</b>
 */
public final class GrandWhirl {

    private GrandWhirl() {
    }

    /**
     * 出現高度。個体の足元（地表面）からの相対値（ブロック）。空間斬撃と同じく、
     * 虚刃の衛士自身の頭上に出現する（実機で確認して、狙った相手の位置・出現高度の範囲から
     * 修正）。効果音のあと、この位置にまとめて出現する。
     */
    public static final double SPAWN_Y_OFFSET = SpatialSlash.SPAWN_Y_OFFSET;

    public static final double SWORD_LENGTH = 3.0;

    /** 移動速度（ブロック / 秒）。空間斬撃と同じ値。 */
    public static final double SPEED_BLOCKS_PER_SECOND = SpatialSlash.SPEED_BLOCKS_PER_SECOND;

    /** 総移動距離の上限（ブロック）。実機で確認して100→130へ、空間斬撃とは別の値にした。 */
    public static final double MAX_DISTANCE_BLOCKS = 130.0;

    /** 移動を始めてから、相手を追尾し続ける時間（tick）。空間斬撃と同じ値に揃えた。 */
    public static final int TRACKING_DURATION_TICKS = SpatialSlash.TRACKING_DURATION_TICKS;

    /** 狙いを相手の現在位置へ更新し直す間隔（tick）。空間斬撃と同じ値に揃えた。 */
    public static final int RETARGET_INTERVAL_TICKS = SpatialSlash.RETARGET_INTERVAL_TICKS;

    /** 発生間隔（tick）。 */
    public static final int WAVE_INTERVAL_TICKS = 5;

    /** 1回の発生で生成する本数。実機で確認して5→10へ、空間斬撃とは別の値にした。 */
    public static final int WAVE_SIZE = 10;

    /**
     * ノックバック（ブロック）。**仮の値。** ダメージ以外は指定が無いため、同じ追尾方式を
     * 持つ空間斬撃（5.0）をそのまま流用する。
     */
    public static final double KNOCKBACK_BLOCKS = SpatialSlash.KNOCKBACK_BLOCKS;

    /**
     * 素材とダメージの並び。生成した順に、この5つ（第三形態からは金のオノを加えた6つ）を
     * 繰り返し割り当てる（金14・銅15・鉄16・ダイヤモンド17・ネザライト18、オノ10）。
     */
    public enum Blade {
        GOLD(14.0),
        COPPER(15.0),
        IRON(16.0),
        DIAMOND(17.0),
        NETHERITE(18.0),
        AXE(GoldenAxe.DAMAGE);

        private final double damage;

        Blade(double damage) {
            this.damage = damage;
        }

        public double damage() {
            return damage;
        }

        public boolean isAxe() {
            return this == AXE;
        }
    }

    private static final List<Blade> SEQUENCE_SWORDS =
            List.of(Blade.GOLD, Blade.COPPER, Blade.IRON, Blade.DIAMOND, Blade.NETHERITE);

    private static final List<Blade> SEQUENCE_WITH_AXE =
            List.of(Blade.GOLD, Blade.COPPER, Blade.IRON, Blade.DIAMOND, Blade.NETHERITE, Blade.AXE);

    /**
     * 参加人数から生成する本数の合計。**第三形態からは金のオノが1つ加わり、周期が5→6になる
     * ぶん本数も増える**（参加人数×5 → 参加人数×(5+1)）。
     */
    public static int totalCount(int participants, boolean axePhase) {
        return participants * sequence(axePhase).size();
    }

    /** 生成した順（0始まり）から、割り当てる素材とダメージ。 */
    public static Blade bladeAt(int spawnIndex, boolean axePhase) {
        List<Blade> sequence = sequence(axePhase);
        return sequence.get(spawnIndex % sequence.size());
    }

    private static List<Blade> sequence(boolean axePhase) {
        return axePhase ? SEQUENCE_WITH_AXE : SEQUENCE_SWORDS;
    }

    /** 1tickあたりの歩幅（ブロック）。 */
    public static double blocksPerTick() {
        return HomingDart.blocksPerTick(SPEED_BLOCKS_PER_SECOND);
    }
}
