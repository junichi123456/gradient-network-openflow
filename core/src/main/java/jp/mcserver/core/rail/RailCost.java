package jp.mcserver.core.rail;

import jp.mcserver.core.NationalAccounts;

/**
 * レール設置制限および即時コスト徴収（`rail_infra_spec.md` F-01）。
 *
 * <p><b>設置手数料の「設置総数」は当月の累計と解釈している。</b>要件定義書のデータベース設計
 * （§4 `nation_monthly_data`）が持つカウンタは当月ぶんの1つだけであり、全期間の累計を持つ
 * 場所が無いためである。手数料は<b>国家全体の累計</b>（レール種別を問わない合算）に基づく
 * ——ユーザーへ確認済み（§6 の未確定点1点目）。
 */
public final class RailCost {

    private RailCost() {
    }

    /** 設置を許可する高度（Y座標）の下限。 */
    public static final int MIN_Y = -10;

    /** 設置を許可する高度（Y座標）の上限。 */
    public static final int MAX_Y = 10;

    /** 設置手数料が発生する刻み（本）。この本数ごとに手数料が段階的に上がる。 */
    public static final int FEE_STEP = 10;

    /** 1段あたりの手数料加算額。 */
    public static final long FEE_PER_STEP = 10;

    /** 自国領土外での設置に掛かる倍率。 */
    public static final double OUTSIDE_TERRITORY_MULTIPLIER = 1.5;

    /** 高度が設置許可の範囲内か。 */
    public static boolean altitudeAllowed(int y) {
        return y >= MIN_Y && y <= MAX_Y;
    }

    /**
     * 設置手数料。当月にこれまで設置した本数（今回の分は含まない、国家全体の累計）に応じて
     * 段階的に上がる——10本設置済みなら +10、20本設置済みなら +20、という具合。
     */
    public static long fee(int placedThisMonthSoFar) {
        if (placedThisMonthSoFar < 0) {
            throw new IllegalArgumentException("設置本数が負である: " + placedThisMonthSoFar);
        }
        return (placedThisMonthSoFar / FEE_STEP) * FEE_PER_STEP;
    }

    /** 領土内外による倍率。 */
    public static double territoryMultiplier(boolean outsideOwnTerritory) {
        return outsideOwnTerritory ? OUTSIDE_TERRITORY_MULTIPLIER : 1.0;
    }

    /**
     * 請求額 = (基本単価 + 手数料) × 領土内外倍率。端数は切り上げる
     * （{@code NationalAccounts} の他の徴収と同じ丸め方に揃えた）。
     */
    public static long totalCost(RailType type, int placedThisMonthSoFar, boolean outsideOwnTerritory) {
        long base = type.basePrice() + fee(placedThisMonthSoFar);
        double multiplied = base * territoryMultiplier(outsideOwnTerritory);
        return (long) Math.ceil(multiplied - 1e-9);
    }

    /** 設置を拒否する理由。 */
    public enum Denial {
        NONE, ALTITUDE, NOT_AFFILIATED, MONTHLY_LIMIT_REACHED, INSUFFICIENT_FUNDS
    }

    public record Check(boolean allowed, Denial denial, String message) {
    }

    /**
     * 設置の可否（残高確認より前の3条件、§「設置許可判定」）。
     *
     * @param monthlyLimit 自国の当月の有効上限枠（{@link DiplomacyQuota}）
     */
    public static Check canPlace(int y, boolean affiliated, int placedThisMonthSoFar, int monthlyLimit) {
        if (!altitudeAllowed(y)) {
            return new Check(false, Denial.ALTITUDE,
                    "高度がY" + MIN_Y + "〜" + MAX_Y + "の範囲外です（Y=" + y + "）");
        }
        if (!affiliated) {
            return new Check(false, Denial.NOT_AFFILIATED, "国家に所属していません");
        }
        if (placedThisMonthSoFar >= monthlyLimit) {
            return new Check(false, Denial.MONTHLY_LIMIT_REACHED,
                    "当月の設置上限（" + monthlyLimit + "本）に達しています");
        }
        return new Check(true, Denial.NONE, "設置可能です");
    }

    /**
     * 引き落としの結果。<b>残高不足なら全額キャンセルする</b>（部分徴収はしない）——
     * 要件定義書「残高不足ならキャンセル」のとおり。国庫（{@code NationalAccounts#payDomestic}
     * と同じ、国内専用の勘定）から引き落とす。
     */
    public record Charge(boolean paid, NationalAccounts.Balances after, long amount) {
    }

    public static Charge charge(NationalAccounts.Balances balances, RailType type,
            int placedThisMonthSoFar, boolean outsideOwnTerritory) {
        long amount = totalCost(type, placedThisMonthSoFar, outsideOwnTerritory);
        if (balances.treasury() < amount) {
            return new Charge(false, balances, amount);
        }
        return new Charge(true, balances.withTreasury(balances.treasury() - amount), amount);
    }
}
