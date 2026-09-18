package jp.mcserver.core;

/**
 * 共和制の議会（§9.1）。
 *
 * <p>議会 = 役職者全員（首長・リーダー・チーフ）。過半数で可決し、首長も1票を持つ。
 */
public final class Assembly {

    private Assembly() {}

    /** 承認を要する支出の閾値（%）。 */
    public static final int SPEND_THRESHOLD_PERCENT = 10;

    /** 議会の承認を要する事項（§9.1）。 */
    public enum Matter {
        /** 統一（§8.3）。 */
        UNIFICATION,
        /** 体制変更（§9）。 */
        GOVERNMENT_CHANGE,
        /** 首都変更（§4.10）。 */
        CAPITAL_CHANGE,
        /** 国庫残高の10%以上の支出（§7）。 */
        TREASURY_SPEND,
        /** 外交準備高の10%以上の援助金・救済（§7.3）。 */
        DIPLOMATIC_AID
    }

    /**
     * 承認が必要かを判定する。共和制以外では議会が存在しないため常に false。
     *
     * @param amount 支出額（金額を伴わない事項では無視される）
     */
    public static boolean requiresApproval(Government government, Matter matter, long amount,
                                           NationalAccounts.Balances balances) {
        if (government != Government.REPUBLIC) {
            return false;
        }
        return switch (matter) {
            case UNIFICATION, GOVERNMENT_CHANGE, CAPITAL_CHANGE -> true;
            case TREASURY_SPEND -> reachesThreshold(amount, balances.treasury());
            case DIPLOMATIC_AID -> reachesThreshold(amount, balances.reserve());
        };
    }

    private static boolean reachesThreshold(long amount, long balance) {
        if (amount < 0) {
            throw new IllegalArgumentException("金額が負である: " + amount);
        }
        return amount * 100 >= balance * SPEND_THRESHOLD_PERCENT;
    }

    /** 採決の結果。 */
    public record Vote(boolean passed, int yes, int no, int abstained, int required) {}

    /**
     * 採決（§9.1）。議会構成員の<b>過半数</b>で可決する。棄権は賛成に数えない。
     *
     * @param assemblySize 議会構成員の総数（{@link Roles#assemblySize}）
     */
    public static Vote tally(int assemblySize, int yes, int no) {
        if (assemblySize <= 0) {
            throw new IllegalArgumentException("議会が存在しない");
        }
        if (yes < 0 || no < 0 || yes + no > assemblySize) {
            throw new IllegalArgumentException("票数が不正である: yes=" + yes + " no=" + no);
        }
        int required = assemblySize / 2 + 1;
        return new Vote(yes >= required, yes, no, assemblySize - yes - no, required);
    }
}
