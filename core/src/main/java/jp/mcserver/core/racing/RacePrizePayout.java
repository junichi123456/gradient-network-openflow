package jp.mcserver.core.racing;

/**
 * 競走馬が獲得する賞金（1着賞金・§27.8.5の制覇ボーナスいずれも）の振り分け
 * （`minecraft_server_spec.md` §27.5・§27.8.5）。
 *
 * <p>賞金のexpは全額、馬主が属する国の国庫（§7、
 * {@code jp.mcserver.core.NationalAccounts#receiveRacePrize}）に入金する——馬主
 * 個人のexpとしては受け取れない。馬側に残るのは、§27.5の重賞（G3）昇級判定や
 * §27.8.5の制覇ボーナス判定に使う「累積獲得賞金」という記録専用の数値のみで、
 * 実際に使えるexpではない。**この2つは同じ金額をそれぞれ別目的で計上するもの
 * であり、国庫と馬とで山分けする分割ではない**（旧版の7:3配分は廃止した）。
 *
 * @param treasuryCreditExp 馬主の国の国庫に入金するexp
 * @param horseCumulativePrizeExp 馬の累積獲得賞金（記録専用）に加算する額
 */
public record RacePrizePayout(long treasuryCreditExp, long horseCumulativePrizeExp) {

    public RacePrizePayout {
        if (treasuryCreditExp < 0 || horseCumulativePrizeExp < 0) {
            throw new IllegalArgumentException(
                    "賞金額が負である: 国庫=" + treasuryCreditExp + " 馬の記録=" + horseCumulativePrizeExp);
        }
    }

    /**
     * 賞金expからの振り分けを求める。全額が国庫への入金・馬の累積獲得賞金への加算の
     * 両方に計上される。
     */
    public static RacePrizePayout of(long prizeExp) {
        if (prizeExp < 0) {
            throw new IllegalArgumentException("賞金額が負である: " + prizeExp);
        }
        return new RacePrizePayout(prizeExp, prizeExp);
    }
}
