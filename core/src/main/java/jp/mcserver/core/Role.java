package jp.mcserver.core;

/** 国家の役職（§6.1）。 */
public enum Role {
    /** 首長。 */
    HEAD("首長"),
    /** リーダー。 */
    LEADER("リーダー"),
    /** チーフ。 */
    CHIEF("チーフ"),
    /** 市民。 */
    CITIZEN("市民");

    private final String label;

    Role(String label) {
        this.label = label;
    }

    public String label() {
        return label;
    }

    /** 議会の構成員であるか（§9.1）。議会 = 役職者全員。 */
    public boolean inAssembly() {
        return this != CITIZEN;
    }
}
