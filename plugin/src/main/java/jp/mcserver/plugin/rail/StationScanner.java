package jp.mcserver.plugin.rail;

import java.util.EnumMap;
import java.util.HashMap;
import java.util.Map;
import jp.mcserver.core.rail.StationCertification;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.World;
import org.bukkit.block.Block;

/**
 * F-04の駅舎認定で、実際にワールドのブロックを数える（Bukkit依存の部分）。
 *
 * <p>数えるところまでがここの仕事で、判定そのもの（面積・比率・総数のルール）は
 * {@code core} の {@link StationCertification} が持つ。ユーザーの決定により、
 * 「単一の直方体であるか」の追加検証はしない（`rail_infra_spec.md` §3
 * 「実際に建てた構造物が単純な直方体1つであるか」は検証しない）——ここでは
 * 与えられた2点から求めたバウンディングボックスをそのまま数えるだけでよい。
 */
public final class StationScanner {

    private StationScanner() {
    }

    public record ScanResult(int outerWidth, int outerHeight, int outerDepth, int centerX,
                             int centerY, int centerZ,
                             Map<StationCertification.Face, Integer> qualifyingCountsByFace,
                             Map<String, Integer> blockCounts) {
    }

    /**
     * @param corner1 プレイヤーが指定した1つ目の角（{@code /rail station pos1}）
     * @param corner2 プレイヤーが指定した2つ目の角（対角、{@code /rail station pos2}）
     */
    public static ScanResult scan(Location corner1, Location corner2, RailConfig config) {
        World world = corner1.getWorld();
        int minX = Math.min(corner1.getBlockX(), corner2.getBlockX());
        int maxX = Math.max(corner1.getBlockX(), corner2.getBlockX());
        int minY = Math.min(corner1.getBlockY(), corner2.getBlockY());
        int maxY = Math.max(corner1.getBlockY(), corner2.getBlockY());
        int minZ = Math.min(corner1.getBlockZ(), corner2.getBlockZ());
        int maxZ = Math.max(corner1.getBlockZ(), corner2.getBlockZ());

        Map<String, Integer> blockCounts = new HashMap<>();
        Map<StationCertification.Face, Integer> faceCounts =
                new EnumMap<>(StationCertification.Face.class);
        for (StationCertification.Face face : StationCertification.Face.values()) {
            faceCounts.put(face, 0);
        }

        // Minecraft の方角は北=−Z・南=+Z・東=+X・西=−X（`raid_model_spec.md` §1 と同じ規約）
        for (int x = minX; x <= maxX; x++) {
            for (int y = minY; y <= maxY; y++) {
                for (int z = minZ; z <= maxZ; z++) {
                    Block block = world.getBlockAt(x, y, z);
                    Material material = block.getType();
                    if (!config.isQualifying(material)) {
                        continue;
                    }
                    blockCounts.merge(material.name(), 1, Integer::sum);
                    if (y == maxY) {
                        faceCounts.merge(StationCertification.Face.CEILING, 1, Integer::sum);
                    }
                    if (y == minY) {
                        faceCounts.merge(StationCertification.Face.FLOOR, 1, Integer::sum);
                    }
                    if (z == minZ) {
                        faceCounts.merge(StationCertification.Face.NORTH, 1, Integer::sum);
                    }
                    if (z == maxZ) {
                        faceCounts.merge(StationCertification.Face.SOUTH, 1, Integer::sum);
                    }
                    if (x == maxX) {
                        faceCounts.merge(StationCertification.Face.EAST, 1, Integer::sum);
                    }
                    if (x == minX) {
                        faceCounts.merge(StationCertification.Face.WEST, 1, Integer::sum);
                    }
                }
            }
        }
        return new ScanResult(maxX - minX + 1, maxY - minY + 1, maxZ - minZ + 1,
                (minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2, faceCounts, blockCounts);
    }
}
