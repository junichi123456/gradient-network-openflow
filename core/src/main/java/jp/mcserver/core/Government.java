package jp.mcserver.core;

/** 政治体制（§9）。rank15 で解禁されるため、それ未満は未選択である。 */
public enum Government {
    /** 未選択。 */
    NONE("未選択"),
    /** 共和制。 */
    REPUBLIC("共和制"),
    /** 君主制。 */
    MONARCHY("君主制");

    private final String label;

    Government(String label) {
        this.label = label;
    }

    public String label() {
        return label;
    }

    /** 同盟の保有数（§8.1）。 */
    public int allianceLimit() {
        return switch (this) {
            case REPUBLIC -> 2;
            case MONARCHY -> 1;
            case NONE -> 1;
        };
    }

    /** 役職枠の加算（§6.1）。共和制は首長以外が +1。 */
    public int roleBonus() {
        return this == REPUBLIC ? 1 : 0;
    }
}
