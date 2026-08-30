package jp.mcserver.plugin;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import org.bukkit.Location;
import org.bukkit.command.Command;
import org.bukkit.command.CommandSender;
import org.bukkit.entity.Entity;
import org.bukkit.entity.Player;
import org.bukkit.entity.Projectile;
import org.bukkit.event.EventHandler;
import org.bukkit.event.Listener;
import org.bukkit.event.entity.EntityDamageByEntityEvent;
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
 * </ul>
 */
public final class RaidPlugin extends JavaPlugin implements Listener {

    private final List<KnightBoss> active = new ArrayList<>();

    /**
     * 飛び道具の発射地点（§12.6）。
     *
     * <p>戦場の外から放たれた攻撃を通さないため、<b>撃った位置</b>を覚えておく。
     * 射手の現在位置で見ると、外から撃って踏み込むだけで通ってしまう。
     */
    private final Map<UUID, Location> launchPoints = new HashMap<>();

    @Override
    public void onEnable() {
        getServer().getPluginManager().registerEvents(this, this);
        // 村人の取引テーブル（§3.2）。レイドとは独立だが、常駐の購読はここへ集約する
        getServer().getPluginManager().registerEvents(new VillagerTradeFilter(), this);
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
        int count = active.size();
        active.forEach(KnightBoss::despawn);
        active.clear();
        launchPoints.clear();
        return count;
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

        if (damager instanceof Player player) {
            attacker = player;
            origin = player.getLocation();
            ranged = false;
        } else if (damager instanceof Projectile projectile
                && projectile.getShooter() instanceof Player shooter) {
            attacker = shooter;
            origin = launchPoints.getOrDefault(projectile.getUniqueId(),
                    projectile.getLocation());
            ranged = true;
        } else {
            return;
        }

        for (KnightBoss boss : new ArrayList<>(active)) {
            if (boss.handleHit(event.getEntity().getUniqueId(), attacker, origin, ranged)) {
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
