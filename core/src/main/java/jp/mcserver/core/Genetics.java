package jp.mcserver.core;

/**
 * 育種システム（§26.1〜26.6）。作物と一般動物（牛・豚・オオカミ）を対象とする。
 * 馬（§26.7）は繁殖系統・形質継承の方式が大きく異なるため対象外（別途扱う）。
 *
 * <p>各個体は最大3つの形質スロットを持つ。形質値は0〜100で、0は「無形質」を意味する
 * （{@link #rank}）。値1以上を持つ個体・株を「育種」と呼ぶ。
 *
 * <p><b>継承</b>: 作物は同一系統を植え直しながら育てる対象のため、収穫のたびに植えた
 * 時点の値からドリフトする（{@link #cropDrift}）。動物は2個体を交配し、両親の平均値を
 * 基準にドリフトする（{@link #animalInherit}）。ドリフト幅（roll）そのものは呼び出し側の
 * 乱数を渡す——本クラスは決定的な計算のみを行う。
 *
 * <p><b>突然変異</b>: 作物の収穫時・動物の繁殖時とも{@value #MUTATION_RATE}（0.1%）で発生し、
 * 元は持っていなかった形質をランクⅠ帯（1〜10）で1つ獲得する。既存の形質は必ずしも
 * 継承されない。無形質からの突然変異が「育種」の起点になる。
 *
 * <p><b>歯止め</b>: 形質ドリフトによる一方的な上振れを、作物は不作イベント
 * （{@link #cropBlightRate}）、動物は瀕死イベント（{@link #nearDeathEventRate}）で
 * 押し戻す。いずれも1日2回判定される。
 *
 * <p><b>未実装の値</b>: 一般動物（牛・豚・オオカミ）の近親交配ペナルティは、仕様が
 * 「形質にペナルティを与える」「平均へ回帰する」と定性的に述べるのみで、馬（§26.7.6の
 * 血量18.75/25/31.25/37.5/50%表）のような具体的な数式・閾値を与えていないため、ここでは
 * 実装しない。同様に遺伝子改変（§26.6）の失敗率も、§22が「失敗率の設定・調整」を今後の
 * 実測課題として明記しており、確定値が無いため定数化していない。
 */
public final class Genetics {

    private Genetics() {}

    /** 形質値の下限・上限。 */
    public static final int VALUE_MIN = 0;
    public static final int VALUE_MAX = 100;

    /** 個体・株が持てる形質スロット数（§26.1）。 */
    public static final int TRAIT_SLOTS = 3;

    /**
     * 形質値からランク（0〜10。0は無形質、1〜10がⅠ〜Ⅹ）を求める（§26.1）。
     *
     * <p>Ⅰ=1〜10、Ⅱ〜Ⅸ=11〜90（10点刻み）、Ⅹ=90〜100。90はⅨ・Ⅹの境界に重なると
     * 仕様が明記しており実装時に確定するよう求めているため、ここでは90を「10点刻み」の
     * 並びと整合させてⅨ（=81〜90の帯の上端）に含め、Ⅹは91〜100とする（実測後に§22で
     * 見直す前提の暫定判断）。
     */
    public static int rank(int value) {
        requireValue(value);
        if (value == 0) {
            return 0;
        }
        return (value - 1) / 10 + 1;
    }

    /** 突然変異の発生率（作物の収穫時・動物の繁殖時で共通、§26.2・§26.3）。 */
    public static final double MUTATION_RATE = 0.001;

    /** 突然変異で獲得する形質値の範囲（ランクⅠ帯）。 */
    public static final int MUTATION_TRAIT_VALUE_MIN = 1;
    public static final int MUTATION_TRAIT_VALUE_MAX = 10;

    /** 形質ドリフトの範囲（作物の植え直し・動物の交配で共通、平均+3）。 */
    public static final int DRIFT_MIN = -2;
    public static final int DRIFT_MAX = 8;

    /**
     * 作物の収穫時の形質値（§26.2）。植えた時点の値に roll（{@link #DRIFT_MIN}〜
     * {@link #DRIFT_MAX}、ジョブツリーの上限適用時はさらに広い範囲）を加え、
     * 0〜100 に丸める。
     */
    public static int cropDrift(int plantedValue, int roll) {
        requireValue(plantedValue);
        return clamp(plantedValue + roll);
    }

    /**
     * 動物の交配で子に継承される形質値（§26.3）。両親の平均値に roll を加え、
     * 0〜100 に丸める。
     */
    public static int animalInherit(int parentA, int parentB, int roll) {
        requireValue(parentA);
        requireValue(parentB);
        int base = Math.floorDiv(parentA + parentB, 2);
        return clamp(base + roll);
    }

    /** 作物の不作イベント・動物の瀕死イベントとも、1日に判定される回数（§26.2・§26.3）。 */
    public static final int EVENT_CHECKS_PER_DAY = 2;

    /** 作物の不作イベントの発現率（§26.2）。 */
    public static final double CROP_BLIGHT_RATE = 0.10;

    /** 不作イベントの二次感染率と半径（§26.2）。連鎖は二次までで止まる。 */
    public static final double CROP_BLIGHT_SECONDARY_INFECTION_RATE = 0.50;
    public static final int CROP_BLIGHT_SECONDARY_RADIUS_BLOCKS = 3;

    /** 動物の瀕死イベントの基準発現率（§26.3）。 */
    public static final double NEAR_DEATH_BASE_RATE = 0.01;

    /** 対応する耐性形質がランク5（値41〜50）に達した場合の発現率（半減）。 */
    public static final double NEAR_DEATH_RESISTANT_RATE = 0.005;
    public static final int NEAR_DEATH_RESISTANT_RANK = 5;

    /** 耐性形質の値に応じた瀕死イベントの発現率（§26.3）。 */
    public static double nearDeathEventRate(int resistanceTraitValue) {
        return rank(resistanceTraitValue) >= NEAR_DEATH_RESISTANT_RANK
                ? NEAR_DEATH_RESISTANT_RATE
                : NEAR_DEATH_BASE_RATE;
    }

    private static int clamp(int value) {
        return Math.max(VALUE_MIN, Math.min(VALUE_MAX, value));
    }

    private static void requireValue(int value) {
        if (value < VALUE_MIN || value > VALUE_MAX) {
            throw new IllegalArgumentException("形質値が範囲外である: " + value);
        }
    }
}
