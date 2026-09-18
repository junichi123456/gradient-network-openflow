package jp.mcserver.core.worldcouncil;

import jp.mcserver.core.NationalAccounts;

import java.util.Map;

/**
 * 「世界協議」の還付金（固定額表・非課税）。
 *
 * <p>世界政府が徴収したキャッシュ（属国上納の世界政府取り分・援助金の償却・
 * 外交準備高の月次減価など、いずれも{@link NationalAccounts}が既に扱っている歳入）を、
 * 上位国家の国庫へ還付するという名目の制度。実際の徴収総額とは連動させず、
 * 固定額表で運用する（調整のしやすさを優先した判断）。
 *
 * <p>「非課税」とは、外交準備高を経由せず、援助金のような償却（§7.3 の3%）も発生させずに、
 * 全額を国庫へ計上することを指す。{@link NationalAccounts#donate} と同じ経路（国庫のみ）を
 * 再利用する。
 *
 * <p><b>金額は暫定値。</b>現実にどのような利益をもたらすかは構想段階であり、
 * 運用しながら調整することを前提とする。
 */
public final class WorldCouncilPayout {

    private WorldCouncilPayout() {}

    /** 順位（1〜4）ごとの還付額（exp）。5位以下・順位なしは0。 */
    public static final Map<Integer, Long> AMOUNTS = Map.of(
            1, 40_000L,
            2, 24_000L,
            3, 12_000L,
            4, 4_000L
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
