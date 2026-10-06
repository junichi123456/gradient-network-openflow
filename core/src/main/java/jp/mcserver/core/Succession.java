package jp.mcserver.core;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Optional;

/**
 * 君主制の継承（§9.2）。
 *
 * <p>君主制は弾劾を持たず、リーダー職を欠くため代行者も存在しない。
 * したがって首長の不在は国家の完全な停止を意味する。
 */
public final class Succession {

    private Succession() {}

    /**
     * 不在と判定するログインなしの時間（§9.2）。
     *
     * <p>君主制の特則であり、全体規定の48時間（§6.2、{@link Roles#HEAD_ABSENCE_HOURS}）
     * より長い。君主制は代行者を持たないため、48時間で全権限が停止すると
     * 週末を挟む不在だけで国家が止まってしまう。
     */
    public static final int ABSENCE_HOURS = 120;

    /** 不在の警告を出す日数。 */
    public static final int WARNING_DAYS = 30;

    /** 継承を発動する不在日数。 */
    public static final int SUCCESSION_DAYS = 90;

    /** 継承の打診への応答期限（時間）。 */
    public static final int RESPONSE_HOURS = 48;

    /** 空位から自動就任までの日数。 */
    public static final int AUTO_APPOINT_DAYS = 7;

    /** 実効国民の要件（§4.3）。直近30日の有効活動時間。 */
    public static final double EFFECTIVE_CITIZEN_HOURS = 1.0;

    // ------------------------------------------------------------ 不在の判定

    public enum Absence {
        /** 在任中。 */
        PRESENT,
        /** 120時間ログインがない。 */
        NO_LOGIN,
        /** 実効国民の要件を満たさなくなった。 */
        NOT_EFFECTIVE_CITIZEN;

        public boolean absent() {
            return this != PRESENT;
        }
    }

    /**
     * 不在の判定（§9.2）。
     *
     * <p>120時間ログインがない場合に加え、<b>首長が実効国民でなくなった時点</b>でも不在とする。
     * 後者がなければ、毎日わずかにログインするだけで権限を握り続けられてしまう。
     */
    public static Absence absence(double hoursSinceLogin, double activity30dHours) {
        if (activity30dHours < EFFECTIVE_CITIZEN_HOURS) {
            return Absence.NOT_EFFECTIVE_CITIZEN;
        }
        if (hoursSinceLogin >= ABSENCE_HOURS) {
            return Absence.NO_LOGIN;
        }
        return Absence.PRESENT;
    }

    // ------------------------------------------------------------ 停止する権限

    /** 首長の不在中に停止するか否かで分類した処理（§9.2）。 */
    public enum Power {
        /** 国庫の支出。 */
        TREASURY_SPEND(true),
        /** 昇格の申請。 */
        PROMOTION(true),
        /** 同盟の締結・破棄。 */
        ALLIANCE(true),
        /** 戦争の発起・日時の応諾。 */
        WAR(true),
        /** 統一の発議・応諾。 */
        UNIFICATION(true),
        /** 国民の登用承認・除名の執行。 */
        MEMBERSHIP(true),
        /** 首都チャンクの変更。 */
        CAPITAL_CHANGE(true),
        /** 政治体制の変更。 */
        GOVERNMENT_CHANGE(true),
        /** 領土の保護。 */
        TERRITORY_PROTECTION(false),
        /** 有効活動時間の集計と国家累計への加算。 */
        ACTIVITY_ACCOUNTING(false),
        /** 同盟継続料・属国上納の自動引き落とし。 */
        AUTOMATIC_BILLING(false),
        /** 維持要件の判定と降格・失効の執行。 */
        MAINTENANCE_JUDGEMENT(false),
        /** シュルカーボックスの使用とGUI市場での個人取引。 */
        PLAYER_LOGISTICS(false),
        /** 新規チャンクの取得。 */
        CLAIM_EXPANSION(false),
        /** 求人区画の掲載。 */
        RECRUITMENT(false);

        private final boolean suspendedWhenAbsent;

        Power(boolean suspendedWhenAbsent) {
            this.suspendedWhenAbsent = suspendedWhenAbsent;
        }

        public boolean suspendedWhenAbsent() {
            return suspendedWhenAbsent;
        }
    }

    /**
     * その処理が現に実行できるか。
     *
     * <p>空位期間（継承の発動から就任まで）も不在と同じ停止状態に置かれる。
     */
    public static boolean available(Power power, boolean absentOrVacant) {
        return !absentOrVacant || !power.suspendedWhenAbsent();
    }

    // ------------------------------------------------------------ 継承者の指名

    /** 国民。 */
    public record Member(String playerName, double contribution, boolean effectiveCitizen,
                         boolean inNation) {}

    /**
     * 指名が有効か（§9.2）。
     *
     * <p>指名者が離脱・除名・実効国民でなくなった場合、指名は自動失効する。
     */
    public static boolean nominationValid(Member nominee) {
        return nominee != null && nominee.inNation() && nominee.effectiveCitizen();
    }

    // ------------------------------------------------------------ 発動

    public enum Trigger {
        /** 何も起きない。 */
        NONE,
        /** 不在30日の警告。 */
        WARNING,
        /** 継承の発動。 */
        SUCCESSION,
        /** 任意退位による即時の発動。 */
        ABDICATION
    }

    /**
     * 発動の判定（§9.2）。ゲーム内の死亡は継承事由にならない。
     *
     * @param absentDays 不在が継続している日数
     * @param abdicated  首長が任意に退位したか
     */
    public static Trigger trigger(int absentDays, boolean abdicated) {
        if (abdicated) {
            return Trigger.ABDICATION;
        }
        if (absentDays >= SUCCESSION_DAYS) {
            return Trigger.SUCCESSION;
        }
        if (absentDays >= WARNING_DAYS) {
            return Trigger.WARNING;
        }
        return Trigger.NONE;
    }

    // ------------------------------------------------------------ 継承順位

    /**
     * 継承順位（§9.2）。
     *
     * <ol>
     *   <li>指名された継承者</li>
     *   <li>貢献度が最大の実効国民</li>
     *   <li>実効国民が存在しない場合は継承しない（§4.5 の通常処理に従う）</li>
     * </ol>
     *
     * @param nominee 指名された継承者。指名がない、または失効している場合は null
     * @param members 国民（前首長を除く）
     * @return 打診する順序
     */
    public static List<Member> order(Member nominee, List<Member> members) {
        List<Member> ordered = new ArrayList<>();
        if (nominationValid(nominee)) {
            ordered.add(nominee);
        }
        members.stream()
                .filter(Member::inNation)
                .filter(Member::effectiveCitizen)
                .filter(m -> ordered.stream().noneMatch(o -> o.playerName().equals(m.playerName())))
                .sorted(Comparator.comparingDouble(Member::contribution).reversed()
                        .thenComparing(Member::playerName))
                .forEach(ordered::add);
        return ordered;
    }

    /** 継承の打診。 */
    public record Offer(String playerName, double issuedAtHours) {

        /** 応答期限内か。 */
        public boolean open(double nowHours) {
            return nowHours - issuedAtHours < RESPONSE_HOURS;
        }
    }

    public enum Response { ACCEPTED, DECLINED, TIMEOUT }

    /** 応答の解決。期限切れは辞退と同じく次順位へ送る。 */
    public static Response resolve(Offer offer, Boolean accepted, double nowHours) {
        if (!offer.open(nowHours)) {
            return Response.TIMEOUT;
        }
        if (accepted == null) {
            return Response.TIMEOUT;
        }
        return accepted ? Response.ACCEPTED : Response.DECLINED;
    }

    /**
     * 空位からの自動就任（§9.2）。
     *
     * <p>発動から7日を経過しても就任者が決まらない場合、貢献度が最大の実効国民が
     * 自動的に就任する。<b>辞退できない。</b>
     *
     * @param daysSinceTrigger 発動からの経過日数
     */
    public static Optional<Member> forcedSuccessor(int daysSinceTrigger, List<Member> members) {
        if (daysSinceTrigger < AUTO_APPOINT_DAYS) {
            return Optional.empty();
        }
        return members.stream()
                .filter(Member::inNation)
                .filter(Member::effectiveCitizen)
                .max(Comparator.comparingDouble(Member::contribution)
                        .thenComparing(Member::playerName, Comparator.reverseOrder()));
    }

    // ------------------------------------------------------------ 継承後

    /**
     * 継承後の状態（§9.2）。
     *
     * @param government               体制は君主制のまま継承される
     * @param governmentChangeCooldown 体制変更のクールダウンは国家に帰属し、継承でリセットされない
     * @param alliancesRetained        同盟は法人に帰属するため維持される
     */
    public record Outcome(String newHead, Government government, int governmentChangeCooldown,
                          boolean alliancesRetained, Role previousHeadRole) {}

    public static Outcome succeed(String newHead, int governmentChangeCooldownRemaining) {
        return new Outcome(newHead, Government.MONARCHY, governmentChangeCooldownRemaining,
                true, Role.CITIZEN);
    }

    /**
     * 国民の対抗手段（§9.2）。
     *
     * <p>君主制において国民が首長を交代させる制度上の手段は存在しない。唯一の対抗は離脱であり、
     * 実効国民が M(a) を下回れば14日で降格が執行される。
     *
     * @return 離脱によって降格を招くのに必要な最小の離脱者数
     */
    public static int departuresToForceDemotion(int rank, int effectiveCitizens) {
        int required = Formulas.maintenanceCapacity(rank);
        return Math.max(0, effectiveCitizens - required + 1);
    }
}
