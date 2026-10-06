package jp.mcserver.core;

/**
 * 現在チャンクとプレイヤーの関係（§4, §8）。
 *
 * <p>棒の所持に関わらず、すべてのプレイヤーに表示される。
 */
public enum TerritoryRelation {
    /** 自国領土。 */
    OWN("自国"),
    /** 同盟国の領土（§8.1）。 */
    ALLY("同盟国"),
    /** 自国の属国の領土（§8.2）。 */
    VASSAL("属国"),
    /** 自国が従属する宗主国の領土（§8.2）。 */
    SUZERAIN("宗主国"),
    /** いずれの関係もない他国の領土。 */
    FOREIGN("他国"),
    /** 主世界中央の保護区（§1.2）。 */
    PROTECTED_ZONE("保護区"),
    /** 領土外。 */
    WILDERNESS("領土外");

    private final String label;

    TerritoryRelation(String label) {
        this.label = label;
    }

    public String label() {
        return label;
    }

    public boolean isTerritory() {
        return this != WILDERNESS && this != PROTECTED_ZONE;
    }
}
