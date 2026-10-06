package jp.mcserver.core.raid;

/**
 * スピア（ヤリ。1.21.11 追加）をレイド個体へ通すための補正。
 *
 * <p>個体の当たり判定は生き物ではないため、バニラの攻撃力はそのまま届かない
 * （プラグインの {@code WeaponDamage} が組み立て直す）。スピアをバニラの値のまま
 * 組み立てると、ネザライトでも突き1回5・1.15秒に1回で毎秒4.3しか出ず、剣の基準
 * （毎秒19.2）の2割強にとどまる。<b>使える武器にするには補正が要る。</b>
 *
 * <p>スピアは間合いが長く（最大4.5ブロック。剣は3）、クリティカルのために跳ぶ必要も無い。
 * そのぶん安全に削れるため、剣の基準の約75%に合わせる（ユーザーへ確認して決定）。
 * スピアはクリティカルを出さないので、倍率は掛けない。
 *
 * <p>溜め突撃（使用ボタン長押し）は、相手との相対速度で威力が決まる。レイド次元には
 * 動物を連れて入れない（§12.1）ため騎乗の速さは出せず、徒歩の全力疾走（毎秒約5.6）だけでは
 * 閾値に届かない。<b>個体の突進を正面から迎え撃ったときにだけ成立する</b>。成立すれば
 * パリイと同じく技を止め、弱点を露出させる（迎え撃ち）。
 */
public final class SpearBalance {

    private SpearBalance() {}

    /** 剣の基準（エンチャント無しのネザライトの剣、毎回クリティカル）の毎秒ダメージ。 */
    public static final double SWORD_REFERENCE_DPS = 12.0 / 0.625;

    /** 剣の基準に対して、スピアの突きに許す毎秒ダメージの割合。 */
    public static final double SHARE_OF_SWORD = 0.75;

    /**
     * ネザライトのスピアの突き1回の補正後ダメージ。
     *
     * <p>19.2 × 0.75 × 1.15秒 = 16.56 を、表で扱いやすい 16.5 に丸める。
     * 他の素材はバニラの突きの値の比で割り振る（{@link #jab(Tier)}）。
     */
    public static final double NETHERITE_JAB = 16.5;

    /** 迎え撃ちが成立する、部位からの距離（ブロック）。バニラの溜め突撃の最大射程に合わせる。 */
    public static final double COUNTER_REACH = 2.25;

    /** 迎え撃ちのダメージ。接近の速さ（毎秒ブロック）1あたり。 */
    public static final double COUNTER_DAMAGE_PER_SPEED = 2.0;

    /** 迎え撃ち1回のダメージ上限。 */
    public static final double COUNTER_DAMAGE_CAP = 30.0;

    /**
     * 素材ごとの値。
     *
     * @param vanillaJab      バニラの突きのダメージ
     * @param cooldownTicks   突きの間隔（tick）
     * @param counterMinSpeed 迎え撃ちが成立する接近の速さ（毎秒ブロック）。
     *                        バニラの溜め突撃がノックバックを起こす速さに合わせた。
     *                        木は資料に無いため、石・金より1遅い値を置く（要実機確認）
     */
    public enum Tier {
        WOODEN(1, 13, 14),
        STONE(2, 15, 13),
        COPPER(2, 17, 12),
        IRON(3, 19, 11),
        GOLDEN(1, 19, 13),
        DIAMOND(4, 21, 10),
        NETHERITE(5, 23, 9);

        private final int vanillaJab;
        private final int cooldownTicks;
        private final double counterMinSpeed;

        Tier(int vanillaJab, int cooldownTicks, double counterMinSpeed) {
            this.vanillaJab = vanillaJab;
            this.cooldownTicks = cooldownTicks;
            this.counterMinSpeed = counterMinSpeed;
        }

        public int vanillaJab() {
            return vanillaJab;
        }

        public int cooldownTicks() {
            return cooldownTicks;
        }

        public double counterMinSpeed() {
            return counterMinSpeed;
        }
    }

    /** 突き1回の補正後ダメージ（クールダウンが明けている場合）。 */
    public static double jab(Tier tier) {
        return NETHERITE_JAB * tier.vanillaJab() / Tier.NETHERITE.vanillaJab();
    }

    /** 突きを間隔どおりに当て続けたときの毎秒ダメージ。 */
    public static double jabDps(Tier tier) {
        return jab(tier) / (tier.cooldownTicks() / 20.0);
    }

    /**
     * 迎え撃ちのダメージ。
     *
     * @param closingSpeed 相手へ近づく速さ（毎秒ブロック）。自分の速度から個体の速度を引いたものを、
     *                     自分から個体への向きへ射影した値
     * @return 成立しなければ 0
     */
    public static double counter(Tier tier, double closingSpeed) {
        if (closingSpeed < tier.counterMinSpeed()) {
            return 0;
        }
        return Math.min(COUNTER_DAMAGE_CAP, closingSpeed * COUNTER_DAMAGE_PER_SPEED);
    }
}
