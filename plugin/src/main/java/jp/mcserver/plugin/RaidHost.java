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
import org.bukkit.World;
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
 * <p><b>会場はレイド専用次元である</b>（{@link RaidArena}）。枠が始まると参加者を
 * 中心から半径13以内へ強制的に送り、<b>20秒後に個体が高さ3から落ちてくる</b>。
 * 個体は常に北向きで出る。終了時は落下物を片付け、参加者を元の位置へ戻す。
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

    private RaidSession session;
    private BukkitTask watcher;
    /** 個体を出すまでの残り（tick）。0 未満なら出す予定は無い */
    private int spawnCountdown = -1;
    private final java.util.Random random = new java.util.Random();

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
            if (spawnCountdown >= 0) {
                spawnCountdown -= WATCH_INTERVAL;
                if (spawnCountdown <= 0) {
                    spawnCountdown = -1;
                    spawnBoss();
                } else if (spawnCountdown % (5 * 20) == 0) {
                    title(spawnCountdown / 20);
                }
                return;
            }
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
        World world = RaidArena.world(plugin);
        if (world == null) {
            plugin.getServer().broadcastMessage("§c[レイド] 会場（ワールド "
                    + RaidArena.WORLD + "）が読み込めません");
            return;
        }
        entry.start(day, slot);
        session = new RaidSession(day, slot, RaidArena.center(world),
                System.currentTimeMillis());
        for (String name : names) {
            Player player = plugin.getServer().getPlayer(UUID.fromString(name));
            if (player != null) {
                // 降ろす点は1人ずつ引く。1点に重ねると押し出しで弾かれる
                session.admit(player, RaidArena.entryPoint(world, random));
            }
        }
        // 個体は20秒後に落ちてくる。降りてから身構える間を置く
        spawnCountdown = RaidArena.SPAWN_DELAY_TICKS;
        plugin.getServer().broadcastMessage("§6[レイド] §f第" + slot + "枠を開始しました — "
                + session.participantCount() + " 名 / 制限時間 "
                + Raid.TIME_LIMIT_MINUTES + " 分"
                + (forced ? "§7（手動開始）" : ""));
        title(RaidArena.SPAWN_DELAY_TICKS / 20);
    }

    /**
     * 個体を出す。
     *
     * <p><b>高さ3から自由落下して地表面に到達し、常に北向きで出る</b>（会場の取り決め）。
     * 参加人数は戦場の内側にいる者で数えるため、迎え入れたあとに出す。
     */
    private void spawnBoss() {
        World world = session.arena().getWorld();
        KnightBoss boss = new KnightBoss(plugin, RaidArena.bossDrop(world),
                RaidArena.FACING_NORTH, true);
        boss.spawn();
        plugin.adopt(boss);
        session.boss(boss);
        plugin.getServer().broadcastMessage("§6[レイド] §f騎士型が降りてきた");
    }

    /** 出現までの数え。参加者にだけ出す。 */
    private void title(int seconds) {
        for (UUID id : session.everyone()) {
            Player player = plugin.getServer().getPlayer(id);
            if (player != null) {
                player.sendTitle("§6構えよ", "§7" + seconds + " 秒後に降りてくる", 5, 40, 10);
            }
        }
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
        spawnCountdown = -1;
        if (boss != null && !boss.isDead()) {
            plugin.retire(boss);
        }
        // 地形は変わらないので、片付けるのは落下物だけで足りる（RaidArena.sweep）
        World world = ended.arena().getWorld();
        if (world != null) {
            int swept = RaidArena.sweep(world);
            if (swept > 0) {
                plugin.getLogger().info("会場の落下物を " + swept + " 件片付けました");
            }
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
            case "arena" -> showArena(player);
            case "warp" -> warp(player);
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
        World world = RaidArena.world(plugin);
        if (world == null) {
            player.sendMessage("§c会場（ワールド " + RaidArena.WORLD + "）が読み込めません");
            return;
        }
        permit.setAmount(permit.getAmount() - 1);
        session = new RaidSession(today(), 0, RaidArena.center(world),
                System.currentTimeMillis());
        session.admit(player, RaidArena.entryPoint(world, random));
        spawnCountdown = RaidArena.SPAWN_DELAY_TICKS;
        player.sendMessage("§6単身討伐 §f— 許可証を1枚使いました / 制限時間 "
                + Raid.TIME_LIMIT_MINUTES + " 分");
        title(RaidArena.SPAWN_DELAY_TICKS / 20);
    }

    private ItemStack findPermit(Player player) {
        for (ItemStack item : player.getInventory().getContents()) {
            if (item != null && "solo_permit".equals(RaidLoot.idOf(item, plugin))) {
                return item;
            }
        }
        return null;
    }

    /** 会場の様子を示す。位置を置く指示ではなくなった（会場はレイド専用次元である）。 */
    private void showArena(Player player) {
        World world = RaidArena.world(plugin);
        if (world == null) {
            player.sendMessage("§c会場（ワールド " + RaidArena.WORLD + "）が読み込めません");
            player.sendMessage("§7サーバーの直下に " + RaidArena.WORLD
                    + " フォルダを置いて再起動してください");
            return;
        }
        player.sendMessage("§6会場 §f" + world.getName() + "（難易度 "
                + world.getDifficulty() + "）");
        player.sendMessage("§7  立つ高さ y=" + RaidArena.FLOOR_Y
                + " / 安全域 半径 " + (int) RaidArena.SAFE_RADIUS
                + " / 炎上 半径 " + (int) RaidArena.FIRE_RADIUS + " 以遠");
        player.sendMessage("§7  壁 半径 " + (int) RaidArena.WALL_RADIUS
                + " / 天井 y=" + RaidArena.CEILING_Y
                + "（頭上 " + (RaidArena.CEILING_Y - RaidArena.FLOOR_Y) + " ブロック）");
        player.sendMessage("§7  参加者は中心から半径 " + (int) RaidArena.ENTRY_RADIUS
                + " 以内へ、個体は高さ " + (int) RaidArena.DROP_HEIGHT
                + " から落下・北向き、開始から " + RaidArena.SPAWN_DELAY_TICKS / 20 + " 秒後");
        player.sendMessage("§7  移動するには /raid warp");
    }

    /** 会場へ移る（検証用）。 */
    private void warp(Player player) {
        World world = RaidArena.world(plugin);
        if (world == null) {
            player.sendMessage("§c会場が読み込めません");
            return;
        }
        player.teleport(RaidArena.entryPoint(world, random));
        player.sendMessage("§7会場へ移りました。戻るには自分で移動してください");
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
