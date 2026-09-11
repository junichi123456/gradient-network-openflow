package jp.mcserver.core;

/**
 * 保護解除・降格時の座標告知（§4.8）。
 *
 * <p>告知座標 = (真の座標 + hash(チャンク座標) mod ±64) を100単位で四捨五入。
 *
 * <p>オフセットはチャンク座標をシードとした固定値である。同じチャンクは何度告知しても
 * 同じオフセットを持つため、複数回の告知を平均しても真の座標を復元できない。
 */
public final class CoordinateAnnouncement {

    private CoordinateAnnouncement() {}

    /** オフセットの絶対値の上限（ブロック）。 */
    public static final int OFFSET_LIMIT = 64;

    /** 告知の丸め単位（ブロック）。 */
    public static final int ROUNDING = 100;

    /** チャンク座標から決定的に定まる X 方向のオフセット。範囲は [-64, 64]。 */
    public static int offsetX(int chunkX, int chunkZ) {
        return offset(mix(chunkX, chunkZ, 0x9E3779B9L));
    }

    /** チャンク座標から決定的に定まる Z 方向のオフセット。範囲は [-64, 64]。 */
    public static int offsetZ(int chunkX, int chunkZ) {
        return offset(mix(chunkX, chunkZ, 0xC2B2AE35L));
    }

    /** 告知するブロック座標を返す。 */
    public static int announcedX(int trueX, int chunkX, int chunkZ) {
        return round(trueX + offsetX(chunkX, chunkZ));
    }

    public static int announcedZ(int trueZ, int chunkX, int chunkZ) {
        return round(trueZ + offsetZ(chunkX, chunkZ));
    }

    static int round(int value) {
        return (int) (Math.round(value / (double) ROUNDING) * ROUNDING);
    }

    private static int offset(long hash) {
        int span = OFFSET_LIMIT * 2 + 1; // [-64, 64]
        return (int) Math.floorMod(hash, span) - OFFSET_LIMIT;
    }

    private static long mix(int x, int z, long salt) {
        long h = (x * 0x1F1F1F1FL) ^ (z * 0x5DEECE66DL) ^ salt;
        h ^= (h >>> 33);
        h *= 0xFF51AFD7ED558CCDL;
        h ^= (h >>> 33);
        h *= 0xC4CEB9FE1A85EC53L;
        h ^= (h >>> 33);
        return h;
    }
}
