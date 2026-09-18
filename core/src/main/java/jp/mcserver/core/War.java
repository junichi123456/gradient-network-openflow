package jp.mcserver.core;

/**
 * 戦争イベント（§11）。君主制のみが発起できる。
 */
public final class War {

    private War() {}

    /** 発起後、再発起できない期間（日）。 */
    public static final int REINITIATE_COOLDOWN_DAYS = 120;

    /** 発起を受けない期間（日）。 */
    public static final int IMMUNITY_DAYS = 90;

    /** 日時を決定する期間（発起日を含む日数）。 */
    public static final int SCHEDULING_DAYS = 8;

    /** ボーナスチャンクの累積上限（§11.7）。 */
    public static final int MAX_BONUS_CHUNKS = 40;

    /** ボーナスチャンクを保持できる最低ランク（§11.7）。 */
    public static final int BONUS_MIN_RANK = 15;

    public enum Denial {
        NONE, NOT_MONARCHY, EMBARGO, RELATION_EXISTS, TARGET_IMMUNE,
        INITIATOR_COOLDOWN, SELF
    }

    public record Check(boolean allowed, Denial denial, String message) {}

    /**
     * 発起の可否（§11.1）。
     *
     * @param serverDay              サーバー稼働日（1 始まり）
     * @param relationExists         同盟・属国・宗主のいずれかの関係があるか（発議時点で固定）
     * @param targetImmuneDays       対象国が発起を受けない残日数
     * @param initiatorCooldownDays  自国の再発起までの残日数
     */
    public static Check canInitiate(Government government, int serverDay, boolean relationExists,
                                    int targetImmuneDays, int initiatorCooldownDays, boolean self) {
        if (self) {
            return new Check(false, Denial.SELF, "自国に戦争を発起できません");
        }
        if (government != Government.MONARCHY) {
            return new Check(false, Denial.NOT_MONARCHY, "戦争を発起できるのは君主制のみです");
        }
        if (!ServerTimeline.conflictAllowed(serverDay)) {
            return new Check(false, Denial.EMBARGO,
                    "戦争はサーバー開始から " + ServerTimeline.CONFLICT_EMBARGO_DAYS
                            + " 日間実施できません（解禁まであと "
                            + ServerTimeline.daysUntilConflictAllowed(serverDay) + " 日）");
        }
        if (relationExists) {
            return new Check(false, Denial.RELATION_EXISTS,
                    "なんらの関係性も構築していない国家にのみ発起できます");
        }
        if (targetImmuneDays > 0) {
            return new Check(false, Denial.TARGET_IMMUNE,
                    "対象国は発起を受けない期間中です（あと " + targetImmuneDays + " 日）");
        }
        if (initiatorCooldownDays > 0) {
            return new Check(false, Denial.INITIATOR_COOLDOWN,
                    "再発起まであと " + initiatorCooldownDays + " 日です");
        }
        return new Check(true, Denial.NONE, "発起可能です");
    }

    /**
     * 勝利によるボーナスチャンク（§11.7）。1〜3勝目は +8、4勝目以降は +4。累積上限 +40。
     */
    public static int bonusChunks(int wins) {
        if (wins < 0) {
            throw new IllegalArgumentException("勝利数が負である: " + wins);
        }
        int total = wins <= 3 ? 8 * wins : 24 + 4 * (wins - 3);
        return Math.min(MAX_BONUS_CHUNKS, total);
    }

    /**
     * 現に有効なボーナスチャンク（§11.7）。
     * rank14 以下に降格した時点ですべてのボーナスチャンクを失う。
     */
    public static int effectiveBonus(int wins, int rank) {
        return rank >= BONUS_MIN_RANK ? bonusChunks(wins) : 0;
    }

    /** 敗北によるランクの変化（§11.7）。 */
    public static int rankAfterDefeat(int rank) {
        return Math.max(0, rank - 1);
    }
}
