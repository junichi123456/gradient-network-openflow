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
     * @param base       静止時の変換
     * @param modelId    リソースパック側のモデル識別子
     * @param damageable この部位への攻撃が個体にダメージを与えるか。
     *                   騎士型の槍のように、当てても通らない部位がある
     */
    public record Part(String name, String parent, Transform base, int modelId,
                       boolean damageable) {

        public Part {
            if (name == null || name.isBlank()) {
                throw new IllegalArgumentException("部位名が空である");
            }
        }

        /** 被弾する部位。 */
        public Part(String name, String parent, Transform base, int modelId) {
            this(name, parent, base, modelId, true);
        }

        public boolean isRoot() {
            return parent == null;
        }
    }

    private final Map<String, Part> parts = new LinkedHashMap<>();
    private final double heightBlocks;
    private final double hitboxWidth;

    /**
     * @param heightBlocks 体高。<b>全長</b>で測る（§12.6）
     * @param hitboxWidth  幅。<b>胴体と並行にした両腕を含む長さ</b>で測る（§12.6）
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
        if (damageablePartCount() == 0) {
            throw new IllegalArgumentException("被弾する部位が1つもない");
        }
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

    /** 被弾する部位の数。すべての部位が無敵な骨格は成立しない。 */
    public int damageablePartCount() {
        return (int) parts.values().stream().filter(Part::damageable).count();
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
