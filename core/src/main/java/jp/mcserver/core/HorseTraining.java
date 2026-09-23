package jp.mcserver.core;

/**
 * 馬の育成システム（§26.7、簡略化版）。
 *
 * <p>馬は他の家畜（§26.1・§26.3）と同じ形質システムをそのまま使う。耐性形質に加えて、
 * レースステータス4種（速さ・スタミナ・頑丈さ・旋回性）を持つ——いずれも
 * {@link Genetics} と同じ値0〜100・ランクⅠ〜Ⅹで表し、継承も{@link Genetics#animalInherit}
 * をそのまま使う。
 *
 * <p>旧版が持っていた「形質0〜100とパラメータ0〜200の二重体系」「毎Tickの坂道物理
 * （傾斜率・スタミナ消費・速度補正の3式）」「4世代16頭ぶんの血統ツリーと5段階の血量
 * ペナルティ表」は、実装コストに見合わないため簡略化した。
 *
 * <p><b>近親交配</b>: 血統ツリーは持たず、各個体は直近の親2頭のIDだけを記録する
 * （{@link #related}）。親を共有する2頭を交配させると、継承時のブレを反転させる
 * （{@link #inbredDriftRoll}）——通常の交配式を鏡合わせに使うだけで、新しい式は増やさない。
 *
 * <p><b>怪我</b>: 毎Tickの物理判定の代わりに、危険な行動が起きたという事実だけを
 * plugin側から受け取り、その時点で1回だけ判定する（{@link #injuryRate}・
 * {@link #injurySeverity}）。頑丈さのランクが高いほど発生率が下がる。
 */
public final class HorseTraining {

    private HorseTraining() {}

    /** レースステータスの種類（§26.7.1）。値・ランクの扱いは{@link Genetics}と共通。 */
    public enum RaceStat { SPEED, STAMINA, TOUGHNESS, TURNING }

    /** 仔馬が成体になるまでの時間（分）。 */
    public static final int MATURATION_MINUTES = 14;

    /** 個体の血統。直近の親2頭のIDのみを保持し、それより遡らない（§26.7.3）。 */
    public record Parentage(String parentAId, String parentBId) {}

    /**
     * 交配させる2頭が近親交配にあたるか。片方がもう片方の親である場合、または
     * 両方が親を共有している（半兄弟・全兄弟）場合に近親交配とする。
     */
    public static boolean related(String horseAId, Parentage horseA, String horseBId, Parentage horseB) {
        if (isParentOf(horseAId, horseB) || isParentOf(horseBId, horseA)) {
            return true;
        }
        return sharesParent(horseA, horseB);
    }

    private static boolean isParentOf(String candidateId, Parentage of) {
        return candidateId.equals(of.parentAId()) || candidateId.equals(of.parentBId());
    }

    private static boolean sharesParent(Parentage a, Parentage b) {
        if (a.parentAId() == null && a.parentBId() == null) {
            return false;
        }
        return equalsNonNull(a.parentAId(), b.parentAId()) || equalsNonNull(a.parentAId(), b.parentBId())
                || equalsNonNull(a.parentBId(), b.parentAId()) || equalsNonNull(a.parentBId(), b.parentBId());
    }

    private static boolean equalsNonNull(String x, String y) {
        return x != null && x.equals(y);
    }

    /**
     * 継承時のブレを近親交配かどうかに応じて返す（§26.7.3）。
     * 通常は{@link Genetics#DRIFT_MIN}〜{@link Genetics#DRIFT_MAX}（平均+3）、
     * 近親交配ならこれを反転した範囲（平均-3）とする。
     *
     * @param roll {@link Genetics#DRIFT_MIN}〜{@link Genetics#DRIFT_MAX} の乱数（呼び出し側が用意する）
     */
    public static int inbredDriftRoll(int roll, boolean inbred) {
        if (roll < Genetics.DRIFT_MIN || roll > Genetics.DRIFT_MAX) {
            throw new IllegalArgumentException("rollが範囲外である: " + roll);
        }
        return inbred ? -roll : roll;
    }

    /** 危険な行動1回あたりの基準怪我発生率（§26.7.4）。 */
    public static final double BASE_INJURY_RATE = 0.05;

    /** 頑丈さランク1段階あたりの発生率軽減幅（ランクⅩで最大50%軽減）。 */
    public static final double TOUGHNESS_REDUCTION_PER_RANK = 0.05;

    /** 頑丈さの値に応じた怪我発生率。 */
    public static double injuryRate(int toughnessValue) {
        int rank = Genetics.rank(toughnessValue);
        return BASE_INJURY_RATE * (1 - rank * TOUGHNESS_REDUCTION_PER_RANK);
    }

    /** 怪我の段階（§26.7.4）。 */
    public enum InjuryStage { MINOR, MAJOR, PERMANENT }

    /** 怪我発生時、各段階になる確率の閾値（累積）。軽傷70%・重傷25%・後遺症5%。 */
    private static final double MINOR_UPPER = 0.70;
    private static final double MAJOR_UPPER = 0.95;

    /**
     * 怪我が発生した際の重症度。
     *
     * @param roll 0以上1未満の乱数（呼び出し側が用意する）
     */
    public static InjuryStage injurySeverity(double roll) {
        if (roll < 0 || roll >= 1) {
            throw new IllegalArgumentException("rollが範囲外である: " + roll);
        }
        if (roll < MINOR_UPPER) {
            return InjuryStage.MINOR;
        }
        return roll < MAJOR_UPPER ? InjuryStage.MAJOR : InjuryStage.PERMANENT;
    }

    /** 軽傷時の移動速度倍率（−30%）。 */
    public static final double MINOR_SPEED_MULTIPLIER = 0.70;

    /** 後遺症時の速さ・スタミナの恒久低下率の範囲（5〜10%）。 */
    public static final double PERMANENT_STAT_LOSS_MIN = 0.05;
    public static final double PERMANENT_STAT_LOSS_MAX = 0.10;

    /** その段階で騎乗できるか（重傷のみ不可）。 */
    public static boolean ridable(InjuryStage stage) {
        return stage != InjuryStage.MAJOR;
    }
}
