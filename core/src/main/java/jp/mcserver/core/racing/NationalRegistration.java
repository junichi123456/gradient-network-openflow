package jp.mcserver.core.racing;

import jp.mcserver.core.HorseTraining;

/**
 * 国別の重賞出走登録（`minecraft_server_spec.md` §27.10）。
 *
 * <p>1カ国が重賞（G3以上、{@link RaceCalendar}のレースはすべて対象）に登録できる
 * 個体は、同時に{@value #MAX_REGISTERED_HORSES}頭まで。新馬戦〜オープン
 * （{@link RaceClass}のうち{@link RaceClass#G3}未満）は登録不要で、誰でも自由に
 * 出走できる。
 *
 * <p>登録は前シーズンのスプリット3・4の間に行う（{@link #registrationWindowOpen}、
 * §27.9の{@link HorseTraining#classicSplit}と同じ判定——3歳馬クラシックの解禁と
 * 登録の受付期間が、たまたま同じスプリットを使う）。割り当ては先着順で、特定の
 * 役職の承認は要らない。
 */
public final class NationalRegistration {

    private NationalRegistration() {}

    /** 1カ国が重賞に同時登録できる個体数の上限（§27.10）。 */
    public static final int MAX_REGISTERED_HORSES = 4;

    /**
     * 国の登録枠にまだ空きがあり、新たに1頭を登録できるか。
     *
     * @param currentlyRegistered その国が現在登録している頭数
     */
    public static boolean canRegisterAnother(int currentlyRegistered) {
        if (currentlyRegistered < 0) {
            throw new IllegalArgumentException("登録頭数が負である: " + currentlyRegistered);
        }
        return currentlyRegistered < MAX_REGISTERED_HORSES;
    }

    /**
     * 登録の受付期間（前シーズンのスプリット3・4）が開いているか。§27.9で3歳馬
     * クラシックが解禁されるスプリットと同じ判定を流用する。
     */
    public static boolean registrationWindowOpen(int split) {
        return HorseTraining.classicSplit(split);
    }

    /** 登録済みの馬だけが重賞（G3以上）に出走できる。 */
    public static boolean gradedRaceEligible(boolean registered) {
        return registered;
    }
}
