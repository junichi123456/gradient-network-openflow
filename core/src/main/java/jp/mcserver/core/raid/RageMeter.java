package jp.mcserver.core.raid;

/**
 * 激昂（§12.6）。<b>戦闘が停滞したとき</b>に個体を強化する。
 *
 * <p>強化の引き金を「プレイヤーが上手く立ち回ったこと」に置くと、上手い立ち回りが
 * 罰になってしまう。そこで引き金は<b>個体の攻撃が一定時間まったく命中していないこと</b>
 * に置く。遠くから削るだけ、逃げ回るだけ、という組み立てを咎める一方で、
 * 前に出て捌く組み立ては罰しない。
 *
 * <p>激昂中は待機モーションが短くなりダメージが増すかわりに、露出型の弱点が閉じる。
 * したがって「激昂を待ってから殴る」ことは得にならず、激昂させないほうが速く倒せる。
 */
public final class RageMeter {

    /** 命中が無いまま激昂に至るまでの時間（tick）。20tick = 1秒。 */
    public static final int STALL_TICKS = 400;

    /** 激昂の持続時間（tick）。 */
    public static final int ENRAGED_TICKS = 200;

    /** 激昂中のダメージ倍率。 */
    public static final double DAMAGE_MULTIPLIER = 1.2;

    /** 激昂中の待機モーションの長さ（tick）。 */
    public static final int ENRAGED_IDLE_TICKS = 15;

    private int stall;
    private int remaining;

    /** 1tick 進める。 */
    public void tick() {
        if (remaining > 0) {
            remaining--;
            if (remaining == 0) {
                stall = 0;
            }
            return;
        }
        stall++;
        if (stall >= STALL_TICKS) {
            remaining = ENRAGED_TICKS;
        }
    }

    /** 個体の攻撃が命中した。停滞の計測をやり直す。 */
    public void landedHit() {
        if (remaining == 0) {
            stall = 0;
        }
    }

    public boolean enraged() {
        return remaining > 0;
    }

    /** 激昂が始まった tick かを判定するために使う。 */
    public boolean justEnraged() {
        return remaining == ENRAGED_TICKS;
    }

    public int remaining() {
        return remaining;
    }

    /** 停滞の蓄積（tick）。 */
    public int stall() {
        return stall;
    }

    /** 激昂までの残り時間（tick）。激昂中は 0。 */
    public int untilEnrage() {
        return enraged() ? 0 : Math.max(0, STALL_TICKS - stall);
    }

    /** 激昂を反映した待機モーションの長さ。 */
    public int idleTicks(int normal) {
        return enraged() ? Math.min(normal, ENRAGED_IDLE_TICKS) : normal;
    }

    /** 激昂を反映したダメージ倍率。 */
    public double damageMultiplier() {
        return enraged() ? DAMAGE_MULTIPLIER : 1.0;
    }

    /** 形態移行でやり直す。 */
    public void reset() {
        stall = 0;
        remaining = 0;
    }
}
