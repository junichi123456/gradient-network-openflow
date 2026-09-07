package jp.mcserver.core.raid;

import java.util.ArrayList;
import java.util.List;
import java.util.Random;

/**
 * 特殊「降り注ぐ刃」（`raid_species.md` §2、第二形態から）。
 *
 * <p>Y=15 から自由落下する浮遊剣。プレイヤーの真上を中心に半径3ブロック以内の
 * どこかへ、重複しない位置で降り注ぐ。地面に刺さると0.5ブロック埋まり、
 * 高さ0.4ブロック・半径1ブロックの衝撃波を残す。
 */
public final class SwordRain {

    private SwordRain() {
    }

    /** 出現高度（Y）。 */
    public static final double SPAWN_Y = 15.0;

    /** 出現位置の、狙うプレイヤーの真上を中心とした半径（ブロック）。 */
    public static final double SPAWN_RADIUS = 3.0;

    /** 出現位置どうしを離す最小距離（ブロック）。「重複なし」の実装（仮の値）。 */
    public static final double MIN_SEPARATION = 0.6;

    /** 剣の長さ（ブロック）。 */
    public static final double SWORD_LENGTH = 2.0;

    /** 剣本体に当たったときのダメージ。 */
    public static final double BLADE_DAMAGE = 18.0;

    /** 着地の衝撃波のダメージ。 */
    public static final double SHOCKWAVE_DAMAGE = 10.0;

    /** 着地の衝撃波の半径（ブロック）。 */
    public static final double SHOCKWAVE_RADIUS = 1.0;

    /** 着地の衝撃波の高さ（ブロック）。 */
    public static final double SHOCKWAVE_HEIGHT = 0.4;

    /** 着地したとき地面に埋まる深さ（ブロック）。 */
    public static final double EMBED_DEPTH = 0.5;

    /** ノックバック（ブロック）。 */
    public static final double KNOCKBACK_BLOCKS = 1.0;

    /** 発生間隔（tick）。この間隔ごとに {@link #WAVE_SIZE} 本ずつ生成する。 */
    public static final int WAVE_INTERVAL_TICKS = 5;

    /** 1回の発生で生成する本数。 */
    public static final int WAVE_SIZE = 3;

    /**
     * 自由落下の加速度（ブロック / tick^2）と最高速度（ブロック / tick）。
     *
     * <p>{@code RaidBossBase} の登場演出（レイド次元へ落下してくる場面）と同じ近似式を使う。
     * 見た目の落下の速さを個体・浮遊剣で揃えるための流用であり、物理的な厳密さを狙ったもの
     * ではない。
     */
    public static final double GRAVITY_ACCEL = 0.08;

    public static final double GRAVITY_MAX = 1.5;

    /** 参加人数から生成する本数の合計。 */
    public static int totalCount(int participants) {
        return participants * 3 + 2;
    }

    /** 1tick進んだあとの落下速度。0から始めて、この関数を毎tick呼んで積み上げる。 */
    public static double nextFallSpeed(double currentSpeed) {
        return Math.min(GRAVITY_MAX, currentSpeed + GRAVITY_ACCEL);
    }

    /**
     * 出現位置を割り当てる。プレイヤーごとの真上を中心に、半径3ブロック以内へ均等に
     * ばら撒き、隣り合う位置が {@link #MIN_SEPARATION} 未満まで近づかないよう避ける。
     *
     * @param playerXZ 参加プレイヤーの (x, z)。空であってはならない
     * @param count    生成する本数
     * @param random   乱数源
     * @return 割り当てた (x, z) の一覧。{@code count} 件
     */
    public static List<double[]> spawnPositions(List<double[]> playerXZ, int count, Random random) {
        if (playerXZ.isEmpty()) {
            throw new IllegalArgumentException("プレイヤーが1人もいない");
        }
        List<double[]> chosen = new ArrayList<>(count);
        for (int i = 0; i < count; i++) {
            double[] center = playerXZ.get(i % playerXZ.size());
            double[] point = null;
            for (int attempt = 0; attempt < 20; attempt++) {
                double angle = random.nextDouble() * 2 * Math.PI;
                double radius = Math.sqrt(random.nextDouble()) * SPAWN_RADIUS;
                double x = center[0] + Math.cos(angle) * radius;
                double z = center[1] + Math.sin(angle) * radius;
                if (farEnough(chosen, x, z)) {
                    point = new double[] {x, z};
                    break;
                }
            }
            if (point == null) {
                double angle = random.nextDouble() * 2 * Math.PI;
                double radius = Math.sqrt(random.nextDouble()) * SPAWN_RADIUS;
                point = new double[] {center[0] + Math.cos(angle) * radius,
                        center[1] + Math.sin(angle) * radius};
            }
            chosen.add(point);
        }
        return chosen;
    }

    private static boolean farEnough(List<double[]> chosen, double x, double z) {
        for (double[] point : chosen) {
            double dx = point[0] - x;
            double dz = point[1] - z;
            if (dx * dx + dz * dz < MIN_SEPARATION * MIN_SEPARATION) {
                return false;
            }
        }
        return true;
    }
}
