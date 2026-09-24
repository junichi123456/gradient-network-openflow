package jp.mcserver.core;

import java.util.HashSet;
import java.util.Set;

/**
 * 馬の育成システム（§26.7、簡略化版）。
 *
 * <p>馬は他の家畜（§26.1・§26.3）と同じ形質システムをそのまま使う。耐性形質に加えて、
 * レースステータス4種（速さ・スタミナ・頑丈さ・旋回性）を持つ——いずれも
 * {@link Genetics} と同じ値0〜100・ランクⅠ〜Ⅹで表し、継承の基本式も
 * {@link Genetics#animalInherit} をそのまま使う。
 *
 * <p>旧版が持っていた「形質0〜100とパラメータ0〜200の二重体系」「毎Tickの坂道物理
 * （傾斜率・スタミナ消費・速度補正の3式）」「4世代16頭ぶんの血統ツリーと5段階の血量
 * ペナルティ表」は実装コストに見合わないため単純化したが、競技としての手触り
 * （地形との駆け引き・特化の代償・血統の戦略性・スタミナ管理のリスク）は次の4点で
 * 保った。
 *
 * <ul>
 *   <li>地形は毎Tickでなく、坂の区間に入った時点の1回判定にする
 *       （{@link #speedMultiplier}・{@link #staminaCostMultiplier}）</li>
 *   <li>速さと頑丈さはトレードオフにする（{@link #toughnessAfterTradeoff}）</li>
 *   <li>血統は2世代・6頭（親2頭＋祖父母4頭）まで見る（{@link #related}）</li>
 *   <li>怪我の発生率はその時点のスタミナ残量に連動する（{@link #injuryRate}）</li>
 *   <li>速さ100は実速度17.0 m/秒に相当し、14.0 m/秒以上では強力な推進力により
 *       旋回が段階的に困難になる。旋回性は自身の最高速度の80%以上でのみ働き、
 *       100でこの影響を75%まで軽減する（{@link #maxSpeedMps}・
 *       {@link #effectiveInertiaEffect}）</li>
 * </ul>
 */
public final class HorseTraining {

    private HorseTraining() {}

    /** レースステータスの種類（§26.7.1）。値・ランクの扱いは{@link Genetics}と共通。 */
    public enum RaceStat { SPEED, STAMINA, TOUGHNESS, TURNING }

    /** 仔馬が成体になるまでの時間（分）。 */
    public static final int MATURATION_MINUTES = 14;

    // ---- 血統と近親交配（§26.7.3） ----

    /**
     * 個体の血統。親2頭と祖父母4頭のIDのみを保持し（2世代・計6頭）、それより遡らない。
     * 不明な祖先は {@code null} でよい。
     */
    public record Parentage(
            String parentAId, String parentBId,
            String grandparentAAId, String grandparentABId,
            String grandparentBAId, String grandparentBBId) {

        /** 記録されている祖先のIDの集合（{@code null} を除く）。 */
        public Set<String> ancestors() {
            Set<String> ids = new HashSet<>();
            for (String id : new String[] {parentAId, parentBId,
                    grandparentAAId, grandparentABId, grandparentBAId, grandparentBBId}) {
                if (id != null) {
                    ids.add(id);
                }
            }
            return ids;
        }
    }

    /**
     * 交配させる2頭が近親交配にあたるか。片方がもう片方の親である場合、または
     * 2世代・6頭以内に共通の祖先を持つ場合に近親交配とする。
     */
    public static boolean related(String horseAId, Parentage a, String horseBId, Parentage b) {
        if (isParentOf(horseAId, b) || isParentOf(horseBId, a)) {
            return true;
        }
        for (String id : a.ancestors()) {
            if (b.ancestors().contains(id)) {
                return true;
            }
        }
        return false;
    }

    private static boolean isParentOf(String candidateId, Parentage of) {
        return candidateId.equals(of.parentAId()) || candidateId.equals(of.parentBId());
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

    // ---- 特化のトレードオフ（§26.7.1） ----

    /** 速さがこの値を超えた分だけ、頑丈さから差し引く（「サラブレッド化」の代償）。 */
    public static final int SPEED_TOUGHNESS_TRADEOFF_THRESHOLD = 70;

    /** 超過分に対する頑丈さの減少率。 */
    public static final double SPEED_TOUGHNESS_TRADEOFF_RATE = 0.3;

    /**
     * 継承した速さの値に応じて、頑丈さの値を補正する（全ステータス高の「万能馬」を
     * 作れないようにするトレードオフ）。
     */
    public static int toughnessAfterTradeoff(int inheritedToughness, int inheritedSpeed) {
        int excess = Math.max(0, inheritedSpeed - SPEED_TOUGHNESS_TRADEOFF_THRESHOLD);
        int penalty = (int) Math.round(excess * SPEED_TOUGHNESS_TRADEOFF_RATE);
        return Math.max(Genetics.VALUE_MIN, inheritedToughness - penalty);
    }

    // ---- 速さの実数値換算と高速域の旋回困難（§26.7.1） ----

    /**
     * 速さ0のときの実速度（m/秒）。バニラの movement_speed 属性が取り得る下限
     * （0.1125）に相当する。
     */
    public static final double SPEED_TRAIT_MIN_MPS = 4.857;

    /**
     * 速さ100のときの実速度（m/秒）の上限。バニラの movement_speed 属性の自然な
     * 上限（0.3375、14.57 m/秒相当）を超えて設定する。
     */
    public static final double SPEED_TRAIT_MAX_MPS = 17.0;

    /** 速さの値（0〜100）を実速度（m/秒）へ変換する。0が下限、100が上限に対応する。 */
    public static double maxSpeedMps(int speedValue) {
        Genetics.rank(speedValue); // 0〜100の範囲検証を兼ねる
        return SPEED_TRAIT_MIN_MPS + (speedValue / 100.0) * (SPEED_TRAIT_MAX_MPS - SPEED_TRAIT_MIN_MPS);
    }

    /** この速度（m/秒）から、非常に強力な推進力により旋回が困難になり始める。 */
    public static final double INERTIA_THRESHOLD_MPS = 14.0;

    /** 旋回性が効き始める、自身の最高速度に対する割合（80%以上）。 */
    public static final double TURNING_RELEVANT_SPEED_FRACTION = 0.8;

    /** 旋回性100のとき、慣性の影響を削減できる最大割合（75%）。 */
    public static final double TURNING_MAX_MITIGATION = 0.75;

    /**
     * 現在速度による、旋回困難（慣性の影響）の基礎的な強さ（0〜1）。
     * {@value #INERTIA_THRESHOLD_MPS} m/秒未満では0、{@link #SPEED_TRAIT_MAX_MPS}
     * （全個体に共通の速度上限）に向けて段階的に強くなる。
     */
    public static double baseInertiaEffect(double currentSpeedMps) {
        if (currentSpeedMps < INERTIA_THRESHOLD_MPS) {
            return 0.0;
        }
        double clamped = Math.min(currentSpeedMps, SPEED_TRAIT_MAX_MPS);
        return (clamped - INERTIA_THRESHOLD_MPS) / (SPEED_TRAIT_MAX_MPS - INERTIA_THRESHOLD_MPS);
    }

    /**
     * 旋回性が効いているか。自身の最高速度の{@value #TURNING_RELEVANT_SPEED_FRACTION}
     * 倍以上を出している場合にのみ、旋回性による軽減が働く。
     */
    public static boolean turningStatActive(double currentSpeedMps, double horseMaxSpeedMps) {
        return currentSpeedMps >= horseMaxSpeedMps * TURNING_RELEVANT_SPEED_FRACTION;
    }

    /** 旋回性の値による、慣性影響の軽減率（0〜{@value #TURNING_MAX_MITIGATION}）。 */
    public static double turningMitigation(int turningValue) {
        Genetics.rank(turningValue); // 0〜100の範囲検証を兼ねる
        return (turningValue / 100.0) * TURNING_MAX_MITIGATION;
    }

    /**
     * 旋回性による軽減を適用した、最終的な旋回困難（慣性の影響、0〜1）。
     * 自身の最高速度の80%未満では旋回性は働かない（{@link #turningStatActive}）。
     * ただし速度上限（{@value #SPEED_TRAIT_MAX_MPS} m/秒）が全個体で共通のため、
     * {@value #INERTIA_THRESHOLD_MPS} m/秒（80%相当は13.6 m/秒）に達している時点で
     * 常にこの条件は満たされる。
     */
    public static double effectiveInertiaEffect(double currentSpeedMps, double horseMaxSpeedMps, int turningValue) {
        double base = baseInertiaEffect(currentSpeedMps);
        if (base == 0.0) {
            return 0.0;
        }
        boolean active = turningStatActive(currentSpeedMps, horseMaxSpeedMps);
        double mitigation = active ? turningMitigation(turningValue) : 0.0;
        return base * (1 - mitigation);
    }

    // ---- 地形との駆け引き（§26.7.4） ----

    /**
     * 地形の種別。毎Tickの傾斜計算は行わず、坂の区間に入った時点でこの分類を
     * 1回だけ判定する（判定自体はplugin側が行い、coreは区分だけを受け取る）。
     */
    public enum Terrain { FLAT, GENTLE_SLOPE, STEEP_SLOPE }

    private static double baseStaminaCostMultiplier(Terrain terrain) {
        return switch (terrain) {
            case FLAT -> 1.0;
            case GENTLE_SLOPE -> 2.0;
            case STEEP_SLOPE -> 4.0;
        };
    }

    private static double baseSpeedMultiplier(Terrain terrain) {
        return switch (terrain) {
            case FLAT -> 1.0;
            case GENTLE_SLOPE -> 0.9;
            case STEEP_SLOPE -> 0.7;
        };
    }

    /** 頑丈さのランク1段階あたり、地形の負荷を軽減する割合（ランクⅩで最大50%）。 */
    public static final double TOUGHNESS_TERRAIN_MITIGATION_PER_RANK = 0.05;

    /** その地形区間でのスタミナ消費倍率。頑丈さが高いほど坂の負荷が軽い。 */
    public static double staminaCostMultiplier(Terrain terrain, int toughnessValue) {
        int rank = Genetics.rank(toughnessValue);
        double base = baseStaminaCostMultiplier(terrain);
        double reduction = (base - 1.0) * (rank * TOUGHNESS_TERRAIN_MITIGATION_PER_RANK);
        return base - reduction;
    }

    /** その地形区間での速度倍率。頑丈さが高いほど坂による減速が小さい。 */
    public static double speedMultiplier(Terrain terrain, int toughnessValue) {
        int rank = Genetics.rank(toughnessValue);
        double base = baseSpeedMultiplier(terrain);
        double recovery = (1.0 - base) * (rank * TOUGHNESS_TERRAIN_MITIGATION_PER_RANK);
        return base + recovery;
    }

    // ---- 怪我（§26.7.4） ----

    /** 危険な行動1回あたりの基準怪我発生率（スタミナが満タンの場合）。 */
    public static final double BASE_INJURY_RATE = 0.05;

    /** 頑丈さランク1段階あたりの発生率軽減幅（ランクⅩで最大50%軽減）。 */
    public static final double TOUGHNESS_REDUCTION_PER_RANK = 0.05;

    /** スタミナが空の状態で危険な行動を起こした場合の倍率上限（満タン時の3倍）。 */
    public static final double STAMINA_RISK_MAX_MULTIPLIER = 3.0;

    /**
     * 頑丈さとその時点のスタミナ残量に応じた怪我発生率。スタミナが少ないほど、
     * 同じ危険な行動でも発生率が上がる——「疲弊した馬を押すか、落とすか」の
     * 駆け引きを生む。
     *
     * @param staminaFraction 0（空）〜1（満タン）のスタミナ残量比
     */
    public static double injuryRate(int toughnessValue, double staminaFraction) {
        if (staminaFraction < 0 || staminaFraction > 1) {
            throw new IllegalArgumentException("スタミナ残量比が範囲外である: " + staminaFraction);
        }
        int rank = Genetics.rank(toughnessValue);
        double toughnessAdjusted = BASE_INJURY_RATE * (1 - rank * TOUGHNESS_REDUCTION_PER_RANK);
        double staminaMultiplier = 1 + (1 - staminaFraction) * (STAMINA_RISK_MAX_MULTIPLIER - 1);
        return toughnessAdjusted * staminaMultiplier;
    }

    /** 怪我の段階（§26.7.4）。 */
    public enum InjuryStage { MINOR, MAJOR, PERMANENT }

    private static final double MINOR_UPPER = 0.70;
    private static final double MAJOR_UPPER = 0.95;

    /**
     * 怪我が発生した際の重症度。軽傷70%・重傷25%・後遺症5%（初期値・§22で調整）。
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
