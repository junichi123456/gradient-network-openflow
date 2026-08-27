package jp.mcserver.core.raid;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * 個体の骨格（§12.6）。部位のツリーと体型を定める。
 *
 * <p>各部位は1つの表示エンティティに対応する。部位の合成（親子の変換の積）は描画側で行い、
 * ここでは<b>構造の妥当性</b>と<b>部位数・階層の深さ</b>を扱う。
 * 行列の積を本層で持たないのは、回転の合成が成分ごとの加算では表せないためである。
 */
public final class Rig {

    /**
     * 部位。
     *
     * @param name    部位名。個体内で一意
     * @param parent  親の部位名。根は null
     * @param base    静止時の変換
     * @param modelId リソースパック側のモデル識別子
     */
    public record Part(String name, String parent, Transform base, int modelId) {

        public Part {
            if (name == null || name.isBlank()) {
                throw new IllegalArgumentException("部位名が空である");
            }
        }

        public boolean isRoot() {
            return parent == null;
        }
    }

    private final Map<String, Part> parts = new LinkedHashMap<>();
    private final double heightBlocks;
    private final double hitboxWidth;

    /**
     * @param heightBlocks 体高（ブロック）
     * @param hitboxWidth  当たり判定の幅（ブロック）
     */
    public Rig(List<Part> parts, double heightBlocks, double hitboxWidth) {
        if (parts.isEmpty()) {
            throw new IllegalArgumentException("部位が1つもない");
        }
        if (heightBlocks <= 0 || hitboxWidth <= 0) {
            throw new IllegalArgumentException("体型が不正である");
        }
        for (Part part : parts) {
            if (this.parts.put(part.name(), part) != null) {
                throw new IllegalArgumentException("部位名が重複している: " + part.name());
            }
        }
        this.heightBlocks = heightBlocks;
        this.hitboxWidth = hitboxWidth;
        validate();
    }

    private void validate() {
        List<Part> roots = parts.values().stream().filter(Part::isRoot).toList();
        if (roots.size() != 1) {
            throw new IllegalArgumentException("根の部位は1つでなければならない: " + roots.size());
        }
        for (Part part : parts.values()) {
            if (part.isRoot()) {
                continue;
            }
            if (!parts.containsKey(part.parent())) {
                throw new IllegalArgumentException(
                        "親が存在しない: " + part.name() + " → " + part.parent());
            }
            // 循環の検出
            Set<String> seen = new HashSet<>();
            String cursor = part.name();
            while (cursor != null) {
                if (!seen.add(cursor)) {
                    throw new IllegalArgumentException("部位の親子関係が循環している: " + part.name());
                }
                cursor = parts.get(cursor).parent();
            }
        }
    }

    public Part root() {
        return parts.values().stream().filter(Part::isRoot).findFirst().orElseThrow();
    }

    public Part part(String name) {
        Part part = parts.get(name);
        if (part == null) {
            throw new IllegalArgumentException("存在しない部位である: " + name);
        }
        return part;
    }

    public Set<String> partNames() {
        return parts.keySet();
    }

    public int partCount() {
        return parts.size();
    }

    public double heightBlocks() {
        return heightBlocks;
    }

    public double hitboxWidth() {
        return hitboxWidth;
    }

    /** 根から指定の部位までの連鎖。描画側が変換を合成する順序を示す。 */
    public List<Part> chain(String name) {
        List<Part> reversed = new ArrayList<>();
        Part cursor = part(name);
        while (cursor != null) {
            reversed.add(cursor);
            cursor = cursor.isRoot() ? null : parts.get(cursor.parent());
        }
        List<Part> ordered = new ArrayList<>(reversed.size());
        for (int i = reversed.size() - 1; i >= 0; i--) {
            ordered.add(reversed.get(i));
        }
        return ordered;
    }

    /** 階層の深さ（根のみなら1）。 */
    public int depth() {
        int max = 0;
        for (String name : parts.keySet()) {
            max = Math.max(max, chain(name).size());
        }
        return max;
    }
}
