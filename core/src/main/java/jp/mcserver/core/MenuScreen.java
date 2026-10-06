package jp.mcserver.core;

import java.util.ArrayList;
import java.util.EnumMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;

/**
 * メニュー画面の組み立て（§14）。
 *
 * <p>時計の使用で開くストレージ画面（9×6）を、プレイヤーの権限と状態から構成する。
 * 権限がない項目は表示せず、権限はあるが条件を満たさない項目は無効化して理由を示す。
 */
public final class MenuScreen {

    private MenuScreen() {}

    /** 1行のスロット数。 */
    public static final int COLUMNS = 9;

    /** 行数。ストレージ画面の最大。 */
    public static final int ROWS = 6;

    /**
     * 画面を組み立てるための状態。
     *
     * @param role                 国家における役職。無所属は null
     * @param rank                 国家ランク。無所属・野営地は -1
     * @param government           政治体制
     * @param inHub                Hub 内にいるか
     * @param inWarServer          戦争サーバーに在室しているか（§11.8）
     * @param hubCooldownSeconds   Hub 再入場クールダウンの残り秒数（§1.3）
     * @param serverDay            サーバー稼働日（§20）
     * @param headAbsent           首長が不在または空位か（§9.2）
     * @param isVassal             自国が属国か（§8.2）
     * @param hasExtraGate         追加ゲートを保有しているか（§12.4）
     * @param effectiveCitizens    実効国民数
     */
    public record Context(Role role, int rank, Government government, boolean inHub,
                          boolean inWarServer, int hubCooldownSeconds, int serverDay,
                          boolean headAbsent, boolean isVassal, boolean hasExtraGate,
                          int effectiveCitizens) {

        public boolean belongsToNation() {
            return role != null && rank >= 0;
        }

        public boolean inAssembly() {
            return government == Government.REPUBLIC && role != null && role.inAssembly();
        }
    }

    /**
     * 画面上の1項目。
     *
     * @param slot   スロット番号（0 始まり）
     * @param reason 無効化の理由。有効な場合は null
     */
    public record Slot(int slot, MenuEntry entry, boolean enabled, String reason) {}

    /** 画面を組み立てる。 */
    public static List<Slot> build(Context context) {
        Map<MenuEntry.Section, List<MenuEntry>> visible = new EnumMap<>(MenuEntry.Section.class);
        for (MenuEntry entry : MenuEntry.values()) {
            if (!visible(entry, context)) {
                continue;
            }
            visible.computeIfAbsent(entry.section(), k -> new ArrayList<>()).add(entry);
        }

        List<Slot> slots = new ArrayList<>();
        MenuEntry.Section[] sections = MenuEntry.Section.values();
        for (int row = 0; row < sections.length && row < ROWS; row++) {
            List<MenuEntry> entries = visible.getOrDefault(sections[row], List.of());
            for (int column = 0; column < entries.size() && column < COLUMNS; column++) {
                MenuEntry entry = entries.get(column);
                Optional<String> reason = disabledReason(entry, context);
                slots.add(new Slot(row * COLUMNS + column, entry, reason.isEmpty(),
                        reason.orElse(null)));
            }
        }
        return slots;
    }

    /** 権限を満たし、状況として表示すべき項目か（§14.2 — 権限がなければ表示しない）。 */
    static boolean visible(MenuEntry entry, Context context) {
        if (!hasAccess(entry.access(), context)) {
            return false;
        }
        return switch (entry) {
            // 移動先は現在地によって出し分ける
            case HUB_TRAVEL -> !context.inHub() && !context.inWarServer();
            case RETURN_OVERWORLD -> context.inHub() && !context.inWarServer();
            case CAPITAL_GATE -> context.inHub();
            case EXTRA_GATE -> context.inHub() && context.hasExtraGate();
            // 体制に固有の項目
            case SANCTION -> context.government() == Government.REPUBLIC;
            case WAR, HEIR_NOMINATE -> context.government() == Government.MONARCHY;
            case GOVERNMENT_SELECT -> context.rank() >= 15
                    || context.government() != Government.NONE;
            case ASSEMBLY_VOTE, IMPEACHMENT, ELECTION ->
                    context.government() == Government.REPUBLIC && context.belongsToNation();
            default -> true;
        };
    }

    static boolean hasAccess(MenuEntry.Access access, Context context) {
        Role role = context.role();
        return switch (access) {
            case EVERYONE -> true;
            case CITIZEN -> context.belongsToNation();
            case CHIEF -> role == Role.CHIEF || role == Role.LEADER || role == Role.HEAD;
            case LEADER -> role == Role.LEADER || role == Role.HEAD;
            case HEAD -> role == Role.HEAD;
            case ASSEMBLY -> context.inAssembly();
        };
    }

    /** 無効化の理由（§14.2 — 権限はあるが条件を満たさない場合）。 */
    static Optional<String> disabledReason(MenuEntry entry, Context context) {
        if (entry == MenuEntry.HUB_TRAVEL && context.hubCooldownSeconds() > 0) {
            return Optional.of("Hubへの再入場まであと " + context.hubCooldownSeconds() + " 秒です");
        }
        if (entry == MenuEntry.SANCTION || entry == MenuEntry.WAR) {
            if (!ServerTimeline.conflictAllowed(context.serverDay())) {
                return Optional.of("開始から " + ServerTimeline.CONFLICT_EMBARGO_DAYS
                        + " 日間は実施できません（あと "
                        + ServerTimeline.daysUntilConflictAllowed(context.serverDay()) + " 日）");
            }
        }
        if (entry == MenuEntry.SANCTION && context.rank() < 15) {
            return Optional.of("rank14 以下では制裁の発起権が停止します");
        }
        if (context.isVassal() && switch (entry) {
            case ALLIANCE, WAR, UNIFICATION -> true;
            default -> false;
        }) {
            return Optional.of("属国は同盟・戦争・統一を発議できません");
        }
        if (context.headAbsent() && suspendedPower(entry).isPresent()) {
            return Optional.of("首長の不在により停止しています");
        }
        if ((entry == MenuEntry.MARKET || entry == MenuEntry.RECRUITMENT) && !context.inHub()) {
            return Optional.of("Hub内でのみ利用できます");
        }
        return Optional.empty();
    }

    /** 首長の不在中に停止する権限（§9.2）との対応。 */
    static Optional<Succession.Power> suspendedPower(MenuEntry entry) {
        return switch (entry) {
            case TREASURY_SPEND -> Optional.of(Succession.Power.TREASURY_SPEND);
            case PROMOTION -> Optional.of(Succession.Power.PROMOTION);
            case ALLIANCE -> Optional.of(Succession.Power.ALLIANCE);
            case WAR -> Optional.of(Succession.Power.WAR);
            case UNIFICATION -> Optional.of(Succession.Power.UNIFICATION);
            case MEMBER_APPROVE, MEMBER_EXPEL -> Optional.of(Succession.Power.MEMBERSHIP);
            case CAPITAL_SET -> Optional.of(Succession.Power.CAPITAL_CHANGE);
            case GOVERNMENT_SELECT -> Optional.of(Succession.Power.GOVERNMENT_CHANGE);
            default -> Optional.empty();
        };
    }

    /** 画面に並んだ項目のうち有効なものの数。 */
    public static long enabledCount(List<Slot> slots) {
        return slots.stream().filter(Slot::enabled).count();
    }

    /** 指定の項目が画面上にあるか。 */
    public static Optional<Slot> find(List<Slot> slots, MenuEntry entry) {
        return slots.stream().filter(s -> s.entry() == entry).findFirst();
    }
}
