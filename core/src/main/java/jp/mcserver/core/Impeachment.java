package jp.mcserver.core;

import java.util.Comparator;
import java.util.List;
import java.util.Optional;

/**
 * 共和制の弾劾（§9.1）。
 *
 * <p>議会構成員が発議し、48時間の投票を経て、実効国民の 2/3 以上の賛成で罷免する。
 * rank7未満では議会が首長のみとなるため、実効国民の 1/3 以上の連名で発議できる。
 */
public final class Impeachment {

    private Impeachment() {}

    /** 投票期間（時間）。 */
    public static final int VOTING_HOURS = 48;

    /** 罷免された首長が首長に就任できない期間（日）。 */
    public static final int BAN_DAYS = 90;

    public enum Denial {
        NONE, NOT_REPUBLIC, NOT_ASSEMBLY_MEMBER, INSUFFICIENT_COSIGNERS, TARGET_IS_NOT_HEAD
    }

    public record Check(boolean allowed, Denial denial, String message) {}

    /** rank7未満で発議に必要な連名の数（実効国民の1/3、切り上げ）。 */
    public static int requiredCosigners(int effectiveCitizens) {
        return Math.max(1, (effectiveCitizens + 2) / 3);
    }

    /**
     * 発議の可否（§9.1）。
     *
     * @param proposerRole 発議者の役職
     * @param cosigners    連名の数（発議者を含む）。rank7以上では無視される
     */
    public static Check canPropose(Government government, int rank, Role proposerRole,
                                   int cosigners, int effectiveCitizens) {
        if (government != Government.REPUBLIC) {
            return new Check(false, Denial.NOT_REPUBLIC, "弾劾があるのは共和制のみです");
        }
        if (rank >= Roles.LEADER_UNLOCK_RANK) {
            if (!proposerRole.inAssembly()) {
                return new Check(false, Denial.NOT_ASSEMBLY_MEMBER, "発議できるのは議会構成員のみです");
            }
            return new Check(true, Denial.NONE, "発議可能です");
        }
        // rank7未満は議会が首長のみであるため、連名による発議を認める
        int required = requiredCosigners(effectiveCitizens);
        if (cosigners < required) {
            return new Check(false, Denial.INSUFFICIENT_COSIGNERS,
                    "rank7未満では実効国民の1/3以上の連名が必要です（" + cosigners + " / " + required + "）");
        }
        return new Check(true, Denial.NONE, "連名により発議可能です");
    }

    /** 罷免に必要な賛成数（実効国民の 2/3、切り上げ）。 */
    public static int requiredVotes(int effectiveCitizens) {
        if (effectiveCitizens <= 0) {
            throw new IllegalArgumentException("実効国民がいない");
        }
        return (effectiveCitizens * 2 + 2) / 3;
    }

    /** 投票の結果。 */
    public record Outcome(boolean removed, int yes, int required, String message) {}

    public static Outcome tally(int effectiveCitizens, int yes) {
        int required = requiredVotes(effectiveCitizens);
        if (yes >= required) {
            return new Outcome(true, yes, required,
                    "罷免が可決されました（" + yes + " / " + required + "）");
        }
        return new Outcome(false, yes, required,
                "否決されました（" + yes + " / " + required + "）。発議者は市民に降格します");
    }

    /**
     * 罷免後の後任（§9.1）。
     *
     * <p>リーダーのうち貢献度が最大の者が首長に昇格し、<b>残任期を引き継ぐ</b>。
     * リーダーが存在しない場合（rank7未満など）は、貢献度が最大の実効国民が就任する
     * （§6.2 の自動就任に準ずる）。
     */
    public record Member(String playerName, Role role, double contribution) {}

    public static Optional<Member> successor(List<Member> members) {
        Optional<Member> fromLeaders = members.stream()
                .filter(m -> m.role() == Role.LEADER)
                .max(Comparator.comparingDouble(Member::contribution)
                        .thenComparing(Member::playerName, Comparator.reverseOrder()));
        if (fromLeaders.isPresent()) {
            return fromLeaders;
        }
        return members.stream()
                .filter(m -> m.role() != Role.HEAD)
                .max(Comparator.comparingDouble(Member::contribution)
                        .thenComparing(Member::playerName, Comparator.reverseOrder()));
    }

    /** 残任期を引き継ぐため、任期の終了日は変わらない。 */
    public static int termEndDay(int originalTermStartDay) {
        return Election.nextElectionDay(originalTermStartDay);
    }
}
