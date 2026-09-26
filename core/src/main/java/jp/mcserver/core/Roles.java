package jp.mcserver.core;

/**
 * 役職定員（§6.1）と議会の規模（§9.1）。
 *
 * <p>共和制は首長以外の各役職が +1。君主制は首長以外の全役職を廃止する。
 */
public final class Roles {

    private Roles() {}

    /** 役職枠の任命義務の期間（日）。未任命なら貢献度上位者が自動就任する（§6.1）。 */
    public static final int APPOINTMENT_DEADLINE_DAYS = 14;

    /** 首長不在と判定するログインなしの時間（§6.2）。 */
    public static final int HEAD_ABSENCE_HOURS = 48;

    /** リーダー権限を首長が兼務する上限ランク（§6.2）。 */
    public static final int LEADER_UNLOCK_RANK = 7;

    /** 役職の定員。 */
    public static int slots(Role role, int rank, Government government) {
        if (rank < 0 || rank > Formulas.MAX_RANK) {
            throw new IllegalArgumentException("ランクが範囲外である: " + rank);
        }
        if (role == Role.CITIZEN) {
            throw new IllegalArgumentException("市民に定員はない");
        }
        if (role == Role.HEAD) {
            return 1;
        }
        if (government == Government.MONARCHY) {
            return 0; // 君主制は首長以外の全役職を廃止する
        }
        int base = switch (role) {
            case LEADER -> rank >= 20 ? 2 : rank >= LEADER_UNLOCK_RANK ? 1 : 0;
            case CHIEF -> rank >= 20 ? 3 : rank >= 15 ? 2 : 0;
            default -> 0;
        };
        return base == 0 ? 0 : base + government.roleBonus();
    }

    /** 議会構成員の数（§9.1）。役職者全員。 */
    public static int assemblySize(int rank, Government government) {
        return slots(Role.HEAD, rank, government)
                + slots(Role.LEADER, rank, government)
                + slots(Role.CHIEF, rank, government);
    }
}
