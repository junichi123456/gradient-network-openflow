package jp.mcserver.core.raid;

import java.util.List;

/**
 * 特殊「全域大旋回」（`raid_species.md` §2）。
 *
 * <p>段階移行（体力3分の2・3分の1を切った瞬間）に1回ずつ、計2回だけ発動する
 * 移行専用のモーション。空間斬撃と同じ追尾方式（{@link HomingDart}、実機で確認して
 * 円弧から直した）を使うが、戦場全域を飛び回るぶん出現高度・素材が異なる。
 */
public final class GrandWhirl {

    private GrandWhirl() {
    }

    /**
     * 出現高度の範囲。個体の召喚位置の足元Yからの相対値（ブロック）。この範囲から
     * 個体ごとにばらけて出現する。
     *
     * <p><b>絶対座標のYではなく、召喚位置基準。</b>足元Y=1の会場を基準に決めた値
     * （Y=10〜15、空間斬撃と同じ考え方）をオフセットへ直した（10−1=9、15−1=14）。
     * 実際の高度は {@code 召喚位置の足元Y + この値}。
     */
    public static final double SPAWN_Y_MIN_OFFSET = 9.0;

    public static final double SPAWN_Y_MAX_OFFSET = 14.0;

    public static final double SWORD_LENGTH = 3.0;

    /** 移動速度（ブロック / 秒）。空間斬撃と同じ値に揃えた。 */
    public static final double SPEED_BLOCKS_PER_SECOND = SpatialSlash.SPEED_BLOCKS_PER_SECOND;

    /** 総移動距離の上限（ブロック）。空間斬撃と同じ値に揃えた。 */
    public static final double MAX_DISTANCE_BLOCKS = SpatialSlash.MAX_DISTANCE_BLOCKS;

    /** 召喚してから移動を始めるまでの待機（tick）。空間斬撃（5tick）より長い。 */
    public static final int START_DELAY_TICKS = 10;

    /** 移動を始めてから、相手を追尾し続ける時間（tick）。空間斬撃と同じ値に揃えた。 */
    public static final int TRACKING_DURATION_TICKS = SpatialSlash.TRACKING_DURATION_TICKS;

    /** 狙いを相手の現在位置へ更新し直す間隔（tick）。空間斬撃と同じ値に揃えた。 */
    public static final int RETARGET_INTERVAL_TICKS = SpatialSlash.RETARGET_INTERVAL_TICKS;

    /** 発生間隔（tick）。実機で確認して「一斉に生成」から空間斬撃と同じ波方式へ直した。 */
    public static final int WAVE_INTERVAL_TICKS = SpatialSlash.WAVE_INTERVAL_TICKS;

    public static final int WAVE_SIZE = SpatialSlash.WAVE_SIZE;

    /**
     * ノックバック（ブロック）。**仮の値。** ダメージ以外は指定が無いため、同じ追尾方式を
     * 持つ空間斬撃（5.0）をそのまま流用する。
     */
    public static final double KNOCKBACK_BLOCKS = SpatialSlash.KNOCKBACK_BLOCKS;

    /**
     * 素材とダメージの並び。生成した順に、この5つを繰り返し割り当てる
     * （金14・銅15・鉄16・ダイヤモンド17・ネザライト18）。
     */
    public enum Blade {
        GOLD(14.0),
        COPPER(15.0),
        IRON(16.0),
        DIAMOND(17.0),
        NETHERITE(18.0);

        private final double damage;

        Blade(double damage) {
            this.damage = damage;
        }

        public double damage() {
            return damage;
        }
    }

    private static final List<Blade> SEQUENCE = List.of(Blade.values());

    /**
     * 参加人数から生成する本数の合計。実機で確認して、空間斬撃と同じ式
     * （参加人数*4 → 参加人数*3）・同じ波方式へ揃えた。
     */
    public static int totalCount(int participants) {
        return participants * 3;
    }

    /** 生成した順（0始まり）から、割り当てる素材とダメージ。 */
    public static Blade bladeAt(int spawnIndex) {
        return SEQUENCE.get(spawnIndex % SEQUENCE.size());
    }

    /** 1tickあたりの歩幅（ブロック）。 */
    public static double blocksPerTick() {
        return HomingDart.blocksPerTick(SPEED_BLOCKS_PER_SECOND);
    }
}
