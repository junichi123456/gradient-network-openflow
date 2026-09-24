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
 *   <li>坂道の物理演算の代わりに、1ブロック上昇・水平移動ともブロック単位の
 *       離散的なスタミナ消費で表す（{@link #riseStaminaCost}・
 *       {@link #horizontalStaminaCost}）</li>
 *   <li>速さと頑丈さはトレードオフにする（{@link #toughnessAfterTradeoff}）</li>
 *   <li>血統は2世代・6頭（親2頭＋祖父母4頭）まで見る（{@link #related}）</li>
 *   <li>怪我はデイサイクル終了時、前日の累積疲労から一括判定する
 *       （{@link #injuryChanceFromFatigue}）</li>
 *   <li>速さ100は実速度17.0 m/秒に相当し（{@link #maxSpeedMps}、育種のみに適用。
 *       野生馬はバニラの自然な値をそのまま使う）、14.0 m/秒以上では強力な推進力
 *       により旋回が段階的に困難になる。旋回性は自身の最高速度の80%以上でのみ
 *       働き、100でこの影響を75%まで軽減する（{@link #effectiveInertiaEffect}）</li>
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

    /**
     * 速さの値（0〜100）を実速度（m/秒）へ変換する。0が下限、100が上限に対応する。
     *
     * <p><b>育種（ブリード品種）にのみ適用する。</b> 野生捕獲の馬は形質を持たず、
     * バニラの movement_speed 属性の自然な値（4.857〜14.57 m/秒）をそのまま使う。
     */
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

    // ---- ジャンプ（§26.7.2） ----

    /** 育種のジャンプ力は固定（個体差を持たせない）。 */
    public static final int BRED_JUMP_HEIGHT_BLOCKS = 2;

    // ---- スタミナ（§26.7.4） ----

    /**
     * スタミナの値（0〜100）に対する基準値・倍率。最大スタミナ = 101 + 値×0.35
     * （値100で136になる）。
     */
    public static final double MAX_STAMINA_BASE = 101.0;
    public static final double MAX_STAMINA_PER_VALUE = 0.35;

    /** スタミナの値から最大スタミナを求める。値100で136になる。 */
    public static double maxStamina(int staminaValue) {
        Genetics.rank(staminaValue); // 0〜100の範囲検証を兼ねる
        return MAX_STAMINA_BASE + staminaValue * MAX_STAMINA_PER_VALUE;
    }

    /** 1ブロック上昇（ジャンプ・段差の乗り越えとも）あたりの基準スタミナ消費。 */
    public static final double RISE_STAMINA_COST_BASE = 2.0;

    /**
     * 頑丈さ1につき、上昇時のスタミナ消費を軽減する割合（頑丈さ100で最大45%軽減）。
     * 「傾斜・登坂耐性」としての頑丈さの働きにあたる。
     */
    public static final double RISE_STAMINA_REDUCTION_PER_VALUE = 0.0045;

    /**
     * 1ブロック上昇あたりのスタミナ消費。移動速度によらず一律に発生し、頑丈さで
     * 軽減される（最大45%）。段差をジャンプなしで乗り越える場合も同じ消費とする。
     */
    public static double riseStaminaCost(int toughnessValue) {
        Genetics.rank(toughnessValue); // 0〜100の範囲検証を兼ねる
        double reduction = toughnessValue * RISE_STAMINA_REDUCTION_PER_VALUE;
        return RISE_STAMINA_COST_BASE * (1 - reduction);
    }

    /** 水平移動でスタミナを消費し始める、自身の最高速度に対する割合（80%以上）。 */
    public static final double HORIZONTAL_STAMINA_SPEED_THRESHOLD = 0.80;

    /** 水平移動1ブロックあたりのスタミナ消費（5ブロックで1.0＝0.2/ブロック）。 */
    public static final double HORIZONTAL_STAMINA_COST_PER_BLOCK = 0.2;

    /**
     * 水平移動によるスタミナ消費。自身の最高速度の80%以上でのみ発生する
     * （80%未満は消費なし）。
     */
    public static double horizontalStaminaCost(double blocksTraveled, double speedFraction) {
        if (blocksTraveled < 0) {
            throw new IllegalArgumentException("移動距離が負である: " + blocksTraveled);
        }
        return speedFraction >= HORIZONTAL_STAMINA_SPEED_THRESHOLD ? blocksTraveled * HORIZONTAL_STAMINA_COST_PER_BLOCK : 0.0;
    }

    /** スタミナの回復が始まる、自身の最高速度に対する割合の上限（40%以下）。 */
    public static final double STAMINA_RECOVERY_SPEED_THRESHOLD = 0.40;

    /**
     * その時点の速度でスタミナが回復するか。自身の最高速度の40%以下のときのみ
     * 回復する（回復量自体は§22で運用しながら定める）。
     */
    public static boolean staminaRecovering(double speedFraction) {
        return speedFraction <= STAMINA_RECOVERY_SPEED_THRESHOLD;
    }

    // ---- 累積疲労と怪我（§26.7.4） ----

    /** 累積疲労の上限。 */
    public static final double FATIGUE_CAP = 100.0;

    /** 1デイサイクルの終了時に回復する累積疲労。 */
    public static final double FATIGUE_DAILY_RECOVERY = 10.0;

    /** デイサイクル終了時、前日の累積疲労を回復させる（0を下回らない）。 */
    public static double recoverFatigueDaily(double fatigue) {
        return Math.max(0.0, fatigue - FATIGUE_DAILY_RECOVERY);
    }

    /** スタミナ超過後の走行で累積疲労が発生し始める、自身の最高速度に対する割合（70%以上）。 */
    public static final double FATIGUE_ACCUMULATION_SPEED_THRESHOLD = 0.70;

    /** スタミナ超過後の走行1ブロックあたりの累積疲労（10ブロックで6＝0.6/ブロック）。 */
    public static final double FATIGUE_ACCUMULATION_PER_BLOCK = 0.6;

    /**
     * 走行による累積疲労の増分。スタミナを使い切った状態（{@code staminaExhausted}）
     * で、なお自身の最高速度の70%以上を出している場合にのみ発生する。
     */
    public static double fatigueAccumulation(double blocksTraveled, double speedFraction, boolean staminaExhausted) {
        if (blocksTraveled < 0) {
            throw new IllegalArgumentException("移動距離が負である: " + blocksTraveled);
        }
        boolean accumulates = staminaExhausted && speedFraction >= FATIGUE_ACCUMULATION_SPEED_THRESHOLD;
        return accumulates ? blocksTraveled * FATIGUE_ACCUMULATION_PER_BLOCK : 0.0;
    }

    /**
     * 頑丈さ1につき、怪我判定に使う累積疲労を軽減する量（頑丈さ100で最大25点）。
     * 「足腰強度」としての頑丈さの働きにあたる。頑丈さが最大でも、累積疲労が
     * 上限（100）なら怪我判定に使う値は75まで残り、75%の確率で怪我が発生する
     * （＝最大でも25%の確率でしか怪我を避けられない）。
     */
    public static final double FATIGUE_TOUGHNESS_MITIGATION_PER_VALUE = 0.25;

    /**
     * デイサイクル終了時、前日の累積疲労と頑丈さから求める怪我の発生率（0〜100）。
     * この値をそのままパーセントとして扱う（累積疲労が上限かつ頑丈さ0なら
     * 確定で発生する）。
     */
    public static double injuryChanceFromFatigue(double fatigue, int toughnessValue) {
        if (fatigue < 0 || fatigue > FATIGUE_CAP) {
            throw new IllegalArgumentException("累積疲労が範囲外である: " + fatigue);
        }
        Genetics.rank(toughnessValue); // 0〜100の範囲検証を兼ねる
        double mitigated = fatigue - toughnessValue * FATIGUE_TOUGHNESS_MITIGATION_PER_VALUE;
        return Math.max(0.0, mitigated);
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

    /** 軽傷1回あたりの累積ダメージ。 */
    public static final double CUMULATIVE_DAMAGE_MINOR = 20.0;

    /** 重傷1回あたりの累積ダメージ。 */
    public static final double CUMULATIVE_DAMAGE_MAJOR = 35.0;

    /**
     * その段階の怪我による累積ダメージの増分。後遺症は単発の恒久ステータス
     * 低下（{@link #PERMANENT_STAT_LOSS_MIN}〜{@link #PERMANENT_STAT_LOSS_MAX}）
     * として別途扱うため、ここでは加算しない（0を返す）。
     */
    public static double cumulativeDamageFor(InjuryStage stage) {
        return switch (stage) {
            case MINOR -> CUMULATIVE_DAMAGE_MINOR;
            case MAJOR -> CUMULATIVE_DAMAGE_MAJOR;
            case PERMANENT -> 0.0;
        };
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
