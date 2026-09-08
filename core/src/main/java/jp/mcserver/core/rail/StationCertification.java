package jp.mcserver.core.rail;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/**
 * 駅舎認定および Mob スポーン制御機能（`rail_infra_spec.md` F-04、更新版）。
 *
 * <p>ワールドの実際のブロックを数える処理（`EntitySpawnEvent` の購読も含む）は
 * Bukkit 依存のためプラグイン側が持つ。ここは<b>数え終えた結果</b>（面ごとの対象ブロック数・
 * 種類ごとの総数・最寄りの既存駅舎までの距離）を受け取って判定するだけの、純粋な幾何・
 * 集計ロジックである。
 *
 * <p>対象ブロック（bricks系フルブロック、17種）のうち「硫黄レンガ」「辰砂レンガ」は
 * <b>Minecraft 1.26.2 で実装されたバニラのブロック</b>である（ユーザーへ確認して判明。
 * Mod 供給ではない）。ただし、この2つに対応する Bukkit の {@code Material} 定数名は
 * まだ確認していない——`plugin` のビルドに使う Paper API が 1.26.2 系の定数を持つように
 * なってから確定する（`rail_infra_spec.md` §6）。この理由により、対象ブロックの一覧は
 * Bukkit の {@code Material} ではなく、要件定義書にある名前をそのまま
 * {@link #BLOCK_TYPE_LABELS} として残すに留めている。
 */
public final class StationCertification {

    private StationCertification() {
    }

    // ------------------------------------------------------------ 空間要件

    public static final int MIN_INNER_WIDTH = 20;
    public static final int MIN_INNER_HEIGHT = 10;
    public static final int MIN_INNER_DEPTH = 40;

    /** 壁の厚み1ブロックぶんを内寸に足した外寸の最小値。 */
    public static final int MIN_OUTER_WIDTH = MIN_INNER_WIDTH + 2;
    public static final int MIN_OUTER_HEIGHT = MIN_INNER_HEIGHT + 2;
    public static final int MIN_OUTER_DEPTH = MIN_INNER_DEPTH + 2;

    /** 外寸の最大値（ユーザーが確定した値）。これを超える駅舎は認定しない。 */
    public static final int MAX_OUTER_WIDTH = 32;
    public static final int MAX_OUTER_HEIGHT = 17;
    public static final int MAX_OUTER_DEPTH = 52;

    /** 壁の厚み1ブロックぶんを外寸から引いた内寸の最大値。 */
    public static final int MAX_INNER_WIDTH = MAX_OUTER_WIDTH - 2;
    public static final int MAX_INNER_HEIGHT = MAX_OUTER_HEIGHT - 2;
    public static final int MAX_INNER_DEPTH = MAX_OUTER_DEPTH - 2;

    public static boolean fitsSpace(int outerWidth, int outerHeight, int outerDepth) {
        return outerWidth >= MIN_OUTER_WIDTH && outerWidth <= MAX_OUTER_WIDTH
                && outerHeight >= MIN_OUTER_HEIGHT && outerHeight <= MAX_OUTER_HEIGHT
                && outerDepth >= MIN_OUTER_DEPTH && outerDepth <= MAX_OUTER_DEPTH;
    }

    // ------------------------------------------------------------ 各面の最低使用率

    /** 判定対象の6面（最小バウンディングボックスの外殻）。 */
    public enum Face {
        CEILING, FLOOR, NORTH, SOUTH, EAST, WEST
    }

    /** 各面の対象ブロック使用率の下限。 */
    public static final double FACE_MIN_RATIO = 0.40;

    public record FaceResult(Face face, int qualifyingCount, int faceArea, double ratio,
                             boolean passed) {
    }

    /**
     * 面の面積。最小バウンディングボックス（直方体）を前提に、6面それぞれを<b>1枚の
     * 平らな長方形</b>として扱う——外寸が最小値（22×12×42）より大きくても、それぞれの
     * 面はその大きさなりの正しい面積になる。天井・床は幅×奥行、南北の壁は幅×高さ、
     * 東西の壁は奥行×高さを持つ（3辺のうち向かい合う2面は同じ面積になる）。
     *
     * <p>例: 外寸 幅30×高さ15×奥行45 なら、天井・床はそれぞれ 30×45=1,350、
     * 南北の壁はそれぞれ 30×15=450、東西の壁はそれぞれ 45×15=675 になる——6面とも
     * 同じ面積になるわけではない。
     *
     * <p><b>「実際に建てた構造物が単純な直方体1つであるか」は検証しない</b>
     * （ユーザーが決定：<b>各面の舗装率さえ満たせば駅舎として許容する</b>）。
     * L字型か・床や壁に穴が無いか・離れた場所に別の部屋が無いか、といった形そのものの
     * 妥当性チェックは、この判定の対象外である。
     */
    public static int faceArea(Face face, int outerWidth, int outerHeight, int outerDepth) {
        if (outerWidth <= 0 || outerHeight <= 0 || outerDepth <= 0) {
            throw new IllegalArgumentException(
                    "外寸が0以下である: " + outerWidth + "×" + outerHeight + "×" + outerDepth);
        }
        return switch (face) {
            case CEILING, FLOOR -> outerWidth * outerDepth;
            case NORTH, SOUTH -> outerWidth * outerHeight;
            case EAST, WEST -> outerDepth * outerHeight;
        };
    }

    /**
     * 1面ぶんの判定。
     *
     * @param faceArea       その面を構成する全ブロック数（面積）。{@link #faceArea} で求める
     * @param qualifyingCount そのうち対象ブロックである数
     */
    public static FaceResult checkFace(Face face, int qualifyingCount, int faceArea) {
        if (faceArea <= 0) {
            throw new IllegalArgumentException("面の面積が0以下である: " + face);
        }
        if (qualifyingCount < 0 || qualifyingCount > faceArea) {
            throw new IllegalArgumentException(
                    "対象ブロック数が面積の範囲外である: " + face + " " + qualifyingCount + "/" + faceArea);
        }
        double ratio = (double) qualifyingCount / faceArea;
        return new FaceResult(face, qualifyingCount, faceArea, ratio, ratio >= FACE_MIN_RATIO);
    }

    public static boolean allFacesPass(Map<Face, FaceResult> results) {
        if (results.size() != Face.values().length) {
            return false;
        }
        return results.values().stream().allMatch(FaceResult::passed);
    }

    // ------------------------------------------------------------ ブロック種類・比率

    /** 計上できるブロックの種類数の上限。 */
    public static final int MAX_COUNTED_TYPES = 2;

    /** 計上対象のうち、最も多い1種類が占めるべき比率の下限。 */
    public static final double PRIMARY_TYPE_MIN_RATIO = 0.70;

    /** 有効判定ブロック数として必要な総数の下限。 */
    public static final int MIN_QUALIFYING_TOTAL = 1600;

    /** 種類ごとの集計1件。 */
    public record TypeTally(String material, int count) {
    }

    /**
     * ブロックの集計・種類の絞り込み・比率判定の結果。
     *
     * @param counted        計上対象として抽出された、使用数の多い順の上位 {@link #MAX_COUNTED_TYPES} 種
     * @param effectiveTotal counted の合計本数
     * @param primaryRatio   counted の中で最も多い1種類が占める比率
     * @param ratioPassed    主ブロック比率（70%以上）を満たすか
     * @param totalPassed    有効判定ブロック数が1,600個以上か
     */
    public record BlockJudgement(List<TypeTally> counted, long effectiveTotal, double primaryRatio,
                                 boolean ratioPassed, boolean totalPassed) {
        public boolean passed() {
            return ratioPassed && totalPassed;
        }
    }

    /**
     * 種類ごとの総数から、計上対象（使用数上位2種）を抽出し、70%ルール・1,600個ルールを判定する。
     *
     * <p>3種類以上ある場合、3種類目以降はカウント0として扱う——要件定義書のとおり、
     * 単純に上位2種だけを抜き出す（下位の種類を上位2種へ合算するようなことはしない）。
     *
     * @param counts 対象ブロックの種類名 → 個数
     */
    public static BlockJudgement judgeBlocks(Map<String, Integer> counts) {
        List<TypeTally> sorted = new ArrayList<>();
        counts.forEach((material, count) -> {
            if (count < 0) {
                throw new IllegalArgumentException("ブロック数が負である: " + material);
            }
            if (count > 0) {
                sorted.add(new TypeTally(material, count));
            }
        });
        sorted.sort((a, b) -> Integer.compare(b.count(), a.count()));
        List<TypeTally> counted = sorted.size() > MAX_COUNTED_TYPES
                ? List.copyOf(sorted.subList(0, MAX_COUNTED_TYPES))
                : List.copyOf(sorted);
        long effectiveTotal = counted.stream().mapToLong(TypeTally::count).sum();
        long primaryCount = counted.isEmpty() ? 0 : counted.get(0).count();
        double primaryRatio = effectiveTotal == 0 ? 0.0 : (double) primaryCount / effectiveTotal;
        boolean ratioPassed = !counted.isEmpty() && primaryRatio >= PRIMARY_TYPE_MIN_RATIO;
        boolean totalPassed = effectiveTotal >= MIN_QUALIFYING_TOTAL;
        return new BlockJudgement(counted, effectiveTotal, primaryRatio, ratioPassed, totalPassed);
    }

    // ------------------------------------------------------------ 配置制限

    /** 既設の認定駅舎から、これより離れていなければ新規設置不可（この距離「以内」は不可）。 */
    public static final double MIN_DISTANCE_TO_OTHER_STATION = 150.0;

    /** 最寄りの既存駅舎までの距離が、新規設置に十分か。 */
    public static boolean distanceAllowed(double distanceToNearestStation) {
        return distanceToNearestStation > MIN_DISTANCE_TO_OTHER_STATION;
    }

    // ------------------------------------------------------------ 総合判定

    /**
     * 駅舎認定の総合判定。
     *
     * @param nearestStationDistance 最寄りの既存駅舎までの距離。既存駅舎が無いなら
     *                                {@link Double#POSITIVE_INFINITY} を渡す
     */
    public record Certification(boolean spaceOk, boolean facesOk, BlockJudgement blocks,
                                boolean distanceOk, boolean certified) {
    }

    public static Certification certify(int outerWidth, int outerHeight, int outerDepth,
            Map<Face, FaceResult> faceResults, Map<String, Integer> blockCounts,
            double nearestStationDistance) {
        boolean spaceOk = fitsSpace(outerWidth, outerHeight, outerDepth);
        boolean facesOk = allFacesPass(faceResults);
        BlockJudgement blocks = judgeBlocks(blockCounts);
        boolean distanceOk = distanceAllowed(nearestStationDistance);
        boolean certified = spaceOk && facesOk && blocks.passed() && distanceOk;
        return new Certification(spaceOk, facesOk, blocks, distanceOk, certified);
    }

    /**
     * 総合判定の便利版。外寸から {@link #faceArea} で6面それぞれの面積を求めるところまで
     * 面倒を見る——呼ぶ側は「面ごとに対象ブロックが何個あったか」だけを渡せばよい。
     * 外寸が最小値より大きい駅舎でも、面ごとに正しい面積（天井・床／南北の壁／東西の壁で
     * それぞれ異なる）で判定する。
     *
     * @param qualifyingCountsByFace 面ごとの対象ブロック数。キーが無い面は0として扱う
     */
    public static Certification certifyBox(int outerWidth, int outerHeight, int outerDepth,
            Map<Face, Integer> qualifyingCountsByFace, Map<String, Integer> blockCounts,
            double nearestStationDistance) {
        Map<Face, FaceResult> faceResults = new java.util.EnumMap<>(Face.class);
        for (Face face : Face.values()) {
            int qualifying = qualifyingCountsByFace.getOrDefault(face, 0);
            int area = faceArea(face, outerWidth, outerHeight, outerDepth);
            faceResults.put(face, checkFace(face, qualifying, area));
        }
        return certify(outerWidth, outerHeight, outerDepth, faceResults, blockCounts,
                nearestStationDistance);
    }

    // ------------------------------------------------------------ 対象ブロック一覧（参考）

    /**
     * 認定可能ブロックの一覧（全17種、要件定義書の表記のまま）。<b>Bukkit の {@code Material}
     * への対応付けはここでは行わない</b>（クラス javadoc を参照）。
     */
    public static final List<String> BLOCK_TYPE_LABELS = List.of(
            "レンガ", "石レンガ", "模様入り石レンガ", "苔むした石レンガ", "エンドストーンレンガ",
            "クォーツレンガ", "ネザーレンガ", "模様入りのネザーレンガ", "赤いネザーレンガ",
            "プリズマリンレンガ", "磨かれたブラックストーンレンガ", "深層岩レンガ", "泥レンガ",
            "凝灰岩レンガ", "樹脂レンガ", "硫黄レンガ", "辰砂レンガ");
}
