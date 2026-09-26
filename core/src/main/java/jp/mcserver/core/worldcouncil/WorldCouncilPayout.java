package jp.mcserver.core.worldcouncil;

import jp.mcserver.core.NationalAccounts;

import java.util.Map;

/**
 * 「世界協議」の還付金（固定額表・非課税）。
 *
 * <p>世界政府の残高（§17）が実際に用いられる唯一の用途。世界協議は<b>ミニゲームの範疇</b>
 * であり経済の主軸ではないため、額はこの位置づけに見合う水準に抑える。実際の徴収総額とは
 * 連動させず、固定額表で運用する（調整のしやすさを優先した判断）。財源はサーバー管理者の
 * 裁量により無制限で、残高不足は発生しない。
 *
 * <p>「非課税」とは、外交準備高を経由せず、援助金のような償却（§7.3 の3%）も発生させずに、
 * 全額を国庫へ計上することを指す。{@link NationalAccounts#donate} と同じ経路（国庫のみ）を
 * 再利用する。
 *
 * <p><b>金額は暫定値。</b>現実にどのような利益をもたらすかは構想段階であり、
 * 運用しながら調整することを前提とする。配分比は 10 : 6 : 3 : 1、総額1,000,000。
 */
public final class WorldCouncilPayout {

    private WorldCouncilPayout() {}

    /** 順位（1〜4）ごとの還付額（exp）。5位以下・順位なしは0。 */
    public static final Map<Integer, Long> AMOUNTS = Map.of(
            1, 500_000L,
            2, 300_000L,
            3, 150_000L,
            4, 50_000L
    );

    /** 同順位（共同優勝など）は、その順位の額をそれぞれが満額受け取る。 */
    public static long amountFor(int rank) {
        return AMOUNTS.getOrDefault(rank, 0L);
    }

    /** 国庫への非課税計上（外交準備高を経由しない）。 */
    public static NationalAccounts.Balances credit(NationalAccounts.Balances b, int rank) {
        long amount = amountFor(rank);
        return amount == 0 ? b : NationalAccounts.donate(b, amount);
    }
}
