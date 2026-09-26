package jp.mcserver.core.raid;

/**
 * 相手を追尾しながら直進する浮遊剣の軌道（純粋関数）。
 *
 * <p>「空間斬撃」「全域大旋回」で使う。円弧を描いて旋回する一次実装では、いったん軌道を
 * 決めるとその後は相手を追わなかったため、動く相手にはほとんど当たらなかった
 * （`raid_species.md` §2「軌道の修正」）。そこで、一定間隔ごとに狙い直しながら
 * 相手へ向けて直進する方式に直した——狙いの再計算は plugin 側（生きているプレイヤーの
 * 現在位置を読む必要がある）が行い、ここは「いまの位置から、狙い先まで1tickぶん進んだ
 * 位置」を返す純粋関数だけを持つ。
 */
public final class HomingDart {

    private HomingDart() {
    }

    /**
     * 1tick分、狙い先へ向けて進んだ位置。狙い先までの距離が1tickの歩幅未満なら、
     * 狙い先そのものを返す（追い越さない）。
     *
     * @param speedBlocksPerTick 1tickあたりに進む距離（ブロック）
     * @return {x, y, z}
     */
    public static double[] stepToward(double x, double y, double z,
                                      double targetX, double targetY, double targetZ,
                                      double speedBlocksPerTick) {
        double dx = targetX - x;
        double dy = targetY - y;
        double dz = targetZ - z;
        double distance = Math.sqrt(dx * dx + dy * dy + dz * dz);
        if (distance <= speedBlocksPerTick || distance < 1e-9) {
            return new double[] {targetX, targetY, targetZ};
        }
        double scale = speedBlocksPerTick / distance;
        return new double[] {x + dx * scale, y + dy * scale, z + dz * scale};
    }

    /** 秒速から、1tick（1/20秒）あたりの歩幅を求める。 */
    public static double blocksPerTick(double blocksPerSecond) {
        return blocksPerSecond / 20.0;
    }
}
