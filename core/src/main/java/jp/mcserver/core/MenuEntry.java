package jp.mcserver.core;

/**
 * メニュー画面の項目（§14）。
 *
 * <p>各項目は「必要な権限」と「表示する行」を持つ。権限がない項目は表示せず、
 * 権限はあるが条件を満たさない項目は無効化して理由を示す（§14.2）。
 */
public enum MenuEntry {

    // 1行目: 情報
    LOCATION("現在地", Section.INFO, Access.EVERYONE, "地図"),
    NATION_INFO("自国情報", Section.INFO, Access.CITIZEN, "旗"),
    ACTIVITY("有効活動時間", Section.INFO, Access.EVERYONE, "時計"),
    GDP_RANKING("国内総生産ランキング", Section.INFO, Access.EVERYONE, "本"),

    // 2行目: 移動
    HUB_TRAVEL("Hubへ移動", Section.TRAVEL, Access.EVERYONE, "エンダーパール"),
    RETURN_OVERWORLD("主世界へ戻る", Section.TRAVEL, Access.EVERYONE, "草ブロック"),
    CAPITAL_GATE("自国首都へ", Section.TRAVEL, Access.CITIZEN, "門"),
    EXTRA_GATE("追加ゲートへ", Section.TRAVEL, Access.CITIZEN, "黒曜石"),

    // 3行目: 経済
    MARKET("GUI市場", Section.ECONOMY, Access.EVERYONE, "エメラルド"),
    RECRUITMENT("求人区画", Section.ECONOMY, Access.EVERYONE, "看板"),
    TREASURY_VIEW("国庫の残高と納入", Section.ECONOMY, Access.CITIZEN, "金塊"),
    SHULKER_BUY("シュルカーボックスの購入", Section.ECONOMY, Access.CITIZEN, "シュルカーボックス"),

    // 4行目: 国家運営
    CLAIM_TOOL("領土の操作", Section.ADMIN, Access.HEAD, "棒"),
    PROMOTION("昇格の申請", Section.ADMIN, Access.HEAD, "経験値瓶"),
    CAPITAL_SET("首都の指定", Section.ADMIN, Access.HEAD, "ビーコン"),
    TREASURY_SPEND("国庫の支出", Section.ADMIN, Access.HEAD, "金インゴット"),
    RECRUITMENT_POST("求人区画への出稿", Section.ADMIN, Access.HEAD, "羽根ペン"),
    MEMBER_APPROVE("登用の承認", Section.ADMIN, Access.LEADER, "紙"),
    MEMBER_EXPEL("除名の執行", Section.ADMIN, Access.LEADER, "バリア"),
    GATE_PLACE("追加ゲートの設置", Section.ADMIN, Access.LEADER, "エンドポータルフレーム"),
    DISPUTE("紛争の対応", Section.ADMIN, Access.CHIEF, "盾"),
    ESCALATE("除名・処罰の上申", Section.ADMIN, Access.CHIEF, "羊皮紙"),

    // 5行目: 外交
    ALLIANCE("同盟", Section.DIPLOMACY, Access.HEAD, "握手"),
    VASSALAGE("属国", Section.DIPLOMACY, Access.HEAD, "鎖"),
    UNIFICATION("統一", Section.DIPLOMACY, Access.HEAD, "ネザースター"),
    AID("援助金・救済", Section.DIPLOMACY, Access.HEAD, "金のリンゴ"),
    SANCTION("制裁の発起", Section.DIPLOMACY, Access.HEAD, "鉄格子"),
    WAR("戦争の発起", Section.DIPLOMACY, Access.HEAD, "鉄の剣"),

    // 6行目: 統治
    GOVERNMENT_SELECT("政治体制の選択", Section.POLITICS, Access.HEAD, "王冠"),
    ASSEMBLY_VOTE("議会の採決", Section.POLITICS, Access.ASSEMBLY, "鐘"),
    ELECTION("首長選挙", Section.POLITICS, Access.EVERYONE, "投票箱"),
    IMPEACHMENT("弾劾の発議", Section.POLITICS, Access.ASSEMBLY, "斧"),
    HEIR_NOMINATE("継承者の指名", Section.POLITICS, Access.HEAD, "金の王冠"),
    CLOSE("閉じる", Section.POLITICS, Access.EVERYONE, "レッドストーン");

    /** 表示する行。 */
    public enum Section { INFO, TRAVEL, ECONOMY, ADMIN, DIPLOMACY, POLITICS }

    /** 必要な権限。 */
    public enum Access {
        /** 無所属を含む全員。 */
        EVERYONE,
        /** 国家に所属する国民。 */
        CITIZEN,
        /** チーフ以上。 */
        CHIEF,
        /** リーダー以上。 */
        LEADER,
        /** 首長のみ。 */
        HEAD,
        /** 議会構成員（共和制）。 */
        ASSEMBLY
    }

    private final String label;
    private final Section section;
    private final Access access;
    private final String icon;

    MenuEntry(String label, Section section, Access access, String icon) {
        this.label = label;
        this.section = section;
        this.access = access;
        this.icon = icon;
    }

    public String label() {
        return label;
    }

    public Section section() {
        return section;
    }

    public Access access() {
        return access;
    }

    /** アイコンの用途を示す名前。実際の素材への対応は実装側で行う。 */
    public String icon() {
        return icon;
    }
}
