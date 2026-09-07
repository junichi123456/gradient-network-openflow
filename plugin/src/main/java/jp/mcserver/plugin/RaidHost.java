package jp.mcserver.plugin;

import java.time.LocalDate;
import java.time.LocalDateTime;
import java.time.temporal.ChronoUnit;
import java.util.EnumSet;
import java.util.List;
import java.util.Set;
import java.util.UUID;
import jp.mcserver.core.Raid;
import org.bukkit.Location;
import org.bukkit.entity.Player;
import org.bukkit.event.EventHandler;
import org.bukkit.event.Listener;
import org.bukkit.event.player.PlayerDeathEvent;
import org.bukkit.inventory.ItemStack;
import org.bukkit.scheduler.BukkitTask;

/**
 * 開催の進行（§12.1）。
 *
 * <p><b>登録 → 告知 → 締切 → 開始 → 制限時間 → 終了</b>の一巡を回す。枠の割り当てと
 * 「同日1枠」の規則は {@link Raid.DailyEntry} が持ち、ここは時刻とプレイヤーを繋ぐ。
 *
 * <p><b>会場はいまのワールドである。</b>レイド専用次元（枠ごとにロードし終了後に再生成）は
 * 別途の作業であり、ここでは {@code /raid arena} で置いた位置を会場として使う。
 * 次元を用意したときに差し替わるのは会場の決め方だけで、一巡の流れは変わらない。
 *
 * <p><b>単身討伐許可証</b>は開催枠の外側の経路である（§12.4）。開催日でなくても挑めるが
 * ソロ限定であり、同日1枠の消費もしない。枠の規則は「枠を並べて参加者を増やす」ための
 * ものであり、ソロ挑戦はその枠組みの外にある。
 */
final class RaidHost implements Listener {

    /**
     * 隔週日曜の基準日。
     *
     * <p>2026-01-04 は日曜である。ここから14日ごとが開催日になる（§12.1）。
     * 運用で変える場合はここを直す。
     */
    private static final LocalDate ANCHOR = LocalDate.of(2026, 1, 4);

    /** 進行の確認間隔（tick）。1秒ごとに見る。 */
    private static final long WATCH_INTERVAL = 20;

    private final RaidPlugin plugin;
    private final Raid.DailyEntry entry = new Raid.DailyEntry();
    /** すでに出した告知。出したかを覚えないと、確認の間隔を変えるたびに重なる */
    private final Set<Raid.Notice> announced = EnumSet.noneOf(Raid.Notice.class);
    /** 告知を数えている枠。枠が変わったら数え直す */
    private String announcedFor = "";

    private Location arena;
    private RaidSession session;
    private BukkitTask watcher;

    RaidHost(RaidPlugin plugin) {
        this.plugin = plugin;
    }

    void start() {
        watcher = plugin.getServer().getScheduler()
                .runTaskTimer(plugin, this::watch, WATCH_INTERVAL, WATCH_INTERVAL);
    }

    void stop() {
        if (watcher != null) {
            watcher.cancel();
        }
        if (session != null && session.running()) {
            // サーバーが止まる。報酬は配らない
            session.abandon();
            finish();
        }
    }

    // ------------------------------------------------------------ 進行

    /** 1秒ごとの確認。開催中なら進み具合を、そうでなければ次の枠の告知と開始を見る。 */
    private void watch() {
        if (session != null) {
            if (session.advance(System.currentTimeMillis()) != RaidSession.Outcome.RUNNING) {
                finish();
            }
            return;
        }
        LocalDateTime next = nextSlotStart();
        long minutes = ChronoUnit.MINUTES.between(LocalDateTime.now(), next);
        String key = next.toString();
        if (!key.equals(announcedFor)) {
            announcedFor = key;
            announced.clear();
        }
        for (Raid.Notice notice : Raid.dueNotices(minutes)) {
            if (announced.add(notice)) {
                announce(notice, next, minutes);
            }
        }
        if (minutes <= 0) {
            begin(slotOf(next), false);
        }
    }

    private void announce(Raid.Notice notice, LocalDateTime start, long minutes) {
        int slot = slotOf(start);
        plugin.getServer().broadcastMessage("§6[レイド] §f" + notice.label()
                + " — 第" + slot + "枠 " + start.getHour() + ":00 開始");
        if (Raid.registrationOpen(minutes)) {
            plugin.getServer().broadcastMessage("§7  /raid join " + slot
                    + " で登録（残り " + entry.remaining(today(), slot) + " 名）");
        } else {
            plugin.getServer().broadcastMessage("§7  登録は締め切りました");
        }
    }

    /**
     * 枠を開始する。
     *
     * @param slot 枠番号
     * @param forced 検証用に手で始めたか。定刻でない開始はログに残す
     */
    private void begin(int slot, boolean forced) {
        int day = today();
        if (entry.started(day, slot)) {
            return;
        }
        List<String> names = entry.participants(day, slot);
        if (names.isEmpty()) {
            if (forced) {
                plugin.getLogger().info("第" + slot + "枠に参加者がいないため開始しない");
            }
            return;
        }
        if (arena == null) {
            plugin.getServer().broadcastMessage(
                    "§c[レイド] 会場が未設定です。/raid arena で置いてください");
            return;
        }
        entry.start(day, slot);
        session = new RaidSession(day, slot, arena, System.currentTimeMillis());
        for (String name : names) {
            Player player = plugin.getServer().getPlayer(UUID.fromString(name));
            if (player != null) {
                session.admit(player);
            }
        }
        spawnBoss();
        plugin.getServer().broadcastMessage("§6[レイド] §f第" + slot + "枠を開始しました — "
                + session.participantCount() + " 名 / 制限時間 "
                + Raid.TIME_LIMIT_MINUTES + " 分"
                + (forced ? "§7（手動開始）" : ""));
    }

    /** 個体を出す。参加人数は戦場の内側にいる者で数えるため、迎え入れたあとに出す。 */
    private void spawnBoss() {
        KnightBoss boss = new KnightBoss(plugin, session.arena());
        boss.spawn();
        plugin.adopt(boss);
        session.boss(boss);
    }

    /** 終わりの片付け。討伐以外では報酬を配らない（§12.5）。 */
    private void finish() {
        RaidSession ended = session;
        session = null;
        if (ended == null) {
            return;
        }
        KnightBoss boss = ended.boss();
        switch (ended.outcome()) {
            case DEFEATED -> plugin.getServer().broadcastMessage(
                    "§6[レイド] §f第" + ended.slot() + "枠 — 討伐しました");
            case TIMEOUT -> plugin.getServer().broadcastMessage(
                    "§c[レイド] §f第" + ended.slot() + "枠 — 時間切れ。報酬はありません");
            case WIPED -> plugin.getServer().broadcastMessage(
                    "§c[レイド] §f第" + ended.slot() + "枠 — 全滅。報酬はありません");
            default -> { }
        }
        if (boss != null && !boss.isDead()) {
            plugin.retire(boss);
        }
        for (UUID id : ended.everyone()) {
            Player player = plugin.getServer().getPlayer(id);
            Location back = ended.cameFrom(id);
            if (player != null && back != null) {
                player.teleport(back);
            }
        }
    }

    /** 死亡を記録する。復帰はできない（§12.5）。 */
    @EventHandler
    public void onDeath(PlayerDeathEvent event) {
        if (session == null) {
            return;
        }
        session.fell(event.getEntity().getUniqueId());
    }

    // ------------------------------------------------------------ コマンド

    /**
     * 開催に関わる指示を受ける。
     *
     * @return 受け持った指示だったか
     */
    boolean handle(Player player, String[] args) {
        String action = args.length > 0 ? args[0] : "";
        switch (action) {
            case "slots" -> showSlots(player);
            case "join" -> join(player, args);
            case "leave" -> leave(player);
            case "solo" -> solo(player);
            case "arena" -> setArena(player);
            case "begin" -> {
                begin(args.length > 1 ? parseSlot(args[1]) : 1, true);
            }
            default -> {
                return false;
            }
        }
        return true;
    }

    private void showSlots(Player player) {
        LocalDateTime next = nextSlotStart();
        long minutes = ChronoUnit.MINUTES.between(LocalDateTime.now(), next);
        player.sendMessage("§6次の開催 §f" + next.toLocalDate() + " 第" + slotOf(next)
                + "枠 " + next.getHour() + ":00（あと " + minutes + " 分）");
        player.sendMessage("§7登録は" + (Raid.registrationOpen(minutes)
                ? "受付中（開始 " + Raid.REGISTRATION_CLOSES_MINUTES + " 分前まで）"
                : "締切"));
        int day = today();
        for (int slot = 1; slot <= Raid.SLOTS_PER_DAY; slot++) {
            player.sendMessage(String.format("§7  第%d枠 %2d:00 — 残り %2d / %d%s",
                    slot, Raid.slotHour(slot), entry.remaining(day, slot),
                    Raid.MAX_PARTICIPANTS,
                    entry.started(day, slot) ? " §8（開始済み）" : ""));
        }
        int mine = entry.slotOf(day, player.getUniqueId().toString());
        player.sendMessage(mine == 0 ? "§7あなたは未登録です"
                : "§aあなたは第" + mine + "枠に登録しています");
    }

    private void join(Player player, String[] args) {
        if (args.length < 2) {
            player.sendMessage("§7/raid join <枠番号 1〜" + Raid.SLOTS_PER_DAY + ">");
            return;
        }
        int slot = parseSlot(args[1]);
        LocalDateTime start = slotStart(slot);
        long minutes = ChronoUnit.MINUTES.between(LocalDateTime.now(), start);
        if (!Raid.registrationOpen(minutes)) {
            player.sendMessage("§c第" + slot + "枠の登録は締め切りました（開始 "
                    + Raid.REGISTRATION_CLOSES_MINUTES + " 分前まで）");
            return;
        }
        Raid.Entry result = entry.register(today(), slot, player.getUniqueId().toString());
        switch (result) {
            case ACCEPTED -> player.sendMessage("§a第" + slot + "枠に登録しました — "
                    + start.toLocalDate() + " " + start.getHour() + ":00");
            case SLOT_FULL -> player.sendMessage("§c第" + slot + "枠は満員です（上限 "
                    + Raid.MAX_PARTICIPANTS + " 名）");
            case ALREADY_TODAY -> player.sendMessage("§c同じ開催日に入れるのは1枠だけです（いまは第"
                    + entry.slotOf(today(), player.getUniqueId().toString()) + "枠）");
            case NO_SLOT -> player.sendMessage("§cその枠はありません");
            default -> { }
        }
    }

    private void leave(Player player) {
        if (entry.cancel(today(), player.getUniqueId().toString())) {
            player.sendMessage("§7登録を取り消しました");
            return;
        }
        player.sendMessage("§c取り消せません（未登録か、枠がすでに始まっています）");
    }

    /**
     * 単身討伐（§12.4 の単身討伐許可証）。
     *
     * <p>許可証を1枚消費し、その場で1人ぶんの個体を出す。<b>開催日でなくても挑める</b>が
     * ソロ限定であり、同日1枠の消費もしない。
     */
    private void solo(Player player) {
        if (session != null) {
            player.sendMessage("§c開催中です。終わってから挑んでください");
            return;
        }
        ItemStack permit = findPermit(player);
        if (permit == null) {
            player.sendMessage("§c単身討伐許可証がありません（討伐のドロップ品です）");
            return;
        }
        permit.setAmount(permit.getAmount() - 1);
        session = new RaidSession(today(), 0, player.getLocation(),
                System.currentTimeMillis());
        session.admit(player);
        spawnBoss();
        player.sendMessage("§6単身討伐 §f— 許可証を1枚使いました / 制限時間 "
                + Raid.TIME_LIMIT_MINUTES + " 分");
    }

    private ItemStack findPermit(Player player) {
        for (ItemStack item : player.getInventory().getContents()) {
            if (item != null && "solo_permit".equals(RaidLoot.idOf(item, plugin))) {
                return item;
            }
        }
        return null;
    }

    private void setArena(Player player) {
        arena = player.getLocation().clone();
        player.sendMessage("§a会場を置きました — " + arena.getBlockX() + ", "
                + arena.getBlockY() + ", " + arena.getBlockZ());
        player.sendMessage("§7レイド専用次元を用意したら、ここは次元側の座標に置き換わります");
    }

    // ------------------------------------------------------------ 時刻

    /** いまの開催日（エポック日）。同日1枠の判定に使う。 */
    private static int today() {
        return (int) LocalDate.now().toEpochDay();
    }

    /** 次に始まる枠の時刻。 */
    private LocalDateTime nextSlotStart() {
        LocalDateTime now = LocalDateTime.now();
        int sessionDay = Raid.nextSessionDay((int) ANCHOR.toEpochDay(), today());
        LocalDate date = LocalDate.ofEpochDay(sessionDay);
        if (date.equals(now.toLocalDate())) {
            for (int slot = 1; slot <= Raid.SLOTS_PER_DAY; slot++) {
                LocalDateTime at = date.atTime(Raid.slotHour(slot), 0);
                if (at.isAfter(now)) {
                    return at;
                }
            }
            date = date.plusDays(Raid.CYCLE_DAYS);
        }
        return date.atTime(Raid.slotHour(1), 0);
    }

    /** その枠の今回の開始時刻。 */
    private LocalDateTime slotStart(int slot) {
        int sessionDay = Raid.nextSessionDay((int) ANCHOR.toEpochDay(), today());
        return LocalDate.ofEpochDay(sessionDay).atTime(Raid.slotHour(slot), 0);
    }

    /** その時刻の枠番号。 */
    private static int slotOf(LocalDateTime at) {
        int slot = Raid.slotAt(at.getHour());
        return slot == 0 ? 1 : slot;
    }

    private static int parseSlot(String text) {
        try {
            return Integer.parseInt(text);
        } catch (NumberFormatException wrong) {
            return 0;
        }
    }
}
