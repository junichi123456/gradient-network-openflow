package jp.mcserver.core;

/** チャンク座標。 */
public record ChunkPos(int x, int z) {

    /** チェビシェフ距離（§4.7 の解放順序に用いる）。 */
    public int chebyshev(ChunkPos other) {
        return Math.max(Math.abs(x - other.x), Math.abs(z - other.z));
    }

    /** 直交4方向の隣接。領土の隣接要件（§4.10）と連結性の判定に用いる。 */
    public ChunkPos[] orthogonalNeighbors() {
        return new ChunkPos[] {
                new ChunkPos(x + 1, z),
                new ChunkPos(x - 1, z),
                new ChunkPos(x, z + 1),
                new ChunkPos(x, z - 1)
        };
    }

    @Override
    public String toString() {
        return "(" + x + "," + z + ")";
    }
}
