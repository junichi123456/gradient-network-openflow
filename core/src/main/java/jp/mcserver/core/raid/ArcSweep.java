package jp.mcserver.core.raid;

/**
 * 浮遊剣が戦場の中心を軸に弧を描いて旋回する軌道（純粋関数）。
 *
 * <p>「空間斬撃」「全域大旋回」の2つの特殊モーションで共通に使う。どちらも
 * <b>召喚位置から戦場の中心までの距離を半径とする円弧</b>を、旋回しながら
 * Y座標だけ直線的に下げていく——という同じ形の軌道を持ち、違うのは
 * 半径・角速度・高さの範囲・尺（{@code durationTicks}）だけである
 * （`raid_species.md` §2「空間斬撃」「全域大旋回」）。
 *
 * <p>半径を戦場の中心からの距離に固定するのは、{@code RaidBossBase} の回旋突進
 * （{@code orbitStep}）と同じ理由による。中心を軸に回る円弧は、その半径を
 * 超えて戦場の外へ出ることがなく、境界との整合を個別に確かめずに済む。
 */
public final class ArcSweep {

    private ArcSweep() {
    }

    /**
     * 目標の移動距離（水平方向の弧長、ブロック）から尺（tick）を求める。
     *
     * <p>「移動速度25m/s」を弧に沿った速さとして扱う。1tickは1/20秒なので、
     * 1tickあたり25/20=1.25ブロック進む。尺は距離をこの歩幅で割って丸めた値である。
     */
    public static int durationTicks(double distanceBlocks, double blocksPerSecond) {
        if (distanceBlocks <= 0 || blocksPerSecond <= 0) {
            throw new IllegalArgumentException("距離・速度は正である必要がある");
        }
        double blocksPerTick = blocksPerSecond / 20.0;
        return (int) Math.round(distanceBlocks / blocksPerTick);
    }

    /**
     * 弧の掃過角（ラジアン）。弧長 = 半径 × 掃過角の関係から求める。
     *
     * @param radius        召喚位置から戦場の中心までの距離（ブロック）
     * @param distanceBlocks 弧に沿って進む水平距離（ブロック）
     */
    public static double sweepRadians(double radius, double distanceBlocks) {
        if (radius <= 0) {
            throw new IllegalArgumentException("半径が0以下である: " + radius);
        }
        return distanceBlocks / radius;
    }

    /**
     * 待機明け（旋回を始めた瞬間）からの経過 tick における水平位置。
     *
     * @param centerX     戦場の中心 x
     * @param centerZ     戦場の中心 z
     * @param startAngle  旋回を始める瞬間の、中心から見た角度（ラジアン）
     * @param radius      中心からの距離（ブロック）。旋回のあいだ一定
     * @param direction   +1 で反時計回り、-1 で時計回り
     * @param sweepTotal  旋回全体の掃過角（ラジアン、正の値）
     * @param durationTicks 旋回に掛ける尺（tick）
     * @param ticksSinceStart 旋回を始めてからの経過 tick（0 〜 durationTicks）
     */
    public static double[] horizontalAt(double centerX, double centerZ, double startAngle,
                                        double radius, int direction, double sweepTotal,
                                        int durationTicks, int ticksSinceStart) {
        double progress = clamp01((double) ticksSinceStart / durationTicks);
        double angle = startAngle + direction * sweepTotal * progress;
        return new double[] {
                centerX + Math.cos(angle) * radius,
                centerZ + Math.sin(angle) * radius,
        };
    }

    /** 旋回に伴う Y座標。開始高度から終了高度まで、経過に比例して直線的に下がる。 */
    public static double yAt(double startY, double endY, int durationTicks, int ticksSinceStart) {
        double progress = clamp01((double) ticksSinceStart / durationTicks);
        return startY + (endY - startY) * progress;
    }

    private static double clamp01(double value) {
        return Math.max(0, Math.min(1, value));
    }
}
