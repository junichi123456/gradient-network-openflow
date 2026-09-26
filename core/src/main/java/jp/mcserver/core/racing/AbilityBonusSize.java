package jp.mcserver.core.racing;

/**
 * 特性による「能力プラス補正」の大きさ（`minecraft_server_spec.md` §27.7.2）。
 *
 * <p>5大能力値（§27.3）のいずれに対しても、小=+1・中=+3・大=+5・特大=+7を、
 * 特性抜きのステータスの**上限**にさらに加算する。複数の特性が同時に発動すれば
 * 加算は積み上がる——能力値100の馬に小補正が3つ発動すれば103になる。
 */
public enum AbilityBonusSize {
    SMALL(1),
    MEDIUM(3),
    LARGE(5),
    EXTRA_LARGE(7);

    private final int bonus;

    AbilityBonusSize(int bonus) {
        this.bonus = bonus;
    }

    /** この補正が上限に加算する値。 */
    public int bonusValue() {
        return bonus;
    }
}
