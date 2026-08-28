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
        ON_EXPOSURE,
        /** <b>破壊可能な部位をすべて壊す</b>と恒久的に有効。 */
        ON_ARMOR_BROKEN
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
     * @param breakThreshold 破壊に要するダメージ量（個体の最大体力に対する割合）。0 なら破壊不可
     * @param appearance     見た目。null なら描画側の既定に任せる
     */
    public record Part(String name, String parent, Transform base, int modelId,
                       boolean damageable, double vulnerability, Gate gate,
                       double breakThreshold, Appearance appearance) {

        public Part {
            if (name == null || name.isBlank()) {
                throw new IllegalArgumentException("部位名が空である");
            }
            if (vulnerability < 0) {
                throw new IllegalArgumentException("被弾倍率が負である: " + name);
            }
            if (breakThreshold < 0 || breakThreshold > 1) {
                throw new IllegalArgumentException("破壊の閾値が範囲外である: " + name);
            }
            if (!damageable && breakThreshold > 0) {
                throw new IllegalArgumentException("ダメージが通らない部位は破壊できない: " + name);
            }
            if (gate == null) {
                throw new IllegalArgumentException("弱点の条件が null である: " + name);
            }
        }

        /** 被弾する、倍率も破壊もない部位。 */
        public Part(String name, String parent, Transform base, int modelId) {
            this(name, parent, base, modelId, true, 1.0, Gate.ALWAYS, 0, null);
        }

        /** 被弾可否だけを指定する部位。 */
        public Part(String name, String parent, Transform base, int modelId, boolean damageable) {
            this(name, parent, base, modelId, damageable, 1.0, Gate.ALWAYS, 0, null);
        }

        /**
         * 見た目を差し替えた同じ部位。
         *
         * <p>見た目だけの部位（{@link Appearance#decoration()}）には当たり判定を置かないため、
         * 被弾対象からも外す。角や穂先を「殴れるはずの部位」として数えないためである。
         */
        public Part looks(Appearance value) {
            if (value != null && value.decoration()) {
                return new Part(name, parent, base, modelId, false, 1.0, Gate.ALWAYS, 0, value);
            }
            return new Part(name, parent, base, modelId, damageable, vulnerability, gate,
                    breakThreshold, value);
        }

        /** 弱点にした同じ部位。 */
        public Part weakPoint(double multiplier, Gate condition) {
            if (multiplier <= 1.0) {
                throw new IllegalArgumentException("弱点の倍率が1.0以下である: " + name);
            }
            return new Part(name, parent, base, modelId, damageable, multiplier, condition,
                    breakThreshold, appearance);
        }

        /** 破壊可能にした同じ部位。 */
        public Part breakableAt(double fractionOfMaxHealth) {
            return new Part(name, parent, base, modelId, damageable, vulnerability, gate,
                    fractionOfMaxHealth, appearance);
        }

        /** ダメージが通らない同じ部位。 */
        public Part immune() {
            return new Part(name, parent, base, modelId, false, 1.0, Gate.ALWAYS, 0, appearance);
        }

        public boolean isRoot() {
            return parent == null;
        }

        /** 破壊可能か。 */
        public boolean breakable() {
            return breakThreshold > 0;
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

    /** 破壊可能な部位。すべて壊すと {@link Gate#ON_ARMOR_BROKEN} の弱点が開く。 */
    public List<Part> breakableParts() {
        return parts.values().stream().filter(Part::breakable).toList();
    }

    /** 弱点となる部位。 */
    public List<Part> weakPoints() {
        return parts.values().stream().filter(Part::isWeakPoint).toList();
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
