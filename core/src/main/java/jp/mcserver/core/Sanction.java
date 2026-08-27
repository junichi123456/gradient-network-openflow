package jp.mcserver.core;

import java.util.ArrayList;
import java.util.List;

/**
 * 制裁イベント（§10）。共和制のみが発起できる。
 */
public final class Sanction {

    private Sanction() {}

    /** 支払いの回数（24時間ごと・7日間）。 */
    public static final int INSTALLMENTS = 7;

    /** 賛成国への分配率（%）。 */
    public static final int SUPPORTER_SHARE_PERCENT = 15;

    /** 同一国家への再制裁クールダウン（日）。 */
    public static final int COOLDOWN_DAYS = 90;

    /** 不払い時のランク減少の換算（exp / 有効活動時間1h）。 */
    public static final long UNPAID_EXP_PER_HOUR = 10_000;

    /** 首長の外交能力の停止期間（日）。 */
    public static final int DIPLOMACY_SUSPENSION_DAYS = 14;

    /** 承認に必要な賛成数 = 体制を決定している他国の首長の 1/3（切り上げ）。 */
    public static int requiredApprovals(int governedOtherNations) {
        if (governedOtherNations <= 0) {
            return 0;
        }
        return (governedOtherNations + 2) / 3;
    }

    /** 1回あたりの制裁額 = 賛成国の合計ランク × 1,000。 */
    public static long installment(int sumOfSupporterRanks) {
        return 1_000L * sumOfSupporterRanks;
    }

    /** 7日間の総額。 */
    public static long total(int sumOfSupporterRanks) {
        return installment(sumOfSupporterRanks) * INSTALLMENTS;
    }

    /**
     * 徴収額の分配（§10）。15%を賛成国で等分し、85%と等分の端数は世界政府へ。
     */
    public record Distribution(long toWorld, long perSupporter, int supporters) {}

    public static Distribution distribute(long collected, int supporters) {
        if (supporters <= 0) {
            return new Distribution(collected, 0, 0);
        }
        long toSupporters = collected * SUPPORTER_SHARE_PERCENT / 100;
        long perSupporter = toSupporters / supporters;
        long distributed = perSupporter * supporters;
        return new Distribution(collected - distributed, perSupporter, supporters);
    }

    /** 否決時、賛成した各国が対象国へ支払う額 = 対象国のランク × 1,000（§10）。 */
    public static long rejectionPenaltyPerSupporter(int targetRank) {
        return 1_000L * targetRank;
    }

    /** 不払いによる有効活動時間の減算（§10）。10,000 exp あたり 1h。 */
    public static double unpaidActivityPenaltyHours(long unpaid) {
        if (unpaid < 0) {
            throw new IllegalArgumentException("不払い額が負である: " + unpaid);
        }
        return (double) unpaid / UNPAID_EXP_PER_HOUR;
    }

    public enum Denial {
        NONE, NOT_REPUBLIC, RANK_TOO_LOW, UNDER_SANCTION, COOLDOWN,
        TARGET_IS_ALLY, TARGET_IS_VASSAL, INSUFFICIENT_APPROVALS, SELF, EMBARGO
    }

    public record Check(boolean allowed, Denial denial, String message) {}

    /**
     * 発起の可否（§9・§10）。
     *
     * <p>rank14 以下に降格した場合、体制は保持されるが発起権のみ停止する（§9）。
     * また、サーバー開始から60日間は実施できない（§10）。
     *
     * @param serverDay サーバー稼働日（1 始まり）
     */
    public static Check canInitiate(Government government, int rank, int serverDay,
                                    boolean underSanction,
                                    int cooldownRemainingDays, boolean targetIsAllyOrVassal,
                                    int approvals, int governedOtherNations, boolean self) {
        if (self) {
            return new Check(false, Denial.SELF, "自国を制裁できません");
        }
        if (government != Government.REPUBLIC) {
            return new Check(false, Denial.NOT_REPUBLIC, "制裁を発起できるのは共和制のみです");
        }
        if (!ServerTimeline.conflictAllowed(serverDay)) {
            return new Check(false, Denial.EMBARGO,
                    "制裁はサーバー開始から " + ServerTimeline.CONFLICT_EMBARGO_DAYS
                            + " 日間実施できません（解禁まであと "
                            + ServerTimeline.daysUntilConflictAllowed(serverDay) + " 日）");
        }
        if (rank < 15) {
            return new Check(false, Denial.RANK_TOO_LOW,
                    "rank14 以下では制裁の発起権が停止します（rank" + rank + "）");
        }
        if (underSanction) {
            return new Check(false, Denial.UNDER_SANCTION, "制裁中の国家は他国への制裁を発起できません");
        }
        if (cooldownRemainingDays > 0) {
            return new Check(false, Denial.COOLDOWN,
                    "同一国家への再制裁まであと " + cooldownRemainingDays + " 日です");
        }
        if (targetIsAllyOrVassal) {
            return new Check(false, Denial.TARGET_IS_ALLY, "同盟国・属国は制裁の対象にできません");
        }
        int required = requiredApprovals(governedOtherNations);
        if (approvals < required) {
            return new Check(false, Denial.INSUFFICIENT_APPROVALS,
                    "承認が不足しています（" + approvals + " / " + required + "）");
        }
        return new Check(true, Denial.NONE, "発起可能です");
    }

    /** 徴収の1回分。 */
    public record Collection(NationalAccounts.Balances after, long collected, long unpaid,
                             Distribution distribution) {}

    /** 24時間ごとの徴収（§10）。対象国の外交準備高から徴収し、不足分は国庫で補填する。 */
    public static Collection collect(NationalAccounts.Balances target, int sumOfSupporterRanks,
                                    int supporters) {
        long due = installment(sumOfSupporterRanks);
        var payment = NationalAccounts.payDiplomatic(target, due);
        long collected = due - payment.unpaid();
        return new Collection(payment.after(), collected, payment.unpaid(),
                distribute(collected, supporters));
    }

    /** 発起から解除までに実際に徴収された合計を積み上げる補助。 */
    public static long sum(List<Collection> collections) {
        long total = 0;
        for (Collection c : collections) {
            total += c.collected();
        }
        return total;
    }

    /** 中途解除（§10）。既払い分は返還しない。 */
    public static List<Collection> truncate(List<Collection> collections, int executedInstallments) {
        return new ArrayList<>(collections.subList(0, Math.min(executedInstallments, collections.size())));
    }
}
