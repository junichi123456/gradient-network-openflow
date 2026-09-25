package jp.mcserver.core;

import java.util.EnumMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * 馬の育成システム（§26.7）。
 *
 * <p><b>改訂の経緯</b>: 旧版は旋回性・慣性による高速域の操作性低下、地形起伏ごとの
 * 離散的なスタミナ消費、ジャンプ力2ブロック固定などを持っていた。この複雑さは
 * 競馬専用ワールド（§27）へ役割ごと移し、オーバーワールドは**5属性のポイント
 * 振り分け制**に一本化した。
 *
 * <ul>
 *   <li>レースステータスは{@link RaceStat}の5種。値0〜100で、5属性の合計は
 *       個体ごとに決まる{@link #inheritedCap 配分ポイント上限}を超えられない
 *       （§26.7.1）。特化と代償は、個別のトレードオフ式ではなく<b>配分そのもの</b>
 *       が担う</li>
 *   <li>配分ポイント上限は、旧版の血統判定（{@link #related}、2世代・6頭）を
 *       そのまま流用し、両親の上限の平均値に{@link Genetics}と同じドリフト
 *       （{@link #inheritedCap}）を加えて決まる（§26.7.3）</li>
 *   <li>頑丈さは<b>健康ステータス</b>に改称し、5属性とは別枠の遺伝形質として
 *       維持する。怪我しやすさ（{@link #injuryChanceFromFatigue}）とスタミナ
 *       回復速度（{@link #staminaRecoveryRate}）の両方を左右する（§26.7.4）</li>
 *   <li>怪我はデイサイクル終了時、前日の累積疲労から一括判定する
 *       （{@link #injuryChanceFromFatigue}、§26.7.6）</li>
 *   <li>パワーはジャンプ力（{@link #jumpHeightBlocks}）と登坂時の減速軽減
 *       （{@link #uphillSpeedMultiplier}）、根性はスタミナ切れ時の踏ん張り
 *       （{@link #gutsFatigueAvoidanceChance}）に効果を持つ。賢さは
 *       オーバーワールドでは効果を持たず、競馬専用ワールドのレース展開AI
 *       （§27.4）でのみ働く</li>
 *   <li>性格（気性）は5属性・健康ステータスとは別枠の遺伝形質。気性が荒いほど
 *       調教の成功率（{@link #trainingSuccessRate}）が下がるが、<b>競馬専用ワールドで
 *       レースに敗北した際</b>に特性{@link SpecialTrait}を獲得できる確率
 *       （{@link #traitAcquisitionChanceOnRaceDefeat}）が上がる（§26.7.7）。
 *       闘争心は直近5レースで自分に勝った馬が出走するレースでパワー+10
 *       （{@link #fightingSpiritActive}・{@link #fightingSpiritPowerBonus}）、
 *       本番得意は重賞のレースでスピード+5（{@link #peakPerformerSpeedBonus}）を
 *       もたらす（§27.7、いずれも競馬専用ワールド限定）</li>
 *   <li>ラストスパートは、残りスタミナが閾値（根性0で15%、根性100で20%）以下に
 *       なると入る（{@link #spurtActive}）。スタミナ消費が2倍
 *       （{@link #spurtStaminaConsumptionMultiplier}）になる代わり、最高速度を
 *       1.1倍まで出せる（{@link #spurtSpeedMultiplier}）。スタミナがゴール前で
 *       尽きるか、スパートに入る間もなく終わるかが、距離適性（§27.3）のシグナルに
 *       なる</li>
 *   <li>賢さはレース中のスタミナ消費全般に、賢さ0で1.1倍・賢さ100で0.9倍
 *       （最大効率）となる倍率を掛ける（{@link #wisdomStaminaEfficiencyMultiplier}、
 *       §27.4）。「ゴール前で尽きないようペース配分を調整する」という役割は、
 *       行動を動的に変えるロジックではなく、この燃費の差として実装した</li>
 *   <li>出生から実時間24時間で1歳（調教可能、{@link #trainable}）、48時間で2歳
 *       （レース出走可能）になる。1シーズン＝4週間＝4スプリットのうち、3歳馬
 *       クラシック路線（大地三冠・花冠三冠）はスプリット3・4にのみ出走できる
 *       （{@link #classicRaceEligible}）。古馬・距離別シリーズは2歳に達していれば
 *       スプリットを問わない（{@link #openRaceEligible}、§27.9）</li>
 * </ul>
 */
public final class HorseTraining {

    private HorseTraining() {}

    /**
     * レースステータスの種類（§26.7.1）。値の扱いは{@link Genetics}と共通（0〜100）。
     * WISDOM はオーバーワールドでは効果を持たず、競馬専用ワールドのレース展開AI
     * （§27.4）でのみ働く。
     */
    public enum RaceStat { SPEED, STAMINA, POWER, GUTS, WISDOM }

    /** レースステータス1つあたりの値の下限・上限（§26.7.1）。 */
    public static final int STAT_MIN = Genetics.VALUE_MIN;
    public static final int STAT_MAX = Genetics.VALUE_MAX;

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
     * 近親交配ならこれを反転した範囲（平均-3）とする。配分ポイント上限
     * （{@link #inheritedCap}）にも同じブレを使う。
     *
     * @param roll {@link Genetics#DRIFT_MIN}〜{@link Genetics#DRIFT_MAX} の乱数（呼び出し側が用意する）
     */
    public static int inbredDriftRoll(int roll, boolean inbred) {
        if (roll < Genetics.DRIFT_MIN || roll > Genetics.DRIFT_MAX) {
            throw new IllegalArgumentException("rollが範囲外である: " + roll);
        }
        return inbred ? -roll : roll;
    }

    // ---- 配分ポイント上限の継承（§26.7.3） ----

    /** 配分ポイント上限の範囲。 */
    public static final int CAP_MIN = 90;
    public static final int CAP_MAX = 120;

    /**
     * 配分ポイントの合計上限を、両親の上限の平均値に roll（通常は
     * {@link Genetics#DRIFT_MIN}〜{@link Genetics#DRIFT_MAX}、近親交配なら
     * {@link #inbredDriftRoll} で反転した範囲）を加えて求める。{@link #CAP_MIN}〜
     * {@link #CAP_MAX} に収める。
     */
    public static int inheritedCap(int parentACap, int parentBCap, int roll) {
        requireCap(parentACap);
        requireCap(parentBCap);
        int base = Math.floorDiv(parentACap + parentBCap, 2);
        return clampCap(base + roll);
    }

    private static int clampCap(int value) {
        return Math.max(CAP_MIN, Math.min(CAP_MAX, value));
    }

    private static void requireCap(int cap) {
        if (cap < CAP_MIN || cap > CAP_MAX) {
            throw new IllegalArgumentException("配分ポイント上限が範囲外である: " + cap);
        }
    }

    // ---- 5属性の配分（§26.7.1〜26.7.3） ----

    /** 配分（5属性それぞれの値）の合計。 */
    public static int allocationTotal(Map<RaceStat, Integer> allocation) {
        int total = 0;
        for (RaceStat stat : RaceStat.values()) {
            total += requireStatValue(allocation.getOrDefault(stat, 0));
        }
        return total;
    }

    /** 配分の合計が、与えられた配分ポイント上限に収まっているか。 */
    public static boolean isValidAllocation(Map<RaceStat, Integer> allocation, int cap) {
        requireCap(cap);
        return allocationTotal(allocation) <= cap;
    }

    /**
     * 配分の合計が上限を超えている場合、上限に収まるよう按分して縮小する
     * （§26.7.3「合計が上限を超える場合は、上限に収まるよう按分して縮小する」）。
     * 上限以内であれば、そのままの配分を返す。
     *
     * <p>端数は {@link RaceStat} の宣言順で切り捨てていき、最後の属性
     * （{@link RaceStat#WISDOM}）に残りをすべて割り当てることで、合計が
     * ちょうど上限に一致するようにする。
     */
    public static Map<RaceStat, Integer> scaleToCap(Map<RaceStat, Integer> allocation, int cap) {
        requireCap(cap);
        int total = allocationTotal(allocation);
        Map<RaceStat, Integer> result = new EnumMap<>(RaceStat.class);
        if (total <= cap) {
            for (RaceStat stat : RaceStat.values()) {
                result.put(stat, allocation.getOrDefault(stat, 0));
            }
            return result;
        }
        RaceStat[] stats = RaceStat.values();
        int remaining = cap;
        for (int i = 0; i < stats.length; i++) {
            RaceStat stat = stats[i];
            if (i == stats.length - 1) {
                result.put(stat, remaining);
            } else {
                int scaled = (int) Math.floor(allocation.getOrDefault(stat, 0) * ((double) cap / total));
                result.put(stat, scaled);
                remaining -= scaled;
            }
        }
        return result;
    }

    /**
     * 産駒誕生時の配分を、両親の配分から通常の交配式（{@link Genetics#animalInherit}、
     * §26.3）で暫定決定し、必要なら{@link #scaleToCap}で上限に収める（§26.7.3）。
     */
    public static Map<RaceStat, Integer> inheritAllocation(
            Map<RaceStat, Integer> parentA, Map<RaceStat, Integer> parentB,
            Map<RaceStat, Integer> rolls, int cap) {
        Map<RaceStat, Integer> raw = new EnumMap<>(RaceStat.class);
        for (RaceStat stat : RaceStat.values()) {
            int inherited = Genetics.animalInherit(
                    parentA.getOrDefault(stat, 0), parentB.getOrDefault(stat, 0),
                    rolls.getOrDefault(stat, 0));
            raw.put(stat, inherited);
        }
        return scaleToCap(raw, cap);
    }

    private static int requireStatValue(int value) {
        if (value < STAT_MIN || value > STAT_MAX) {
            throw new IllegalArgumentException("レースステータスの値が範囲外である: " + value);
        }
        return value;
    }

    // ---- パワー：ジャンプ力（§26.7.5） ----

    /** パワー0のときのジャンプ力（ブロック）。 */
    public static final double JUMP_HEIGHT_BASE_BLOCKS = 1.0;

    /** パワー1につき加算されるジャンプ力（パワー100で+1ブロック＝旧版の2ブロック相当）。 */
    public static final double JUMP_HEIGHT_PER_POWER_VALUE = 0.01;

    /** パワーの値からジャンプ力（ブロック）を求める。旧版の「2ブロックで固定」を廃止した置き換え。 */
    public static double jumpHeightBlocks(int powerValue) {
        requireStatValue(powerValue);
        return JUMP_HEIGHT_BASE_BLOCKS + powerValue * JUMP_HEIGHT_PER_POWER_VALUE;
    }

    // ---- パワー：登坂の減速軽減（§26.7.5） ----

    /** 登坂時の移動速度低下（バニラ相当の基準、パワー0で30%低下）。 */
    public static final double UPHILL_SPEED_PENALTY_BASE = 0.30;

    /** パワー1につき軽減される登坂時の速度低下（パワー100で低下を完全に軽減）。 */
    public static final double UPHILL_SPEED_PENALTY_MITIGATION_PER_POWER_VALUE = 0.003;

    /**
     * パワーの値から、登坂時の移動速度倍率（0〜1）を求める。パワー0でバニラ相当の
     * 基準どおり{@value #UPHILL_SPEED_PENALTY_BASE}（30%）低下し、パワー100で
     * 低下なし（倍率1.0）まで軽減される。
     */
    public static double uphillSpeedMultiplier(int powerValue) {
        requireStatValue(powerValue);
        double penalty = Math.max(0.0,
                UPHILL_SPEED_PENALTY_BASE - powerValue * UPHILL_SPEED_PENALTY_MITIGATION_PER_POWER_VALUE);
        return 1.0 - penalty;
    }

    // ---- 健康ステータス（旧・頑丈さ、§26.7.4） ----

    /**
     * 健康ステータス1につき、怪我判定に使う累積疲労を軽減する量（健康ステータス100で
     * 最大25点）。健康ステータスが最大でも、累積疲労が上限（100）なら怪我判定に使う
     * 値は75まで残り、75%の確率で怪我が発生する（＝最大でも25%の確率でしか怪我を
     * 避けられない）。
     */
    public static final double FATIGUE_HEALTH_MITIGATION_PER_VALUE = 0.25;

    /**
     * デイサイクル終了時、前日の累積疲労と健康ステータスから求める怪我の発生率
     * （0〜100）。この値をそのままパーセントとして扱う（累積疲労が上限かつ
     * 健康ステータス0なら確定で発生する）。
     */
    public static double injuryChanceFromFatigue(double fatigue, int healthStatusValue) {
        if (fatigue < 0 || fatigue > FATIGUE_CAP) {
            throw new IllegalArgumentException("累積疲労が範囲外である: " + fatigue);
        }
        requireStatValue(healthStatusValue);
        double mitigated = fatigue - healthStatusValue * FATIGUE_HEALTH_MITIGATION_PER_VALUE;
        return Math.max(0.0, mitigated);
    }

    /** 健康ステータス0のときのスタミナ回復速度（基準値）。 */
    public static final double STAMINA_RECOVERY_RATE_BASE = 5.0;

    /** 健康ステータス1につき加算されるスタミナ回復速度。 */
    public static final double STAMINA_RECOVERY_RATE_PER_HEALTH_VALUE = 0.05;

    /** 健康ステータスの値からスタミナ回復速度を求める（§26.7.4「スタミナ管理」）。 */
    public static double staminaRecoveryRate(int healthStatusValue) {
        requireStatValue(healthStatusValue);
        return STAMINA_RECOVERY_RATE_BASE + healthStatusValue * STAMINA_RECOVERY_RATE_PER_HEALTH_VALUE;
    }

    // ---- スタミナ（§26.7.6） ----

    /**
     * スタミナの値（0〜100）に対する基準値・倍率。最大スタミナ = 101 + 値×0.35
     * （値100で136になる。旧版の式を出発点として維持する）。
     */
    public static final double MAX_STAMINA_BASE = 101.0;
    public static final double MAX_STAMINA_PER_VALUE = 0.35;

    /** スタミナの値から最大スタミナを求める。値100で136になる。 */
    public static double maxStamina(int staminaValue) {
        requireStatValue(staminaValue);
        return MAX_STAMINA_BASE + staminaValue * MAX_STAMINA_PER_VALUE;
    }

    // ---- 根性：累積疲労の回避（§26.7.6） ----

    /** 根性100のとき、累積疲労の蓄積を回避できる確率の上限。 */
    public static final double GUTS_FATIGUE_AVOIDANCE_MAX = 0.30;

    /** 根性1につき加算される、累積疲労の蓄積を回避できる確率。 */
    public static final double GUTS_FATIGUE_AVOIDANCE_PER_VALUE = GUTS_FATIGUE_AVOIDANCE_MAX / STAT_MAX;

    /**
     * 根性の値から、本来なら蓄積するはずの累積疲労を踏ん張って回避できる確率を求める
     * （§26.7.6「根性による軽減」）。
     */
    public static double gutsFatigueAvoidanceChance(int gutsValue) {
        requireStatValue(gutsValue);
        return gutsValue * GUTS_FATIGUE_AVOIDANCE_PER_VALUE;
    }

    // ---- 累積疲労（§26.7.6） ----

    /** 累積疲労の上限。 */
    public static final double FATIGUE_CAP = 100.0;

    /** 1デイサイクルの終了時に回復する累積疲労。 */
    public static final double FATIGUE_DAILY_RECOVERY = 10.0;

    /** デイサイクル終了時、前日の累積疲労を回復させる（0を下回らない）。 */
    public static double recoverFatigueDaily(double fatigue) {
        return Math.max(0.0, fatigue - FATIGUE_DAILY_RECOVERY);
    }

    /**
     * スタミナを使い切った状態でなお運動を続けた場合の、累積疲労の増分
     * （具体的な蓄積量は実測後に§22で定める出発点）。
     */
    public static final double FATIGUE_ACCUMULATION_PER_EXERTION = 6.0;

    /**
     * 運動による累積疲労の増分。スタミナを使い切った状態（{@code staminaExhausted}）
     * で、なお運動を続けた（{@code stillExerting}）場合にのみ発生する。
     */
    public static double fatigueAccumulation(boolean staminaExhausted, boolean stillExerting) {
        return (staminaExhausted && stillExerting) ? FATIGUE_ACCUMULATION_PER_EXERTION : 0.0;
    }

    // ---- 怪我（§26.7.6） ----

    /** 怪我の段階（§26.7.6）。 */
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

    /** 後遺症時のスピード・スタミナの恒久低下率の範囲（5〜10%）。 */
    public static final double PERMANENT_STAT_LOSS_MIN = 0.05;
    public static final double PERMANENT_STAT_LOSS_MAX = 0.10;

    /** その段階で騎乗できるか（重傷のみ不可）。 */
    public static boolean ridable(InjuryStage stage) {
        return stage != InjuryStage.MAJOR;
    }

    // ---- 性格（気性、§26.7.7） ----

    /**
     * 特性の種類。獲得のトリガー（競馬専用ワールドでのレース敗北、§26.7.7）自体が
     * 競馬専用ワールド限定であるため、実質的にこの2種は競馬専用ワールドでのみ扱う。
     * 効果は{@link #fightingSpiritPowerBonus}・{@link #peakPerformerSpeedBonus}
     * に定める（§27.7）。
     */
    public enum SpecialTrait { FIGHTING_SPIRIT, PEAK_PERFORMER }

    /** 気性0のときの調教成功率（100%）。 */
    public static final double TRAINING_SUCCESS_RATE_BASE = 1.0;

    /** 気性1につき下がる調教成功率（気性100で成功率60%まで下がる）。 */
    public static final double TRAINING_SUCCESS_RATE_PENALTY_PER_TEMPERAMENT_VALUE = 0.004;

    /**
     * 気性の値（値0〜100、荒いほど大きい）から調教の成功率を求める（§26.7.2）。
     * 気性が荒いほど成功率が下がる。
     */
    public static double trainingSuccessRate(int temperamentValue) {
        requireStatValue(temperamentValue);
        return TRAINING_SUCCESS_RATE_BASE - temperamentValue * TRAINING_SUCCESS_RATE_PENALTY_PER_TEMPERAMENT_VALUE;
    }

    /** 気性100のとき、レース敗北時に特性を獲得できる確率の上限。 */
    public static final double TRAIT_ACQUISITION_CHANCE_MAX = 0.30;

    /** 気性1につき加算される、レース敗北時の特性獲得確率。 */
    public static final double TRAIT_ACQUISITION_CHANCE_PER_TEMPERAMENT_VALUE = TRAIT_ACQUISITION_CHANCE_MAX / STAT_MAX;

    /**
     * 競馬専用ワールドでレースに敗北した（1着以外だった）際、気性の値から特性
     * （{@link SpecialTrait}）を新たに獲得できる確率を求める（§26.7.7）。獲得の
     * トリガーは調教の失敗ではなくレースの敗北であり、気性が荒いほど獲得しやすい。
     */
    public static double traitAcquisitionChanceOnRaceDefeat(int temperamentValue) {
        requireStatValue(temperamentValue);
        return temperamentValue * TRAIT_ACQUISITION_CHANCE_PER_TEMPERAMENT_VALUE;
    }

    // ---- 特性による効果（§27.7、競馬専用ワールド限定） ----

    /** 闘争心が発動したときのパワーの上乗せ。 */
    public static final double FIGHTING_SPIRIT_POWER_BONUS = 10.0;

    /** 闘争心の判定で遡る、本馬自身の直近レース数。 */
    public static final int FIGHTING_SPIRIT_LOOKBACK_RACES = 5;

    /**
     * 闘争心が発動する条件を判定する。{@code recentRaces} は本馬自身の直近レース結果
     * （新しい順）で、各要素はそのレースで本馬より着順が上だった馬のIDの集合。
     * 直近{@value #FIGHTING_SPIRIT_LOOKBACK_RACES}レースのみを参照し、それより
     * 前のレースは無視する。今回のレースの出走馬（{@code fieldHorseIds}）に、
     * この参照範囲内で本馬に勝ったことのある馬が1頭でも含まれていれば発動する。
     */
    public static boolean fightingSpiritActive(
            List<Set<String>> recentRaces, Set<String> fieldHorseIds) {
        int lookback = Math.min(recentRaces.size(), FIGHTING_SPIRIT_LOOKBACK_RACES);
        for (int i = 0; i < lookback; i++) {
            for (String rivalId : recentRaces.get(i)) {
                if (fieldHorseIds.contains(rivalId)) {
                    return true;
                }
            }
        }
        return false;
    }

    /** 闘争心の発動条件を満たす場合のパワーの上乗せ、満たさない場合は0。 */
    public static double fightingSpiritPowerBonus(boolean hasFightingSpirit, boolean fightingSpiritActive) {
        return (hasFightingSpirit && fightingSpiritActive) ? FIGHTING_SPIRIT_POWER_BONUS : 0.0;
    }

    /** 本番得意が発動したときのスピードの上乗せ。 */
    public static final double PEAK_PERFORMER_SPEED_BONUS = 5.0;

    /**
     * 本番得意の発動条件を満たす（レースのクラスが重賞、§27.5）場合のスピードの
     * 上乗せ、満たさない場合は0。
     */
    public static double peakPerformerSpeedBonus(boolean hasPeakPerformer, boolean isGradedRace) {
        return (hasPeakPerformer && isGradedRace) ? PEAK_PERFORMER_SPEED_BONUS : 0.0;
    }

    // ---- ラストスパート（§27.4、競馬専用ワールド限定） ----

    /** ラストスパートの発動閾値（根性0のとき）。残りスタミナが最大スタミナの15%以下で発動する。 */
    public static final double SPURT_TRIGGER_THRESHOLD_BASE = 0.15;

    /** ラストスパートの発動閾値（根性100のとき）。最大で20%まで早く発動できる。 */
    public static final double SPURT_TRIGGER_THRESHOLD_MAX = 0.20;

    /**
     * 根性の値からラストスパートの発動閾値（残りスタミナの割合）を求める。根性が
     * 高いほど、より多くスタミナが残っている段階でスパートへ入れる（§27.4）。
     */
    public static double spurtTriggerThreshold(int gutsValue) {
        requireStatValue(gutsValue);
        double range = SPURT_TRIGGER_THRESHOLD_MAX - SPURT_TRIGGER_THRESHOLD_BASE;
        return SPURT_TRIGGER_THRESHOLD_BASE + (gutsValue / (double) STAT_MAX) * range;
    }

    /**
     * 残りスタミナの割合（0〜1）と根性の値から、ラストスパートに入っているかを判定する。
     * この状態は、レース距離がその馬のスタミナに対して長すぎる（ゴール前に尽きる）か
     * 短すぎる（スパートに入る間もなく終わる）かという、距離適性のシグナルにもなる
     * （§27.3・§27.4）。
     */
    public static boolean spurtActive(double remainingStaminaFraction, int gutsValue) {
        if (remainingStaminaFraction < 0.0 || remainingStaminaFraction > 1.0) {
            throw new IllegalArgumentException("残りスタミナの割合が範囲外である: " + remainingStaminaFraction);
        }
        return remainingStaminaFraction <= spurtTriggerThreshold(gutsValue);
    }

    /** ラストスパート中のスタミナ消費倍率。 */
    public static final double SPURT_STAMINA_CONSUMPTION_MULTIPLIER = 2.0;

    /** ラストスパート中に出せる最高速度の倍率。 */
    public static final double SPURT_MAX_SPEED_MULTIPLIER = 1.1;

    /** ラストスパート中かどうかから、スタミナ消費倍率を求める。 */
    public static double spurtStaminaConsumptionMultiplier(boolean spurting) {
        return spurting ? SPURT_STAMINA_CONSUMPTION_MULTIPLIER : 1.0;
    }

    /** ラストスパート中かどうかから、最高速度の倍率を求める。 */
    public static double spurtSpeedMultiplier(boolean spurting) {
        return spurting ? SPURT_MAX_SPEED_MULTIPLIER : 1.0;
    }

    // ---- 賢さ：スタミナ効率（§27.4、競馬専用ワールド限定） ----

    /** 賢さ0のときのスタミナ消費倍率（非効率）。 */
    public static final double WISDOM_STAMINA_EFFICIENCY_AT_ZERO = 1.1;

    /** 賢さ100のときのスタミナ消費倍率（最大効率）。 */
    public static final double WISDOM_STAMINA_EFFICIENCY_MAX = 0.9;

    /**
     * 賢さの値から、レース中のスタミナ消費全般に一律で掛かる倍率を求める（§27.4）。
     * 賢さ0で{@value #WISDOM_STAMINA_EFFICIENCY_AT_ZERO}倍、賢さ100で
     * {@value #WISDOM_STAMINA_EFFICIENCY_MAX}倍（最大効率）となるよう線形に補間する。
     * ラストスパート中の消費倍率（{@link #spurtStaminaConsumptionMultiplier}）にも
     * 重ねて掛かる。
     */
    public static double wisdomStaminaEfficiencyMultiplier(int wisdomValue) {
        requireStatValue(wisdomValue);
        double range = WISDOM_STAMINA_EFFICIENCY_MAX - WISDOM_STAMINA_EFFICIENCY_AT_ZERO;
        return WISDOM_STAMINA_EFFICIENCY_AT_ZERO + (wisdomValue / (double) STAT_MAX) * range;
    }

    // ---- シーズンと年齢（§27.9、競馬専用ワールド限定） ----

    /** 出生から1歳（調教可能）になるまでの実時間（時間）。 */
    public static final int AGE_ONE_HOURS = 24;

    /** 出生から2歳（レース出走可能）になるまでの実時間（時間）。 */
    public static final int AGE_TWO_HOURS = 48;

    /** 出生からの経過時間（実時間、時間単位）から、調教が可能か（1歳に達したか）を判定する。 */
    public static boolean trainable(int hoursSinceBirth) {
        requireHours(hoursSinceBirth);
        return hoursSinceBirth >= AGE_ONE_HOURS;
    }

    /** 1シーズンを構成するスプリット数（1スプリット＝1週間、§27.9）。 */
    public static final int SEASON_SPLITS = 4;

    /**
     * 3歳馬クラシック路線（大地三冠・花冠三冠、§27.8.2）の出走が解禁される
     * スプリットか（3・4のみ、§27.9）。
     */
    public static boolean classicSplit(int split) {
        if (split < 1 || split > SEASON_SPLITS) {
            throw new IllegalArgumentException("スプリットが範囲外である: " + split);
        }
        return split == 3 || split == 4;
    }

    /**
     * 3歳馬クラシック路線への出走条件（§27.9）。2歳（実時間{@value #AGE_TWO_HOURS}時間
     * 経過）に達し、かつ現在のスプリットが3・4のいずれかであることの両方を満たす必要がある。
     */
    public static boolean classicRaceEligible(int hoursSinceBirth, int currentSplit) {
        requireHours(hoursSinceBirth);
        return hoursSinceBirth >= AGE_TWO_HOURS && classicSplit(currentSplit);
    }

    /**
     * 古馬シリーズ・距離別シリーズ（§27.8.2）への出走条件（§27.9）。2歳（実時間
     * {@value #AGE_TWO_HOURS}時間経過）に達していればスプリットを問わない。
     */
    public static boolean openRaceEligible(int hoursSinceBirth) {
        requireHours(hoursSinceBirth);
        return hoursSinceBirth >= AGE_TWO_HOURS;
    }

    private static void requireHours(int hoursSinceBirth) {
        if (hoursSinceBirth < 0) {
            throw new IllegalArgumentException("出生からの経過時間が負である: " + hoursSinceBirth);
        }
    }
}
