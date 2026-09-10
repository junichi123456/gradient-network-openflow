package jp.mcserver.plugin.rail;

import java.util.List;
import java.util.concurrent.CopyOnWriteArrayList;

/**
 * 認定駅舎のバウンディングボックスをメモリ上に保持する。
 *
 * <p><b>「認定駅舎内でMobスポーンをキャンセルする」判定（{@link RailListener#onEntitySpawn}）は
 * サーバー内のあらゆるモブのスポーンのたびに呼ばれる。</b>モブの湧きはサーバーの常時負荷の
 * 中心（`capacity_plan.md` §1.3）であり、そのたびに SQLite へ同期クエリを投げるのは
 * 筋が悪い——実測を待たず直した（ユーザーへの負荷対策の相談で見つかった点）。
 *
 * <p>駅舎は「認定されたとき」という低頻度のイベントでしか増えないため、起動時に
 * {@code rail.db} から読み込んでおき、以後は<b>このメモリ上の一覧だけ</b>で判定する。
 * 書き込み（駅舎の追加）は稀、読み取り（Mobスポーンのたび）は頻繁という比率に合わせて
 * {@link CopyOnWriteArrayList} を使っている——読み取り側にロックが要らない。
 */
final class StationIndex {

    /** 駅舎1つぶんのバウンディングボックス。 */
    record Box(String world, int minX, int minY, int minZ, int maxX, int maxY, int maxZ) {

        boolean contains(String world, int x, int y, int z) {
            return this.world.equals(world)
                    && x >= minX && x <= maxX
                    && y >= minY && y <= maxY
                    && z >= minZ && z <= maxZ;
        }

        /** 中心点からの直線距離。ワールドが違えば無限大（F-04の150ブロック判定に使う）。 */
        double distanceTo(String world, int x, int y, int z) {
            if (!this.world.equals(world)) {
                return Double.POSITIVE_INFINITY;
            }
            double cx = (minX + maxX) / 2.0;
            double cy = (minY + maxY) / 2.0;
            double cz = (minZ + maxZ) / 2.0;
            double dx = cx - x;
            double dy = cy - y;
            double dz = cz - z;
            return Math.sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    private final List<Box> stations = new CopyOnWriteArrayList<>();

    /** 起動時、DBから読み込んだ既存の駅舎で埋める。 */
    void loadAll(List<RailDatabase.StationRecord> records) {
        stations.clear();
        for (RailDatabase.StationRecord record : records) {
            stations.add(new Box(record.world(), record.minX(), record.minY(), record.minZ(),
                    record.maxX(), record.maxY(), record.maxZ()));
        }
    }

    /** 新規に認定した駅舎を追加する。DBへの登録とあわせて呼ぶこと。 */
    void add(String world, int minX, int minY, int minZ, int maxX, int maxY, int maxZ) {
        stations.add(new Box(world, minX, minY, minZ, maxX, maxY, maxZ));
    }

    /** その座標が、いずれかの駅舎の範囲内にあるか。 */
    boolean contains(String world, int x, int y, int z) {
        for (Box box : stations) {
            if (box.contains(world, x, y, z)) {
                return true;
            }
        }
        return false;
    }

    /** 最寄りの駅舎までの距離。駅舎が1つも無ければ無限大。 */
    double nearestDistance(String world, int x, int y, int z) {
        double nearest = Double.POSITIVE_INFINITY;
        for (Box box : stations) {
            nearest = Math.min(nearest, box.distanceTo(world, x, y, z));
        }
        return nearest;
    }

    int size() {
        return stations.size();
    }
}
