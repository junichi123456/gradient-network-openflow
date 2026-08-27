package jp.mcserver.core;

/**
 * 同盟（§8.1）。
 */
public final class Alliance {

    private Alliance() {}

    /** 解禁ランク。双方が満たす必要がある。 */
    public static final int MIN_RANK = 5;

    /** 再締結のクールダウン（日）。 */
    public static final int RECONTRACT_COOLDOWN_DAYS = 30;

    /** 継続料の徴収周期（日）。 */
    public static final int BILLING_PERIOD_DAYS = 30;

    /** 継続料 = 2,000 × 自国ランク / 30日。 */
    public static long upkeep(int rank) {
        return 2_000L * rank;
    }

    /** 初期費用 = 継続料1回分。 */
    public static long initialCost(int rank) {
        return upkeep(rank);
    }

    public enum Denial {
        NONE, RANK_TOO_LOW, PARTNER_RANK_TOO_LOW, LIMIT_REACHED,
        COOLDOWN, ALREADY_ALLIED, IS_VASSAL, SELF
    }

    public record Check(boolean allowed, Denial denial, String message) {}

    /**
     * 締結の可否。
     *
     * @param isVassal 自国が属国であるか（属国は同盟を発議できない、§8.2）
     */
    public static Check canForm(int myRank, int partnerRank, Government government,
                                int currentAlliances, int cooldownRemainingDays,
                                boolean alreadyAllied, boolean isVassal, boolean self) {
        if (self) {
            return new Check(false, Denial.SELF, "自国とは同盟できません");
        }
        if (isVassal) {
            return new Check(false, Denial.IS_VASSAL, "属国は同盟を発議できません");
        }
        if (alreadyAllied) {
            return new Check(false, Denial.ALREADY_ALLIED, "既に同盟関係にあります");
        }
        if (myRank < MIN_RANK) {
            return new Check(false, Denial.RANK_TOO_LOW,
                    "同盟の解禁は rank" + MIN_RANK + " 以上です（自国 rank" + myRank + "）");
        }
        if (partnerRank < MIN_RANK) {
            return new Check(false, Denial.PARTNER_RANK_TOO_LOW,
                    "相手国が rank" + MIN_RANK + " に達していません");
        }
        if (currentAlliances >= government.allianceLimit()) {
            return new Check(false, Denial.LIMIT_REACHED,
                    government.label() + "の同盟保有数は " + government.allianceLimit() + " です");
        }
        if (cooldownRemainingDays > 0) {
            return new Check(false, Denial.COOLDOWN,
                    "再締結まであと " + cooldownRemainingDays + " 日です");
        }
        return new Check(true, Denial.NONE, "締結可能です");
    }

    /** 継続料の徴収結果。 */
    public record Billing(NationalAccounts.Balances after, long paid, boolean dissolved) {}

    /**
     * 継続料の徴収（§8.1）。外交準備高から支払い、不足すれば国庫で補填する（§7.1）。
     * それでも払えなければ<b>即解消</b>となり、払った側への返還はない。
     */
    public static Billing bill(NationalAccounts.Balances balances, int rank) {
        var payment = NationalAccounts.payDiplomatic(balances, upkeep(rank));
        return new Billing(payment.after(), upkeep(rank) - payment.unpaid(), !payment.fulfilled());
    }
}
