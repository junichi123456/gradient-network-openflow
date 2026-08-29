package jp.mcserver.plugin;

import java.util.ArrayList;
import java.util.List;
import org.bukkit.command.Command;
import org.bukkit.command.CommandSender;
import org.bukkit.entity.Player;
import org.bukkit.event.EventHandler;
import org.bukkit.event.Listener;
import org.bukkit.event.entity.EntityDamageByEntityEvent;
import org.bukkit.plugin.java.JavaPlugin;

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

    @Override
    public void onEnable() {
        getServer().getPluginManager().registerEvents(this, this);
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
        return count;
    }

    /** 部位への攻撃を個体へ伝える。 */
    @EventHandler
    public void onHit(EntityDamageByEntityEvent event) {
        if (!(event.getDamager() instanceof Player attacker)) {
            return;
        }
        for (KnightBoss boss : new ArrayList<>(active)) {
            if (boss.handleHit(event.getEntity().getUniqueId(), attacker)) {
                event.setCancelled(true); // ダメージは個体側で処理する
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
