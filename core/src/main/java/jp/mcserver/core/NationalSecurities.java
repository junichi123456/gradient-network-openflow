package jp.mcserver.core;

/**
 * 国債と国家株（§7.5）。
 *
 * <p>国家は国庫の資金を国債・国家株で調達できる。購入代金（元本／国家株の代金）は
 * いずれも国庫で受け取る。国債は満期に国庫から元本を返すが、国家株の代金に返済はない。
 * 元本の受払いは実在の exp を動かすだけなので、通貨量は変わらない。<b>利子・配当だけが
 * 外交準備高から支払われ、新たな通貨を生む</b>。
 *
 * <p><b>保有制限</b>: 自国民は自国の証券を保有できない（{@link #canHold}）。自国保有を
 * 許すと、国民が自国株を買い（exp→国庫）、国家が準備高から配当を払い（準備高→同じ国民の
 * exp）、その exp で株を買い増すか国庫へ納入するという循環が成立し、援助金（3%償却・33日の
 * 相互禁止）を経ずに準備高が実質的に国庫へ移ってしまう（§7.1の原則を無手数料で迂回する）。
 * 保有を外国人に限ることで、この循環は成立しなくなる。
 *
 * <p><b>利子・配当は外交準備高からのみ支払い、国庫からは補填しない</b>。国庫は元本の
 * 返済原資であり、利払いに充てれば返済能力を自ら削ることになるためである。準備高が
 * 不足する場合は支払えるだけを支払い、残りは切り捨てる（{@link #payToActivePlayer}）。
 *
 * <p><b>活動日ゲート</b>: その日に有効活動時間（{@link ActivityWindow}）があった
 * プレイヤーにのみ発生する。活動しなかった日の分は消滅し、発行国にも戻らない
 * （準備高からも減らない）。日次上限のある労働所得に対し、資本所得（利子・配当）が
 * 無制限に上回らないための歯止めである。
 *
 * <p><b>通貨発行の上限</b>: 各国が利子・配当として支払える額は、同じ期間の外交準備高
 * 計上額（{@link NationalAccounts#accrual}）を超えられない（{@link #withinIssuanceCap}）。
 *
 * <p><b>デフォルト</b>: 満期に国庫が元本を返済できるかは {@link #canRepayAtMaturity} で
 * 判定できる。返済できない場合の新規発行禁止期間は、仕様が「一定期間」とのみ定め具体的な
 * 日数を明示していないため、ここでは判定のみを提供し日数は運用で定める。
 */
public final class NationalSecurities {

    private NationalSecurities() {}

    /** 証券の種別。 */
    public enum Kind { BOND, STOCK }

    /** 発行された1件の証券。{@code rate} は国債なら利率、国家株なら配当性向。 */
    public record Security(Kind kind, String issuerNation, long principal, double rate) {}

    /** 保有制限：自国民は自国の証券を保有できない（§7.5）。 */
    public static boolean canHold(String issuerNation, String holderNation) {
        return !issuerNation.equals(holderNation);
    }

    /** 発行：購入代金を国庫で受け取る（国債の元本・国家株の代金で共通）。通貨量は変わらない。 */
    public static NationalAccounts.Balances issue(NationalAccounts.Balances issuer, long amount) {
        return NationalAccounts.donate(issuer, amount);
    }

    /** 国債の満期償還：国庫から元本を返す（国庫のみ。不足分は不履行＝デフォルト）。 */
    public static NationalAccounts.Payment redeemBond(NationalAccounts.Balances issuer, long principal) {
        return NationalAccounts.payDomestic(issuer, principal);
    }

    /** 満期時点で国庫が元本を返済できるか。 */
    public static boolean canRepayAtMaturity(NationalAccounts.Balances issuer, long principal) {
        return issuer.treasury() >= principal;
    }

    /** 利子・配当1件の支払い結果。 */
    public record Payout(NationalAccounts.Balances issuerAfter, long paid) {}

    /**
     * 利子・配当の支払い。外交準備高からのみ支払い、国庫は補填しない。
     *
     * @param owed        本来支払うべき額（元本×利率、または元本×配当性向に応じた額）
     * @param activeToday その日に有効活動時間があったか。{@code false} なら支払わず消滅する
     *                    （発行国の準備高も減らない）
     */
    public static Payout payToActivePlayer(NationalAccounts.Balances issuer, long owed, boolean activeToday) {
        if (owed < 0) {
            throw new IllegalArgumentException("支払額が負である: " + owed);
        }
        if (!activeToday) {
            return new Payout(issuer, 0);
        }
        long paid = Math.min(issuer.reserve(), owed);
        return new Payout(issuer.withReserve(issuer.reserve() - paid), paid);
    }

    /** 通貨発行の上限：同じ期間の利子・配当の合計支払額は、同期間の準備高計上額を超えられない。 */
    public static boolean withinIssuanceCap(long totalPayoutsThisPeriod, long periodAccrual) {
        return totalPayoutsThisPeriod <= periodAccrual;
    }
}
