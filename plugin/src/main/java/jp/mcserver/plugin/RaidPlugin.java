package jp.mcserver.plugin;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import jp.mcserver.core.raid.KnightDefinition;
import jp.mcserver.core.raid.RaidDrop;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.command.Command;
import org.bukkit.command.CommandSender;
import org.bukkit.entity.Entity;
import org.bukkit.entity.Player;
import org.bukkit.entity.Projectile;
import org.bukkit.entity.Trident;
import org.bukkit.event.EventHandler;
import org.bukkit.event.EventPriority;
import org.bukkit.event.Listener;
import org.bukkit.event.entity.EntityDamageByEntityEvent;
import org.bukkit.event.entity.EntityDamageEvent;
import org.bukkit.event.entity.ProjectileHitEvent;
import org.bukkit.event.entity.ProjectileLaunchEvent;
import org.bukkit.event.inventory.PrepareItemCraftEvent;
import org.bukkit.inventory.ItemStack;
import org.bukkit.plugin.java.JavaPlugin;
import org.bukkit.projectiles.ProjectileSource;

/**
 * レイド個体の検証用プラグイン（§12）。
 *
 * <p>コマンド
 * <ul>
 *   <li>{@code /raid spawn} — 足元に騎士型を召喚する</li>
 *   <li>{@code /raid despawn} — 召喚した個体をすべて除去する</li>
 *   <li>{@code /raid info} — 状態を表示する</li>
 *   <li>{@code /raid god} — 自分の体力を減らさない（検証用の切り替え）</li>
 *   <li>{@code /raid calibrate} — モデルの原点を較正する立方体を出す（§7）</li>
 *   <li>{@code /raid dump} — 表示へ送っている変換と当たり判定の位置を数値で出す</li>
 * </ul>
 */
public final class RaidPlugin extends JavaPlugin implements Listener {

    private final List<RaidBoss> active = new ArrayList<>();

    /**
     * 飛び道具の発射地点（§12.6）。
     *
     * <p>遠くから放たれた攻撃を通さないため、<b>撃った位置</b>を覚えておく。
     * 射手の現在位置で見ると、遠くから撃って踏み込むだけで通ってしまう。
     */
    private final Map<UUID, Location> launchPoints = new HashMap<>();

    /**
     * 体力を減らさないプレイヤー（検証用）。
     *
     * <p><b>ダメージそのものは通す。</b>減った体力を次tickで元へ戻す形にしているため、
     * ハートが一度減って戻り、当たったことが目で分かる。死ぬ一撃だけは無効にする。
     * 個体側の命中の記録（激昂の判定）もそのまま働く。
     */
    private final Set<UUID> unkillable = new HashSet<>();

    /** 開催の進行（§12.1）。 */
    private final RaidHost host = new RaidHost(this);

    /** ドロップの抽選に使う乱数。 */
    private final java.util.Random random = new java.util.Random();

    /** 較正用に出した表示エンティティ（`raid_model_spec.md` §7）。 */
    private final List<Entity> calibration = new ArrayList<>();

    @Override
    public void onEnable() {
        getServer().getPluginManager().registerEvents(this, this);
        // 村人の取引テーブル（§3.2）。レイドとは独立だが、常駐の購読はここへ集約する
        getServer().getPluginManager().registerEvents(new VillagerTradeFilter(getLogger()), this);
        // 『消滅の呪い』の全面付与（§3.1）。1経路でも漏れると蓄積の抜け道になる
        getServer().getPluginManager()
                .registerEvents(new VanishingCurseEnforcer(getLogger()), this);
        // 開催の進行（§12.1）。登録・告知・開始・制限時間を回す
        getServer().getPluginManager().registerEvents(host, this);
        // レイド専用次元の保護（設置・破壊・PvP の禁止）
        getServer().getPluginManager().registerEvents(RaidArena.guard(), this);
        host.start();
        // jar の日時を出す。差し替えたつもりで古い jar が動いている、という取り違えを防ぐ
        getLogger().info("レイド検証プラグインを有効化しました（jar " + jarStamp() + "）");
    }

    @Override
    public void onDisable() {
        // 開催中なら畳む。報酬は配らない（§12.5）
        host.stop();
        // 表示エンティティを残さない（§12.6 の死活管理）
        despawnAll();
    }

    @Override
    public boolean onCommand(CommandSender sender, Command command, String label, String[] args) {
        if (!(sender instanceof Player player)) {
            sender.sendMessage("プレイヤーから実行してください");
            return true;
        }
        String action = args.length > 0 ? args[0] : "info";
        // 開催の指示（登録・告知・開始）は RaidHost が受け持つ
        if (host.handle(player, args)) {
            return true;
        }
        switch (action) {
            case "spawn" -> {
                String species = args.length > 1 ? args[1] : "knight";
                switch (species) {
                    case "hollow" -> {
                        HollowGuardBoss boss = new HollowGuardBoss(this, player.getLocation());
                        boss.spawn();
                        active.add(boss);
                        player.sendMessage("虚刃の衛士を召喚しました（体力66%から特殊系統が解禁されます）");
                    }
                    default -> {
                        KnightBoss boss = new KnightBoss(this, player.getLocation());
                        boss.spawn();
                        active.add(boss);
                        player.sendMessage("騎士型を召喚しました（参加人数 "
                                + boss.participants() + " / 体力 " + boss.maxHealth() + "）");
                    }
                }
            }
            case "despawn" -> {
                int count = despawnAll();
                player.sendMessage(count + " 体を除去しました");
            }
            case "calibrate" -> {
                clearCalibration();
                // ブロックの中心・地表に置く。座標が読みやすいほうがずれを測れる
                Location at = player.getLocation().getBlock().getLocation().add(0.5, 0, 0.5);
                calibration.addAll(Calibration.spawn(at));
                player.sendMessage("較正用の立方体を " + format(at)
                        + " から東へ4つ並べました");
                for (int i = 0; i < Calibration.LABELS.size(); i++) {
                    player.sendMessage("§7  " + (i + 1) + "つめ（西から）: §f"
                            + Calibration.LABELS.get(i));
                }
                player.sendMessage("§7モデル本来の色: 上=黄緑 下=赤 北=青 南=黄 西=白 東=黒");
                player.sendMessage("§7各立方体の§f天面の色§7と§f北面の色§7を読めば、"
                        + "掛かっている回転が一意に決まります");
                player.sendMessage("§7赤い小さな印がエンティティの位置です。"
                        + "立方体の§f中心§7に印があれば原点は想定どおりです");
                player.sendMessage("§7マゼンタの角がモデル座標 (0,0,0) です");
                player.sendMessage("§7消すときは /raid despawn");
            }
            case "axes" -> {
                clearCalibration();
                Location at = player.getLocation().getBlock().getLocation().add(0.5, 1, 0.5);
                calibration.addAll(Axes.spawn(at));
                player.sendMessage("基準の十字を " + format(at) + " から東へ2つ出しました");
                for (int i = 0; i < Axes.LABELS.size(); i++) {
                    player.sendMessage("§7  " + (i + 1) + "つめ（西から）: §f"
                            + Axes.LABELS.get(i));
                }
                player.sendMessage("§7棒は部位とまったく同じ手順で出しています。"
                        + "指す向きが上のとおりなら、描画へ渡す行列は正しいということです");
                player.sendMessage("§7F3 で向きを確かめてください。消すときは /raid despawn");
            }
            case "dump" -> {
                if (active.isEmpty()) {
                    player.sendMessage("召喚中の個体はありません");
                } else {
                    active.forEach(boss -> boss.describe().forEach(line -> {
                        player.sendMessage(line);
                        getLogger().info(line);
                    }));
                    player.sendMessage("§7同じ内容をサーバーのログにも出しました");
                }
            }
            case "model" -> {
                String mode = args.length > 1 ? args[1] : "";
                switch (mode) {
                    case "authored" -> switchModels(player, true);
                    case "vanilla" -> switchModels(player, false);
                    default -> {
                        player.sendMessage("いまの見た目: "
                                + (KnightDefinition.authoredModels()
                                        ? "描いたモデル" : "バニラの素材"));
                        player.sendMessage("§7/raid model authored"
                                + " … リソースパックで描いたモデルを使う");
                        player.sendMessage("§7/raid model vanilla"
                                + " … バニラの素材を寸法どおりに引き伸ばす");
                    }
                }
            }
            case "god" -> {
                if (unkillable.remove(player.getUniqueId())) {
                    player.sendMessage("体力を通常に戻しました");
                } else {
                    unkillable.add(player.getUniqueId());
                    player.sendMessage("体力を減らさないようにしました（当たった演出は残ります）");
                }
            }
            default -> {
                if (active.isEmpty()) {
                    player.sendMessage("召喚中の個体はありません");
                } else {
                    active.forEach(boss -> player.sendMessage(boss.status()));
                }
            }
        }
        return true;
    }

    /**
     * 見た目の方式を切り替える。
     *
     * <p>骨格は個体を作るときに組むため、<b>出し直さないと反映されない</b>。
     * 塗り直した絵を見るための道具なので、出ている個体はその場で作り直す。
     */
    private void switchModels(Player player, boolean authored) {
        if (KnightDefinition.authoredModels() == authored) {
            player.sendMessage("すでに"
                    + (authored ? "描いたモデル" : "バニラの素材") + "です");
            return;
        }
        KnightDefinition.useAuthoredModels(authored);
        int reborn = 0;
        List<Location> places = new ArrayList<>();
        for (RaidBoss boss : active) {
            if (boss instanceof KnightBoss knight) {
                places.add(knight.location());
            }
        }
        // 描いたモデルの方式は騎士型だけが持つ。虚刃の衛士はそのまま残す
        active.removeIf(boss -> {
            if (boss instanceof KnightBoss) {
                boss.despawn();
                return true;
            }
            return false;
        });
        for (Location place : places) {
            KnightBoss boss = new KnightBoss(this, place);
            boss.spawn();
            active.add(boss);
            reborn++;
        }
        player.sendMessage("見た目を"
                + (authored ? "描いたモデル" : "バニラの素材") + "に切り替えました"
                + (reborn > 0 ? "（" + reborn + " 体を出し直しました）" : ""));
        if (authored) {
            player.sendMessage("§7絵を塗り直したら F3+T でリソースパックを読み直し、"
                    + "このコマンドをもう一度実行すると反映されます");
        }
    }

    /**
     * ドロップを配る（§12.4）。
     *
     * <p><b>確定贈与である。</b>資格のある者には必ず全種が渡る。持ち物が満杯なら足元へ落とす。
     * 資格は「体力の 1/(参加人数×1.5) 以上を削ったか」で、生死は問わない（§12.5）。
     */
    private void grantDrops(RaidBoss boss) {
        List<java.util.UUID> rewarded = boss.rewarded();
        if (rewarded.isEmpty()) {
            getServer().broadcastMessage("§7ドロップの条件（"
                    + String.format("%.0f", boss.rewardThreshold())
                    + " ダメージ）を満たした者がいませんでした");
            return;
        }
        for (java.util.UUID id : rewarded) {
            Player player = getServer().getPlayer(id);
            if (player == null) {
                // 離脱した者へは渡せない。取り置きは永続化の話になるため、いまは記録だけ
                getLogger().info("ドロップの受け取り手が不在: " + id);
                continue;
            }
            List<String> got = new ArrayList<>();
            for (var grant : RaidDrop.roll(random)) {
                RaidLoot.give(player, RaidLoot.build(grant, this));
                got.add(grant.displayName()
                        + (grant.amount() > 1 ? " ×" + grant.amount() : ""));
            }
            player.sendMessage("§6討伐報酬 §7— " + String.join("§7 / §6", got));
            player.sendMessage(String.format("§7与えたダメージ %.0f（条件 %.0f）",
                    boss.dealtBy(id), boss.rewardThreshold()));
        }
        getServer().broadcastMessage("§7ドロップを " + rewarded.size() + " 名へ配りました");
    }

    /**
     * 複製できない品を守る（§12.4）。
     *
     * <p>鍛治型は<b>複製できないことがドロップの価値の根拠</b>である。バニラの鍛治型は
     * 作業台で複製できるため、付け札の付いた品が材料に入っている作業を成立させない。
     */
    @EventHandler(priority = EventPriority.HIGH)
    public void onPrepareCraft(PrepareItemCraftEvent event) {
        for (ItemStack item : event.getInventory().getMatrix()) {
            String id = RaidLoot.idOf(item, this);
            if (id != null && !RaidDrop.copyable(id)) {
                event.getInventory().setResult(null);
                return;
            }
        }
    }

    /**
     * 出した個体を預かる。開催の進行（{@link RaidHost}）から呼ぶ。
     *
     * <p>被弾の処理と片付けは {@code active} を見て回すため、どこで出した個体も
     * ここへ入れる必要がある。
     */
    void adopt(RaidBoss boss) {
        active.add(boss);
    }

    /** 個体を片付ける。討伐以外の終わり方（時間切れ・全滅）で使う。 */
    void retire(RaidBoss boss) {
        boss.despawn();
        active.remove(boss);
    }

    /** 動いている jar の日時。実機の症状と手元の修正を突き合わせるために出す。 */
    private String jarStamp() {
        try {
            return new java.text.SimpleDateFormat("MM/dd HH:mm:ss")
                    .format(new java.util.Date(getFile().lastModified()));
        } catch (RuntimeException failed) {
            return "不明";
        }
    }

    private int despawnAll() {
        int count = active.size() + (calibration.isEmpty() ? 0 : 1);
        active.forEach(RaidBoss::despawn);
        active.clear();
        launchPoints.clear();
        clearCalibration();
        return count;
    }

    private void clearCalibration() {
        calibration.forEach(Entity::remove);
        calibration.clear();
    }

    private static String format(Location at) {
        return String.format("%.1f, %.1f, %.1f", at.getX(), at.getY(), at.getZ());
    }

    /**
     * 体力を元に戻す（検証用の {@code /raid god}）。
     *
     * <p>死ぬ一撃は威力を 0 にし、それ以外は通したうえで次tickに戻す。
     * <b>減らないのは体力だけ</b>であり、ノックバックも演出も個体側の記録もそのまま通る。
     */
    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onPlayerDamage(EntityDamageEvent event) {
        if (!(event.getEntity() instanceof Player player)
                || !unkillable.contains(player.getUniqueId())) {
            return;
        }
        double before = player.getHealth();
        if (event.getFinalDamage() >= before) {
            event.setDamage(0);   // この一撃では死なせない
        }
        // 体力の巻き戻しは次tick。この場で戻すとダメージの適用前になる
        getServer().getScheduler().runTask(this, () -> {
            if (player.isOnline() && !player.isDead() && player.getHealth() < before) {
                player.setHealth(before);
            }
        });
    }

    /** プレイヤーが放った飛び道具の発射地点を覚える。 */
    @EventHandler
    public void onLaunch(ProjectileLaunchEvent event) {
        if (active.isEmpty()) {
            return;
        }
        ProjectileSource source = event.getEntity().getShooter();
        if (source instanceof Player shooter) {
            launchPoints.put(event.getEntity().getUniqueId(), shooter.getLocation().clone());
        }
    }

    /**
     * 地面に着弾した飛び道具の記録を捨てる。外れた矢の分を溜め込まないため。
     *
     * <p><b>実体に当たった場合は消さない。</b>この事象はダメージ事象より先に起きるため、
     * ここで消すと発射地点が失われ、着弾位置（＝個体の近く）で判定してしまう。
     * 外から撃った矢がすべて通ることになる。実体に当たった分はダメージ側で消す。
     */
    @EventHandler
    public void onProjectileHit(ProjectileHitEvent event) {
        if (event.getHitEntity() == null) {
            launchPoints.remove(event.getEntity().getUniqueId());
        }
    }

    /**
     * 部位への攻撃を個体へ伝える。
     *
     * <p>近接は殴った位置、飛び道具は<b>発射地点</b>を「放たれた位置」として渡す。
     */
    @EventHandler
    public void onHit(EntityDamageByEntityEvent event) {
        Entity damager = event.getDamager();
        Player attacker;
        Location origin;
        boolean ranged;

        Material weapon;

        if (damager instanceof Player player) {
            attacker = player;
            origin = player.getLocation();
            ranged = false;
            weapon = player.getInventory().getItemInMainHand().getType();
        } else if (damager instanceof Projectile projectile
                && projectile.getShooter() instanceof Player shooter) {
            attacker = shooter;
            origin = launchPoints.getOrDefault(projectile.getUniqueId(),
                    projectile.getLocation());
            ranged = true;
            // 投げたトライデントは手に残らない。飛んでいる本体で見る
            weapon = damager instanceof Trident ? Material.TRIDENT : Material.AIR;
        } else {
            return;
        }

        for (RaidBoss boss : new ArrayList<>(active)) {
            if (boss.handleHit(event.getEntity().getUniqueId(), attacker, origin, ranged,
                    weapon)) {
                event.setCancelled(true); // ダメージは個体側で処理する
                launchPoints.remove(damager.getUniqueId());
                if (boss.isDead()) {
                    boss.playDefeat();
                    grantDrops(boss);
                    boss.despawn();
                    active.remove(boss);
                    getServer().broadcastMessage("個体を討伐しました");
                }
                return;
            }
        }
    }
}
