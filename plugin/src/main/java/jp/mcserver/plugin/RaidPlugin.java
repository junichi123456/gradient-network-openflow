package jp.mcserver.plugin;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
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
 * </ul>
 */
public final class RaidPlugin extends JavaPlugin implements Listener {

    private final List<KnightBoss> active = new ArrayList<>();

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

    /** 較正用に出した表示エンティティ（`raid_model_spec.md` §7）。 */
    private final List<Entity> calibration = new ArrayList<>();

    @Override
    public void onEnable() {
        getServer().getPluginManager().registerEvents(this, this);
        // 村人の取引テーブル（§3.2）。レイドとは独立だが、常駐の購読はここへ集約する
        getServer().getPluginManager().registerEvents(new VillagerTradeFilter(getLogger()), this);
        getLogger().info("レイド検証プラグインを有効化しました");
    }

    @Override
    public void onDisable() {
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
        switch (action) {
            case "spawn" -> {
                KnightBoss boss = new KnightBoss(this, player.getLocation());
                boss.spawn();
                active.add(boss);
                player.sendMessage("騎士型を召喚しました（参加人数 "
                        + boss.participants() + " / 体力 " + boss.maxHealth() + "）");
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
                player.sendMessage("較正用の立方体を " + format(at) + " に出しました");
                player.sendMessage("§7赤い小さな印がエンティティの位置です。"
                        + "色付きの立方体の§f中心§7に印があれば想定どおりです");
                player.sendMessage("§7マゼンタの角がモデル座標 (0,0,0) です。"
                        + "上=黄緑 下=赤 北=青 南=黄 西=白 東=黒");
                player.sendMessage("§7消すときは /raid despawn");
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

    private int despawnAll() {
        int count = active.size() + (calibration.isEmpty() ? 0 : 1);
        active.forEach(KnightBoss::despawn);
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

        for (KnightBoss boss : new ArrayList<>(active)) {
            if (boss.handleHit(event.getEntity().getUniqueId(), attacker, origin, ranged,
                    weapon)) {
                event.setCancelled(true); // ダメージは個体側で処理する
                launchPoints.remove(damager.getUniqueId());
                if (boss.isDead()) {
                    boss.playDefeat();
                    boss.despawn();
                    active.remove(boss);
                    getServer().broadcastMessage("騎士型を討伐しました");
                }
                return;
            }
        }
    }
}
