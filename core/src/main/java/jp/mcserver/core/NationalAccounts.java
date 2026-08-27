package jp.mcserver.core;

/**
 * 国庫と外交準備高（§7）。
 *
 * <p>国家は2つの勘定を持つ。
 * <ul>
 *   <li><b>国庫</b> — 国内の支払いに用いる。国民の任意納入と援助金の受領で増える</li>
 *   <li><b>外交準備高</b> — 国民の稼得 exp の 0.5 倍が自動的に積み上がる。国内では流通できず、
 *       用途は外交で発生するコストに限られる</li>
 * </ul>
 *
 * <p>対外受取は外交準備高へ、対外支払いは外交準備高から行う。国庫は国内専用である。
 * この分離により、外交準備高から国庫への流入経路は援助金（3%の償却を伴う）だけになる。
 */
public final class NationalAccounts {

    private NationalAccounts() {}

    /** 稼得 exp に対する外交準備高の計上率。 */
    public static final double ACCRUAL_RATE = 0.5;

    /** 援助金に対する世界政府への償却率（%）。 */
    public static final int AID_BURN_PERCENT = 3;

    /** 外交準備高の月次減価率（%）。使われない準備高は毎月この割合が償却される。 */
    public static final int DECAY_PERCENT = 10;

    /** 勘定の残高。 */
    public record Balances(long treasury, long reserve) {

        public Balances {
            if (treasury < 0 || reserve < 0) {
                throw new IllegalArgumentException("残高が負である: 国庫=" + treasury + " 準備高=" + reserve);
            }
        }

        public static Balances empty() {
            return new Balances(0, 0);
        }

        /** 国内総生産（§7.2）。ランキングの指標。 */
        public long gdp() {
            return treasury + reserve;
        }

        public Balances withTreasury(long value) {
            return new Balances(value, reserve);
        }

        public Balances withReserve(long value) {
            return new Balances(treasury, value);
        }
    }

    /**
     * 国民の稼得 exp から外交準備高へ計上される額（§7.1）。
     *
     * <p>プレイヤーの手取りは減らない。国家の勘定に並行して積み上がる。
     */
    public static long accrual(long earnedExp) {
        if (earnedExp < 0) {
            throw new IllegalArgumentException("稼得が負である: " + earnedExp);
        }
        return (long) Math.floor(earnedExp * ACCRUAL_RATE);
    }

    /** 国民の稼得を反映した残高。 */
    public static Balances accrue(Balances b, long earnedExp) {
        return b.withReserve(b.reserve() + accrual(earnedExp));
    }

    /** 国民の任意納入（§7）。国庫にのみ入る。 */
    public static Balances donate(Balances b, long amount) {
        if (amount < 0) {
            throw new IllegalArgumentException("納入額が負である: " + amount);
        }
        return b.withTreasury(b.treasury() + amount);
    }

    /**
     * 支払いの結果。
     *
     * @param unpaid 残高不足で支払えなかった額
     */
    public record Payment(Balances after, long fromReserve, long fromTreasury, long unpaid) {
        public boolean fulfilled() {
            return unpaid == 0;
        }
    }

    /**
     * 外交コストの支払い（§7.1）。同盟継続料、属国上納、制裁、移籍金、援助金が該当する。
     *
     * <p>外交準備高から支払い、不足分は国庫から補填する。それでも足りない分は不履行となり、
     * §7 の支出優先順位と各制度の不履行規定に従う。
     */
    public static Payment payDiplomatic(Balances b, long amount) {
        requireAmount(amount);
        long fromReserve = Math.min(b.reserve(), amount);
        long rest = amount - fromReserve;
        long fromTreasury = Math.min(b.treasury(), rest);
        long unpaid = rest - fromTreasury;
        Balances after = new Balances(b.treasury() - fromTreasury, b.reserve() - fromReserve);
        return new Payment(after, fromReserve, fromTreasury, unpaid);
    }

    /**
     * 国内の支払い（支度金・定期給付）。国庫からのみ支払う。
     *
     * <p>外交準備高は国内で流通できないため、不足しても補填されない。
     */
    public static Payment payDomestic(Balances b, long amount) {
        requireAmount(amount);
        long fromTreasury = Math.min(b.treasury(), amount);
        long unpaid = amount - fromTreasury;
        return new Payment(b.withTreasury(b.treasury() - fromTreasury), 0, fromTreasury, unpaid);
    }

    /** 対外受取（同盟料・上納・制裁の分配・移籍金）。外交準備高に入る。 */
    public static Balances receiveDiplomatic(Balances b, long amount) {
        requireAmount(amount);
        return b.withReserve(b.reserve() + amount);
    }

    /**
     * 援助金の送付結果（§7.3）。
     *
     * @param burned    世界政府へ償却される額
     * @param delivered 受領国の国庫に入る額
     */
    public record Aid(Balances senderAfter, Balances receiverAfter,
                      long burned, long delivered, long unpaid) {
        public boolean fulfilled() {
            return unpaid == 0;
        }
    }

    /** 援助金の償却額（切り上げ）。端数は世界政府に寄せる。 */
    public static long aidBurn(long amount) {
        requireAmount(amount);
        return (amount * AID_BURN_PERCENT + 99) / 100;
    }

    /**
     * 援助金の送付（§7.3）。
     *
     * <p>送付側は外交準備高から支払い、3%を世界政府へ償却する。
     * 受領国は残額を<b>国庫</b>に計上できる。外交準備高が国庫へ変わる唯一の経路である。
     *
     * @param toTreasury 受領国が国庫で受け取るか（false なら外交準備高で受け取る）
     */
    public static Aid sendAid(Balances sender, Balances receiver, long amount, boolean toTreasury) {
        requireAmount(amount);
        Payment payment = payDiplomatic(sender, amount);
        long paid = amount - payment.unpaid();
        long burned = aidBurn(paid);
        long delivered = paid - burned;
        Balances receiverAfter = toTreasury
                ? receiver.withTreasury(receiver.treasury() + delivered)
                : receiver.withReserve(receiver.reserve() + delivered);
        return new Aid(payment.after(), receiverAfter, burned, delivered, payment.unpaid());
    }

    /** 月次減価の結果。 */
    public record Decay(Balances after, long burned) {}

    /**
     * 外交準備高の月次減価（§7.1）。
     *
     * <p>毎月1日 05:00 に、外交準備高の 10% を世界政府へ償却する。端数は切り上げる。
     * 国庫は対象外である。
     *
     * <p>使われない準備高が無限に積み上がると、外交を行わない国が蓄積だけで
     * 国内総生産の上位を占め続ける。減価はこれを防ぎ、準備高に「使う理由」を与える。
     * 月間の計上額を P とすると、残高は (1-0.1)P/0.1 = 9P に収束する。
     */
    public static Decay decayMonthly(Balances b) {
        long burned = (b.reserve() * DECAY_PERCENT + 99) / 100;
        return new Decay(b.withReserve(b.reserve() - burned), burned);
    }

    private static void requireAmount(long amount) {
        if (amount < 0) {
            throw new IllegalArgumentException("金額が負である: " + amount);
        }
    }
}
