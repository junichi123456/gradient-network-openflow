package jp.mcserver.core.raid;

import java.util.random.RandomGenerator;

/**
 * 盾によるガード（§12.6）。
 *
 * <p><b>パリイとは別の仕組みである。</b>パリイは「その区間に個体へ与えた累積ダメージ」で
 * 成立し、盾では成立しない。ガードは<b>受けるダメージを止めるだけ</b>で、
 * モーションを止めず、弱点も開かない。この切り分けは、受け身の防御を正解にしないための
 * 設計である（§12.6 のパリイの節）。
 *
 * <p>止められるもの
 * <ul>
 *   <li><b>武器の判定区間（{@link MotionSpec.DamageWindow}）はガードできる。</b>
 *       槍でも脚でも、当たり方に関係なく止まる</li>
 *   <li><b>範囲攻撃（{@link MotionSpec.AreaEffect}）はガードを貫通する。</b>
 *       地を伝わる衝撃波であり、盾を構えても止まらない</li>
 * </ul>
 *
 * <p>向き・軽減量・耐久の減り方はバニラの盾に合わせてある。プレイヤーの操作感を
 * 独自の規則で変えると、盾の挙動を覚え直させることになる。
 */
public final class ShieldGuard {

    private ShieldGuard() {
    }

    /**
     * ガードが成立する向きの範囲（度）。視線から左右この角度までに個体がいれば成立する。
     *
     * <p>90度はバニラと同じである。真横より後ろから来た攻撃は止められない。
     */
    public static final double ANGLE_DEGREES = 90.0;

    /**
     * ガードが成立したときに通すダメージの割合。
     *
     * <p><b>0 は全ブロックである（バニラ準拠）。</b>割合軽減にしたい場合はここを上げる。
     */
    public static final double DAMAGE_MULTIPLIER = 0.0;

    /** 範囲攻撃をガードできるか。<b>できない</b>（衝撃波は貫通する）。 */
    public static final boolean GUARDS_AREA_EFFECTS = false;

    /** 盾の耐久が減り始めるダメージ。これ未満では減らない（バニラと同じ）。 */
    public static final double DURABILITY_THRESHOLD = 3.0;

    /**
     * 個体がガードの正面にいるか。
     *
     * <p>バニラと同じ測り方をする。個体への向きを<b>水平に潰してから</b>視線と内積を取り、
     * 正なら正面と見なす。上や下を向いていてもガードは外れない。
     *
     * @param viewX     視線の X 成分
     * @param viewZ     視線の Z 成分
     * @param toSourceX プレイヤーから個体へ向かうベクトルの X 成分
     * @param toSourceZ 同じベクトルの Z 成分
     */
    public static boolean facing(double viewX, double viewZ,
                                 double toSourceX, double toSourceZ) {
        double viewLength = Math.hypot(viewX, viewZ);
        double sourceLength = Math.hypot(toSourceX, toSourceZ);
        if (viewLength < 1e-9 || sourceLength < 1e-9) {
            // 真上を向いている、または同じ位置に重なっている。向きが定まらないので通さない
            return false;
        }
        double cosine = (viewX * toSourceX + viewZ * toSourceZ) / (viewLength * sourceLength);
        return cosine > Math.cos(Math.toRadians(ANGLE_DEGREES));
    }

    /** ガードが成立したときに実際に通るダメージ。 */
    public static double damageThrough(double damage) {
        return damage * DAMAGE_MULTIPLIER;
    }

    /**
     * 盾の耐久が減る量（強度の付与を考える前）。
     *
     * <p>バニラと同じ式である。3未満のダメージでは減らず、それ以上では
     * <b>1 + ダメージの整数部</b>ぶん減る。騎士型の一撃は重いため、修繕なしでは
     * 何度も受けられない。
     */
    public static int durabilityCost(double damage) {
        if (damage < DURABILITY_THRESHOLD) {
            return 0;
        }
        return 1 + (int) Math.floor(damage);
    }

    /**
     * 強度（Unbreaking）を通したあとの減り量。
     *
     * <p>バニラの防具以外の規則に合わせ、1点ごとに {@code 1/(強度+1)} の確率で減らす。
     *
     * @param cost   {@link #durabilityCost} の値
     * @param level  強度の水準。0 なら素の盾
     * @param random 判定に使う乱数
     */
    public static int afterUnbreaking(int cost, int level, RandomGenerator random) {
        if (level <= 0) {
            return cost;
        }
        int applied = 0;
        for (int i = 0; i < cost; i++) {
            if (random.nextInt(level + 1) == 0) {
                applied++;
            }
        }
        return applied;
    }
}
