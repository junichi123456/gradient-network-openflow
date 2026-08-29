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
     * 弱点倍率が有効になる条件（§12.6）。
     *
     * <p>倍率を常時有効にすると「弱点を殴り続けるだけ」になるため、条件で開閉する。
     */
    public enum Gate {
        /** 常に有効。 */
        ALWAYS,
        /** パリイ・妨害の成功などで<b>露出している間</b>だけ有効。 */
        ON_EXPOSURE
    }

    /**
     * 部位。
     *
     * @param name           部位名。個体内で一意
     * @param parent         親の部位名。根は null
     * @param base           静止時の変換
     * @param modelId        リソースパック側のモデル識別子
     * @param damageable     この部位への攻撃が個体にダメージを与えるか。
     *                       騎士型の槍のように、当てても通らない部位がある
     * @param vulnerability  被弾倍率。1.0 より大きい部位が弱点である
     * @param gate           弱点倍率が有効になる条件
     * @param hitboxSegments 当たり判定をいくつに分けるか。
     *                       当たり判定は軸に沿った直方体しか取れないため、
     *                       槍のように細長く傾く部位は<b>長さ方向に分割して並べる</b>
     * @param appearance     見た目。null なら描画側の既定に任せる
     */
    public record Part(String name, String parent, Transform base, int modelId,
                       boolean damageable, double vulnerability, Gate gate,
                       int hitboxSegments, Appearance appearance) {

        public Part {
            if (name == null || name.isBlank()) {
                throw new IllegalArgumentException("部位名が空である");
            }
            if (vulnerability < 0) {
                throw new IllegalArgumentException("被弾倍率が負である: " + name);
            }
            if (gate == null) {
                throw new IllegalArgumentException("弱点の条件が null である: " + name);
            }
            if (hitboxSegments < 1) {
                throw new IllegalArgumentException("当たり判定の分割数が1未満である: " + name);
            }
        }

        /** 被弾する、倍率を持たない部位。 */
        public Part(String name, String parent, Transform base, int modelId) {
            this(name, parent, base, modelId, true, 1.0, Gate.ALWAYS, 1, null);
        }

        /** 被弾可否だけを指定する部位。 */
        public Part(String name, String parent, Transform base, int modelId, boolean damageable) {
            this(name, parent, base, modelId, damageable, 1.0, Gate.ALWAYS, 1, null);
        }

        /** 見た目を差し替えた同じ部位。 */
        public Part looks(Appearance value) {
            if (value != null && value.decoration()) {
                return new Part(name, parent, base, modelId, false, 1.0, Gate.ALWAYS, 1, value);
            }
            return new Part(name, parent, base, modelId, damageable, vulnerability, gate,
                    hitboxSegments, value);
        }

        /** 弱点にした同じ部位。 */
        public Part weakPoint(double multiplier, Gate condition) {
            if (multiplier <= 1.0) {
                throw new IllegalArgumentException("弱点の倍率が1.0以下である: " + name);
            }
            return new Part(name, parent, base, modelId, damageable, multiplier, condition,
                    hitboxSegments, appearance);
        }

        /** 当たり判定を長さ方向に分割した同じ部位。 */
        public Part segments(int count) {
            return new Part(name, parent, base, modelId, damageable, vulnerability, gate,
                    count, appearance);
        }

        /** ダメージが通らない同じ部位。 */
        public Part immune() {
            return new Part(name, parent, base, modelId, false, 1.0, Gate.ALWAYS,
                    hitboxSegments, appearance);
        }

        public boolean isRoot() {
            return parent == null;
        }

        /** 弱点か。 */
        public boolean isWeakPoint() {
            return vulnerability > 1.0;
        }

        /** 見た目だけの部位か。当たり判定を置かない。 */
        public boolean decoration() {
            return appearance != null && appearance.decoration();
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

    /** 弱点となる部位。 */
    public List<Part> weakPoints() {
        return parts.values().stream().filter(Part::isWeakPoint).toList();
    }

    /**
     * 指定の部位を動かしたときに、実際に姿勢が変わる<b>表示実体</b>の数。
     *
     * <p>親を動かせば子もついて動く。円錐状の部位は輪切りの枚数だけ実体を持つ。
     * 通信量はモーションが動かす部位の数ではなく、この数で決まる。
     */
    public int movingDisplays(java.util.Collection<String> animated) {
        int total = 0;
        for (Part part : parts.values()) {
            boolean moves = chain(part.name()).stream()
                    .anyMatch(ancestor -> animated.contains(ancestor.name()));
            if (moves) {
                total += part.appearance() == null ? 1 : part.appearance().slices();
            }
        }
        return total;
    }

    /** すべての表示実体の数。 */
    public int displayCount() {
        return parts.values().stream()
                .mapToInt(part -> part.appearance() == null ? 1 : part.appearance().slices())
                .sum();
    }

    /** 当たり判定を持つ部位。見た目だけの部位は含まない。 */
    public List<Part> interactiveParts() {
        return parts.values().stream().filter(part -> !part.decoration()).toList();
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
