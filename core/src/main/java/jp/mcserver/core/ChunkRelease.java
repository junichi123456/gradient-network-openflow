package jp.mcserver.core;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.Deque;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/**
 * チャンク解放（§4.7）。
 *
 * <p>解放順序は次のとおり。
 * <ol>
 *   <li>首都チャンクからのチェビシェフ距離が最大のもの</li>
 *   <li>同点は境界接触面が多い順（より外側に突出している方）</li>
 *   <li>除去すると残存領土が首都と連結でなくなる場合はスキップして次点</li>
 * </ol>
 *
 * <p>「境界接触面」は、直交4方向の隣接チャンクのうち自国領土に属さないものの数とする。
 * 領土の隣接要件（§4.10）が直交隣接であることに合わせ、連結性も直交隣接で判定する。
 */
public final class ChunkRelease {

    private ChunkRelease() {}

    /**
     * 指定数のチャンクを解放順に選ぶ。
     *
     * @param territory 現在の領土（首都を含む）
     * @param capital   首都チャンク
     * @param count     解放するチャンク数
     * @return 解放するチャンクを解放順に並べたリスト。連結性の制約で選べない場合は要求数に満たないことがある。
     */
    public static List<ChunkPos> select(Set<ChunkPos> territory, ChunkPos capital, int count) {
        if (!territory.contains(capital)) {
            throw new IllegalArgumentException("首都チャンクが領土に含まれていない: " + capital);
        }
        Set<ChunkPos> remaining = new HashSet<>(territory);
        List<ChunkPos> released = new ArrayList<>();

        for (int i = 0; i < count; i++) {
            ChunkPos pick = pickOne(remaining, capital);
            if (pick == null) {
                break; // これ以上は連結性を保って解放できない
            }
            remaining.remove(pick);
            released.add(pick);
        }
        return released;
    }

    /** ランク差に応じた解放。ランク差 × 16 チャンク分まで反復適用する（§4.7）。 */
    public static List<ChunkPos> selectForDemotion(Set<ChunkPos> territory, ChunkPos capital,
                                                   int fromRank, int toRank) {
        if (toRank >= fromRank) {
            return List.of();
        }
        if (toRank == 0) {
            return selectDownToCityState(territory, capital);
        }
        int target = Formulas.chunks(toRank);
        int excess = Math.max(0, territory.size() - target);
        return select(territory, capital, excess);
    }

    /**
     * rank1 → 都市国家の降格（§4.7）。
     * 首都チャンクと、それに隣接するチャンクのうち首都からの距離が最小の2つを残す。
     */
    public static List<ChunkPos> selectDownToCityState(Set<ChunkPos> territory, ChunkPos capital) {
        Set<ChunkPos> keep = new HashSet<>();
        keep.add(capital);

        List<ChunkPos> adjacent = new ArrayList<>();
        for (ChunkPos n : capital.orthogonalNeighbors()) {
            if (territory.contains(n)) {
                adjacent.add(n);
            }
        }
        adjacent.sort(Comparator
                .comparingInt((ChunkPos c) -> c.chebyshev(capital))
                .thenComparingInt(ChunkPos::x)
                .thenComparingInt(ChunkPos::z));
        for (int i = 0; i < Math.min(2, adjacent.size()); i++) {
            keep.add(adjacent.get(i));
        }

        List<ChunkPos> released = new ArrayList<>(territory);
        released.removeAll(keep);
        released.sort(releaseOrder(territory, capital));
        return released;
    }

    /** 都市国家 → 野営地の降格（§4.7）。首都チャンクのみを残す。 */
    public static List<ChunkPos> selectDownToCamp(Set<ChunkPos> territory, ChunkPos capital) {
        List<ChunkPos> released = new ArrayList<>(territory);
        released.remove(capital);
        released.sort(releaseOrder(territory, capital));
        return released;
    }

    private static ChunkPos pickOne(Set<ChunkPos> territory, ChunkPos capital) {
        List<ChunkPos> candidates = new ArrayList<>(territory);
        candidates.remove(capital);
        candidates.sort(releaseOrder(territory, capital));

        for (ChunkPos candidate : candidates) {
            Set<ChunkPos> after = new HashSet<>(territory);
            after.remove(candidate);
            if (isConnected(after, capital)) {
                return candidate;
            }
        }
        return null;
    }

    private static Comparator<ChunkPos> releaseOrder(Set<ChunkPos> territory, ChunkPos capital) {
        return Comparator
                .comparingInt((ChunkPos c) -> -c.chebyshev(capital))   // 距離が大きい順
                .thenComparingInt(c -> -exposure(territory, c))        // 境界接触面が多い順
                .thenComparingInt(ChunkPos::x)                         // 決定性のための固定順
                .thenComparingInt(ChunkPos::z);
    }

    /** 境界接触面の数。直交4方向のうち自国領土でないものを数える。 */
    static int exposure(Set<ChunkPos> territory, ChunkPos c) {
        int count = 0;
        for (ChunkPos n : c.orthogonalNeighbors()) {
            if (!territory.contains(n)) {
                count++;
            }
        }
        return count;
    }

    /** 領土全体が首都と連結しているか（直交隣接による幅優先探索）。 */
    static boolean isConnected(Set<ChunkPos> territory, ChunkPos capital) {
        if (territory.isEmpty()) {
            return true;
        }
        if (!territory.contains(capital)) {
            return false;
        }
        Set<ChunkPos> seen = new HashSet<>();
        Deque<ChunkPos> queue = new ArrayDeque<>();
        queue.add(capital);
        seen.add(capital);
        while (!queue.isEmpty()) {
            ChunkPos c = queue.poll();
            for (ChunkPos n : c.orthogonalNeighbors()) {
                if (territory.contains(n) && seen.add(n)) {
                    queue.add(n);
                }
            }
        }
        return seen.size() == territory.size();
    }
}
