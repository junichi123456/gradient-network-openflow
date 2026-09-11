package jp.mcserver.core;

import java.util.ArrayList;
import java.util.List;

/**
 * 援助金の受領記録と上限（§7.3）。
 *
 * <p>受領国が直近30日間に受け取れる援助金には上限がある。援助金は外交準備高が国庫へ変わる
 * 唯一の経路であるため、上限を置かなければ往復させるだけで国内へ流し込めてしまう。
 *
 * <p>上限 = max(30,000, 自国の国内総生産 × 20%)。下限を置くのは、蓄積の乏しい国が
 * 援助を受けられなくなるのを避けるためである。
 */
public final class AidLedger {

    /** 上限の算定期間（日）。 */
    public static final int WINDOW_DAYS = 30;

    /** 国内総生産に対する上限の割合（%）。 */
    public static final int CAP_PERCENT = 20;

    /** 蓄積が乏しい国のための下限（exp）。個人の日次上限1日分に相当する。 */
    public static final long CAP_FLOOR = 30_000;

    private record Receipt(long day, long amount) {}

    private final List<Receipt> receipts = new ArrayList<>();

    /** 直近30日に受領した額。 */
    public long receivedInWindow(long today) {
        long total = 0;
        for (Receipt r : receipts) {
            if (today - r.day() < WINDOW_DAYS) {
                total += r.amount();
            }
        }
        return total;
    }

    /** 受領国の国内総生産から定まる上限。 */
    public static long cap(long gdp) {
        return Math.max(CAP_FLOOR, gdp * CAP_PERCENT / 100);
    }

    /** あと受け取れる額。 */
    public long remaining(long today, long gdp) {
        return Math.max(0, cap(gdp) - receivedInWindow(today));
    }

    /** 受領を記録する。古い記録は捨てる。 */
    public void record(long today, long amount) {
        if (amount < 0) {
            throw new IllegalArgumentException("受領額が負である: " + amount);
        }
        receipts.removeIf(r -> today - r.day() >= WINDOW_DAYS);
        receipts.add(new Receipt(today, amount));
    }

    /** 判定の結果。 */
    public record Check(boolean allowed, long remaining, String message) {}

    /**
     * 受け取れるかを判定する。上限を超える援助は<b>部分的にも実行しない</b>。
     *
     * @param delivered 受領国の勘定に入る額（償却後）
     */
    public Check check(long today, long gdp, long delivered) {
        long remaining = remaining(today, gdp);
        if (delivered <= remaining) {
            return new Check(true, remaining, "受領可能です（30日の残枠 " + remaining + "）");
        }
        return new Check(false, remaining,
                "30日あたりの受領上限を超えています（残枠 " + remaining + " / 上限 " + cap(gdp) + "）");
    }
}
