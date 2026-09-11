package jp.mcserver.core;

import java.util.ArrayList;
import java.util.List;

/**
 * 属国（§8.2）。
 */
public final class Vassalage {

    private Vassalage() {}

    /** 再従属のクールダウン（日）。 */
    public static final int RESUBJUGATION_COOLDOWN_DAYS = 33;

    /** 独立時に一括支払いする日数。 */
    public static final int INDEPENDENCE_DAYS = 30;

    /** 上納の世界政府への取り分（%）。 */
    public static final int WORLD_SHARE_PERCENT = 10;

    /**
     * 保有上限 = リーダー枠数（§8.2）。rank7〜19:1、rank20〜25:2、共和制は +1。
     * 君主制は保有不可。
     */
    public static int limit(int rank, Government government) {
        if (government == Government.MONARCHY) {
            return 0;
        }
        int leaderSlots = rank >= 20 ? 2 : rank >= 7 ? 1 : 0;
        if (leaderSlots == 0) {
            return 0;
        }
        return leaderSlots + government.roleBonus();
    }

    /** 上納額 = 100 × 属国ランク / 日。 */
    public static long tributePerDay(int vassalRank) {
        return 100L * vassalRank;
    }

    /** 独立時の一括支払い = 直前30日分の上納。 */
    public static long independenceCost(int vassalRank) {
        return tributePerDay(vassalRank) * INDEPENDENCE_DAYS;
    }

    /** 上納の分配。 */
    public record Tribute(long total, long toWorld, long toSuzerain, long unpaid) {}

    /**
     * 上納の徴収（§8.2）。属国の外交準備高から徴収し、10%を世界政府、90%を宗主国へ。
     * 一括徴収から天引きする。
     */
    public static Tribute collect(NationalAccounts.Balances vassal, int vassalRank) {
        long total = tributePerDay(vassalRank);
        var payment = NationalAccounts.payDiplomatic(vassal, total);
        long paid = total - payment.unpaid();
        long toWorld = paid * WORLD_SHARE_PERCENT / 100;
        return new Tribute(paid, toWorld, paid - toWorld, payment.unpaid());
    }

    public enum Denial {
        NONE, MONARCHY, LIMIT_REACHED, ALREADY_VASSAL, SUZERAIN_IS_VASSAL,
        VASSAL_HAS_VASSALS, COOLDOWN, NOT_APPROVED, SELF, CAMP
    }

    public record Check(boolean allowed, Denial denial, String message) {}

    /**
     * 従属の成立判定（§8.2）。多重従属は全面禁止であり、属国は属国を持てず、宗主国は1つのみ。
     */
    public static Check canSubjugate(int suzerainRank, Government suzerainGovernment,
                                     int currentVassals, boolean candidateIsVassal,
                                     boolean candidateHasVassals, boolean suzerainIsVassal,
                                     boolean candidateIsCamp, int cooldownRemainingDays,
                                     boolean approved, boolean self) {
        if (self) {
            return new Check(false, Denial.SELF, "自国を属国にはできません");
        }
        if (suzerainGovernment == Government.MONARCHY) {
            return new Check(false, Denial.MONARCHY, "君主制は属国を保有できません");
        }
        if (suzerainIsVassal) {
            return new Check(false, Denial.SUZERAIN_IS_VASSAL, "属国は属国を持てません");
        }
        if (candidateHasVassals) {
            return new Check(false, Denial.VASSAL_HAS_VASSALS, "属国を持つ国家は従属できません");
        }
        if (candidateIsVassal) {
            return new Check(false, Denial.ALREADY_VASSAL, "既に他国の属国です");
        }
        if (candidateIsCamp) {
            return new Check(false, Denial.CAMP, "野営地は従属できません");
        }
        int limit = limit(suzerainRank, suzerainGovernment);
        if (limit == 0) {
            return new Check(false, Denial.LIMIT_REACHED, "属国を保有できるのは rank7 以上です");
        }
        if (currentVassals >= limit) {
            return new Check(false, Denial.LIMIT_REACHED, "属国の保有上限は " + limit + " です");
        }
        if (cooldownRemainingDays > 0) {
            return new Check(false, Denial.COOLDOWN, "再従属まであと " + cooldownRemainingDays + " 日です");
        }
        if (!approved) {
            return new Check(false, Denial.NOT_APPROVED, "宗主国の承認が必要です");
        }
        return new Check(true, Denial.NONE, "従属可能です");
    }

    /**
     * 保有上限の超過による関係解消（§8.2）。<b>最後に加盟した属国から</b>解消する。
     *
     * @param vassalsInJoinOrder 加盟順に並んだ属国名
     * @return 解消する属国名（解消順）
     */
    public static List<String> resolveExcess(List<String> vassalsInJoinOrder, int limit) {
        List<String> released = new ArrayList<>();
        for (int i = vassalsInJoinOrder.size() - 1; i >= limit; i--) {
            released.add(vassalsInJoinOrder.get(i));
        }
        return released;
    }
}
