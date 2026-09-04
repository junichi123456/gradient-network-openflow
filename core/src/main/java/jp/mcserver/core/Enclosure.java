package jp.mcserver.core;

import java.util.ArrayDeque;
import java.util.Deque;
import java.util.HashSet;
import java.util.Set;

/**
 * 囲い込みの検出（§4.11）。
 *
 * <p>自国領土によって四方を塞がれ、外部へ到達できなくなったチャンクの集合を求める。
 * 1チャンクの穴に限らず、複数チャンクの空洞も検出する。
 *
 * <p>自国領土のみを壁とみなす。他国の領土や保護区は通過可能として扱うため、
 * 他国の土地を経由して外へ抜けられる領域は「囲い込み」に含まれない。
 */
public final class Enclosure {

    private Enclosure() {}

    /**
     * 領土に囲まれて外部へ到達できないチャンクを返す。
     *
     * @param ownTerritory 自国領土
     */
    public static Set<ChunkPos> findEnclosed(Set<ChunkPos> ownTerritory) {
        Set<ChunkPos> enclosed = new HashSet<>();
        if (ownTerritory.isEmpty()) {
            return enclosed;
        }

        int minX = Integer.MAX_VALUE, maxX = Integer.MIN_VALUE;
        int minZ = Integer.MAX_VALUE, maxZ = Integer.MIN_VALUE;
        for (ChunkPos c : ownTerritory) {
            minX = Math.min(minX, c.x());
            maxX = Math.max(maxX, c.x());
            minZ = Math.min(minZ, c.z());
            maxZ = Math.max(maxZ, c.z());
        }
        // 領土の外周に1チャンクの余白を取る。この余白は必ず外部と繋がっている。
        minX--; maxX++; minZ--; maxZ++;

        Set<ChunkPos> reached = new HashSet<>();
        Deque<ChunkPos> queue = new ArrayDeque<>();
        for (int x = minX; x <= maxX; x++) {
            offer(queue, reached, ownTerritory, new ChunkPos(x, minZ));
            offer(queue, reached, ownTerritory, new ChunkPos(x, maxZ));
        }
        for (int z = minZ; z <= maxZ; z++) {
            offer(queue, reached, ownTerritory, new ChunkPos(minX, z));
            offer(queue, reached, ownTerritory, new ChunkPos(maxX, z));
        }

        while (!queue.isEmpty()) {
            ChunkPos c = queue.poll();
            for (ChunkPos n : c.orthogonalNeighbors()) {
                if (n.x() < minX || n.x() > maxX || n.z() < minZ || n.z() > maxZ) {
                    continue;
                }
                offer(queue, reached, ownTerritory, n);
            }
        }

        for (int x = minX; x <= maxX; x++) {
            for (int z = minZ; z <= maxZ; z++) {
                ChunkPos c = new ChunkPos(x, z);
                if (!ownTerritory.contains(c) && !reached.contains(c)) {
                    enclosed.add(c);
                }
            }
        }
        return enclosed;
    }

    private static void offer(Deque<ChunkPos> queue, Set<ChunkPos> reached,
                              Set<ChunkPos> territory, ChunkPos c) {
        if (territory.contains(c) || !reached.add(c)) {
            return;
        }
        queue.add(c);
    }
}
