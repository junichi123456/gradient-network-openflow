package jp.mcserver.core;

import java.util.ArrayList;
import java.util.List;

/**
 * 国債と国家株（§7.5）。
 *
 * <p>国家は国庫の資金を国債・国家株で調達できる。購入代金（元本／国家株の代金）は
 * いずれも国庫で受け取る。国債は満期（{@link #MATURITY_OPTIONS_DAYS} から首長が選ぶ）に
 * 国庫から元本を返すが、国家株の代金に返済はない。元本の受払いは実在の exp を動かすだけ
 * なので、通貨量は変わらない。<b>利子・配当だけが外交準備高から支払われ、新たな通貨を
 * 生む</b>。
 *
 * <p><b>保有制限</b>: 自国民は自国の証券を保有できない（{@link #canHold}）。自国保有を
 * 許すと、国民が自国株を買い（exp→国庫）、国家が準備高から配当を払い（準備高→同じ国民の
 * exp）、その exp で株を買い増すか国庫へ納入するという循環が成立し、援助金（3%償却・33日の
 * 相互禁止）を経ずに準備高が実質的に国庫へ移ってしまう（§7.1の原則を無手数料で迂回する）。
 * 移籍・統一で保有者が自国民になった場合は{@link #FORCED_SALE_DAYS}日以内の強制売却が
 * 発生し、売れ残りは発行国が額面で買い戻す（{@link #buyback}）。売却完了までは利子・配当を
 * 支払わない（自国民である間に受け取れば保有制限を迂回できるため。判定は {@link #canHold}
 * を都度呼び直せばよい）。
 *
 * <p><b>利子・配当は外交準備高からのみ支払い、国庫からは補填しない</b>。国庫は元本の
 * 返済原資であり、利払いに充てれば返済能力を自ら削ることになるためである。準備高が
 * 不足する場合は支払えるだけを支払い、残りは切り捨てる（{@link #settle}）。
 *
 * <p><b>日割り発生・月次支払い</b>: 利子・配当は毎日、その日に有効活動時間
 * （{@link ActivityWindow}）があった保有者の分だけ積み上がる（{@link #dailyAccrual}）。
 * 活動しなかった日の分は発生時点で消滅し、発行国にも戻らない。積み上げた1か月分は
 * 毎月1日 05:00 にまとめて支払う（{@link #settle}）。同時刻に行う外交準備高の月次減価
 * （{@link NationalAccounts#decayMonthly}）より<b>先に</b>処理する——減価は「使われない
 * 準備高に使う理由を与える」仕組みであり、利払い・配当こそがその使途そのものだからである。
 *
 * <p><b>発行残高の上限</b>: 国債・国家株の合計（発行価格ベース）は、直近30日の外交準備高
 * 計上額の{@value #ISSUANCE_CAP_MULTIPLIER}倍を超えられない（{@link #issuanceCap}・
 * {@link #canIssue}）。国家株には償還が無く枠を占有し続けるため、枠を空ける手段は
 * {@link #buyback}（国庫から市場価格で買い戻し、消却した分だけ発行残高が戻る）のみである。
 *
 * <p><b>デフォルト</b>: 次のいずれかで発生する。
 * <ul>
 *   <li>満期に国庫が元本を返済できない（{@link #treasuryCanPay}）</li>
 *   <li>外交準備高が国債の利子を支払えない（{@link #bondInterestDefaulted}。配当は裁量の
 *       ため対象外）</li>
 *   <li>強制売却の売れ残りを額面で買い戻せない（{@link #treasuryCanPay}）</li>
 * </ul>
 * デフォルトした国家は{@value #DEFAULT_BAN_DAYS}日間、新規発行が禁止される
 * （{@link #issuanceBanned}。統一・制裁の再発議クールダウンと同じ値）。
 */
public final class NationalSecurities {

    private NationalSecurities() {}

    /** 証券の種別。 */
    public enum Kind { BOND, STOCK }

    /** 国債の満期として選べる日数。 */
    public static final List<Integer> MATURITY_OPTIONS_DAYS = List.of(90, 180, 360);

    /** 移籍・統一で保有者が自国民化した場合の強制売却期限（日）。 */
    public static final int FORCED_SALE_DAYS = 30;

    /** デフォルト後、新規発行が禁止される期間（日）。統一・制裁の再発議クールダウンと同じ値。 */
    public static final int DEFAULT_BAN_DAYS = 90;

    /** 発行残高の上限＝直近30日の外交準備高計上額の何倍か（国債・国家株の合計、発行価格ベース）。 */
    public static final int ISSUANCE_CAP_MULTIPLIER = 9;

    /** 発行された1件の証券。{@code rate} は国債なら利率、国家株なら配当性向。 */
    public record Security(Kind kind, String issuerNation, long principal, double rate) {}

    /** 満期として有効な日数か（{@link #MATURITY_OPTIONS_DAYS} のいずれか）。 */
    public static boolean validMaturity(int days) {
        return MATURITY_OPTIONS_DAYS.contains(days);
    }

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

    /**
     * 国庫がその額を支払えるか。満期償還・強制売却の額面買い戻しの、いずれのデフォルト判定にも使う。
     */
    public static boolean treasuryCanPay(NationalAccounts.Balances issuer, long amount) {
        return issuer.treasury() >= amount;
    }

    /**
     * 国家株の買い戻し：国庫から支払い、消却する（発行残高の枠を空ける唯一の手段）。
     * 通常の買い戻しは市場価格、移籍・統一による強制売却の売れ残りは額面で行う——
     * いずれも国庫のみを原資とする点は同じ（国庫のみ。不足分は不履行＝デフォルト）。
     */
    public static NationalAccounts.Payment buyback(NationalAccounts.Balances issuer, long amount) {
        return NationalAccounts.payDomestic(issuer, amount);
    }

    /** その日の利子・配当の積み上げ額。活動日でなければ0（その日の分は消滅する）。 */
    public static long dailyAccrual(long monthlyOwed, int daysInMonth, boolean activeToday) {
        if (monthlyOwed < 0) {
            throw new IllegalArgumentException("月額が負である: " + monthlyOwed);
        }
        return activeToday ? monthlyOwed / daysInMonth : 0;
    }

    /** 利子・配当1件の支払い結果。 */
    public record Payout(NationalAccounts.Balances issuerAfter, long paid) {}

    /**
     * 積み上げた1か月分の利子・配当を支払う（毎月1日 05:00、準備高の月次減価より先に行う）。
     * 外交準備高からのみ支払い、国庫は補填しない。準備高が不足する場合は支払えるだけを
     * 支払い、残りは切り捨てる。
     *
     * @param accrued {@link #dailyAccrual} を1か月分積み上げた額
     */
    public static Payout settle(NationalAccounts.Balances issuer, long accrued) {
        if (accrued < 0) {
            throw new IllegalArgumentException("支払額が負である: " + accrued);
        }
        long paid = Math.min(issuer.reserve(), accrued);
        return new Payout(issuer.withReserve(issuer.reserve() - paid), paid);
    }

    /** 外交準備高が国債の利子を支払えるか。支払えなければデフォルト（配当は裁量のため対象外）。 */
    public static boolean bondInterestDefaulted(NationalAccounts.Balances issuer, long owedInterest) {
        return issuer.reserve() < owedInterest;
    }

    /** 発行残高の上限＝直近30日の準備高計上額 × {@value #ISSUANCE_CAP_MULTIPLIER}。 */
    public static long issuanceCap(long periodAccrual) {
        return periodAccrual * ISSUANCE_CAP_MULTIPLIER;
    }

    /** 新規発行後も発行残高（発行価格ベース、国債・国家株の合計）が上限内に収まるか。 */
    public static boolean canIssue(long currentOutstanding, long newIssuePrice, long periodAccrual) {
        return currentOutstanding + newIssuePrice <= issuanceCap(periodAccrual);
    }

    /** デフォルトの記録1件。{@code day} からの禁止期間は {@link #DEFAULT_BAN_DAYS}。 */
    public record DefaultRecord(String nation, long day) {}

    /** デフォルトを記録する。期限切れの記録は捨てる。 */
    public static List<DefaultRecord> recordDefault(List<DefaultRecord> existing, String nation, long today) {
        List<DefaultRecord> updated = new ArrayList<>();
        for (DefaultRecord d : existing) {
            if (today - d.day() < DEFAULT_BAN_DAYS) {
                updated.add(d);
            }
        }
        updated.add(new DefaultRecord(nation, today));
        return updated;
    }

    /** デフォルトにより新規発行が禁止されている期間中か。 */
    public static boolean issuanceBanned(String nation, long today, List<DefaultRecord> defaults) {
        return defaults.stream()
                .anyMatch(d -> d.nation().equals(nation) && today - d.day() < DEFAULT_BAN_DAYS);
    }
}
