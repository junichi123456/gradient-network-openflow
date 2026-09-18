package jp.mcserver.core.rail;

import jp.mcserver.core.NationalAccounts;

/**
 * 月末維持費請求タスク（`rail_infra_spec.md` F-03）。
 *
 * <p>請求が終わったあと、各国家の「当月設置カウント」（{@link RailCost}・
 * {@link DiplomacyQuota} が参照する値）は 0 へリセットする。この処理自体は
 * 単なる代入であり、core 側に計算ロジックは無い（プラグイン側でカウンタを 0 にするだけ）。
 */
public final class MonthlyBilling {

    private MonthlyBilling() {
    }

    /** 属国が納める側の取り分（%）。残り（100−これ）を宗主国が負担する。 */
    public static final int VASSAL_SHARE_PERCENT = 60;

    /**
     * レール1個ぶんの月額維持費。<b>設置時の基本単価と同額</b>（設置手数料は含まない、
     * 一回限りの設置費用のため）。領土外倍率は設置時と同じ考え方で適用する。
     */
    public static long maintenanceCost(RailType type, boolean outsideOwnTerritory) {
        double multiplied = type.basePrice() * RailCost.territoryMultiplier(outsideOwnTerritory);
        return (long) Math.ceil(multiplied - 1e-9);
    }

    /**
     * 属国負担の按分。属国が設置したレールの月額維持費の合計を、宗主国と属国とで分ける。
     *
     * @param total 属国が設置した全レールの月額維持費の合計
     */
    public record Apportionment(long total, long toSuzerain, long toVassal) {
    }

    public static Apportionment apportion(long total) {
        if (total < 0) {
            throw new IllegalArgumentException("維持費が負である: " + total);
        }
        long toVassal = total * VASSAL_SHARE_PERCENT / 100;
        long toSuzerain = total - toVassal;
        return new Apportionment(total, toSuzerain, toVassal);
    }

    /**
     * 独立国（属国ではない）の維持費請求。全額を自国の国庫（{@code NationalAccounts#payDomestic}）
     * から引き落とす。属国負担の按分とは異なり、不足しても<b>不履行のまま記録するだけ</b>
     * （F-01の設置時とは違い、月末請求は既存インフラの維持費であり、「無かったことにする」
     * キャンセルという概念が無いため）。
     */
    public static NationalAccounts.Payment billIndependent(NationalAccounts.Balances balances,
            long totalMaintenance) {
        return NationalAccounts.payDomestic(balances, totalMaintenance);
    }

    /** 属国側の請求結果。宗主国・属国それぞれの国庫から独立して引き落とす。 */
    public record VassalBilling(NationalAccounts.Payment suzerainPayment,
                                NationalAccounts.Payment vassalPayment) {
    }

    public static VassalBilling billVassal(NationalAccounts.Balances suzerainBalances,
            NationalAccounts.Balances vassalBalances, long totalMaintenance) {
        Apportionment apportionment = apportion(totalMaintenance);
        var suzerainPayment = NationalAccounts.payDomestic(suzerainBalances, apportionment.toSuzerain());
        var vassalPayment = NationalAccounts.payDomestic(vassalBalances, apportionment.toVassal());
        return new VassalBilling(suzerainPayment, vassalPayment);
    }
}
