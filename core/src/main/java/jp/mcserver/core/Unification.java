package jp.mcserver.core;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;

/**
 * 統一（§8.3）。
 */
public final class Unification {

    private Unification() {}

    /** 発議側に必要なランク。存続法人も実行時にこれを満たす必要がある。 */
    public static final int MIN_RANK = 20;

    /** 発議から実行までの時間（時間）。 */
    public static final int EXECUTION_DELAY_HOURS = 48;

    /** 再発議のクールダウン（日）。 */
    public static final int COOLDOWN_DAYS = 90;

    /** 統一処理後、定員判定を停止する時間（時間）。 */
    public static final int CAPACITY_GRACE_HOURS = 24;

    /** 統一ポイントの上限（§8.3）。 */
    public static final int MAX_POINTS = 3;

    /** 除名された国民が被統一側国家に加入できない期間（日）。 */
    public static final int REJOIN_BAN_DAYS = 90;

    /** 統一の当事国。 */
    public record Party(String nationName, int rank, double activity30d, long cumulativeHours,
                        boolean isVassal) {}

    public enum Denial {
        NONE, PROPOSER_RANK_TOO_LOW, IS_VASSAL, COOLDOWN, SELF, REFUSED
    }

    public record Check(boolean allowed, Denial denial, String message) {}

    /** 発議の可否（§8.3）。被統一側のランクに制限はない。 */
    public static Check canPropose(Party proposer, Party target, int cooldownRemainingDays) {
        if (proposer.nationName().equals(target.nationName())) {
            return new Check(false, Denial.SELF, "自国を統一できません");
        }
        if (proposer.isVassal()) {
            return new Check(false, Denial.IS_VASSAL, "属国は統一を発議できません");
        }
        if (proposer.rank() < MIN_RANK) {
            return new Check(false, Denial.PROPOSER_RANK_TOO_LOW,
                    "統一の発議は rank" + MIN_RANK + " 以上です（rank" + proposer.rank() + "）");
        }
        if (cooldownRemainingDays > 0) {
            return new Check(false, Denial.COOLDOWN, "再発議まであと " + cooldownRemainingDays + " 日です");
        }
        return new Check(true, Denial.NONE, "発議可能です");
    }

    /**
     * 存続法人の決定（§8.3）。
     *
     * <p>直近30日の活動がより活発な側。ただし存続法人は実行時点で rank20 以上でなければならない。
     * 満たさない場合は他方。両者が満たさない場合は不成立。
     *
     * @return 存続する側。不成立の場合は null
     */
    public static Party resolveSurvivor(Party a, Party b) {
        boolean aEligible = a.rank() >= MIN_RANK;
        boolean bEligible = b.rank() >= MIN_RANK;
        if (!aEligible && !bEligible) {
            return null;
        }
        if (aEligible && !bEligible) {
            return a;
        }
        if (!aEligible) {
            return b;
        }
        if (a.activity30d() > b.activity30d()) {
            return a;
        }
        if (b.activity30d() > a.activity30d()) {
            return b;
        }
        // 完全に同値なら国家名で決定的に選ぶ
        return a.nationName().compareTo(b.nationName()) <= 0 ? a : b;
    }

    /** 累計活動時間は単純合算し、C(25) を超えた分も保持する（§8.3）。 */
    public static long mergeCumulative(Party a, Party b) {
        return a.cumulativeHours() + b.cumulativeHours();
    }

    /** 統一後の国民。 */
    public record Member(String playerName, Role role, double activity30d) {}

    /**
     * 定員超過の処理（§8.3）。
     *
     * <p>直近30日の活動が少ない順に除名する。リーダー・チーフは除名対象から除外するが、
     * 対象が不足する場合はチーフ → リーダーの順に役職を剥奪して対象に加える。
     * 首長は最後まで除外し、除名しない。
     *
     * @return 除名する国民（除名順）
     */
    public static List<Member> selectExpulsions(List<Member> members, int capacity) {
        int excess = members.size() - capacity;
        if (excess <= 0) {
            return List.of();
        }
        List<Member> expelled = new ArrayList<>();
        for (Role role : List.of(Role.CITIZEN, Role.CHIEF, Role.LEADER)) {
            if (expelled.size() >= excess) {
                break;
            }
            List<Member> pool = new ArrayList<>();
            for (Member m : members) {
                if (m.role() == role) {
                    pool.add(m);
                }
            }
            pool.sort(Comparator.comparingDouble(Member::activity30d)
                    .thenComparing(Member::playerName));
            for (Member m : pool) {
                if (expelled.size() >= excess) {
                    break;
                }
                expelled.add(m);
            }
        }
        return expelled;
    }

    /** 消滅法人の首長の移行先（§8.3）。君主制の場合は市民。 */
    public static Role headTransition(Government survivorGovernment) {
        return survivorGovernment == Government.MONARCHY ? Role.CITIZEN : Role.CHIEF;
    }

    /** 統一ポイントの選択肢（§8.3）。 */
    public enum Point { MOVEMENT, WORK_SPEED, JUMP, WATER_BREATHING }

    /**
     * 統一ポイントが有効か（§8.3）。
     * 自国の保護チャンク内にいる間、かつ国家ランクが20以上である間のみ有効。
     */
    public static boolean pointsActive(int rank, boolean insideOwnTerritory) {
        return rank >= MIN_RANK && insideOwnTerritory;
    }
}
