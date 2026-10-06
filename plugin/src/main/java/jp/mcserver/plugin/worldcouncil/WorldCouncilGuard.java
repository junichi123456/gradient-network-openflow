package jp.mcserver.plugin.worldcouncil;

import org.bukkit.entity.Player;
import org.bukkit.event.EventHandler;
import org.bukkit.event.EventPriority;
import org.bukkit.event.Listener;
import org.bukkit.event.block.BlockBreakEvent;
import org.bukkit.event.block.BlockPlaceEvent;
import org.bukkit.event.entity.EntityDamageEvent;
import org.bukkit.event.entity.FoodLevelChangeEvent;
import org.bukkit.event.player.PlayerDropItemEvent;

/**
 * 「世界協議」専用ワールドの安全策（`world_council_spec.md`）。
 *
 * <p>ユーザーの決定（全面禁止＋完全無効化）どおり、想定される方法（BLOCK CONQUEST の
 * カード・進行コマンドを通した操作。今回は未実装）以外でのブロック・エンティティ・
 * アイテムの操作を禁じ、体力・空腹度の消費を無効化する。
 *
 * <p>体力の無効化は {@link WorldCouncilArena#enter} の {@code setInvulnerable(true)} が
 * 一次防御であり、{@link #onDamage} はその上に重ねる二重の保険である
 * （{@code EntityDamageEvent} を直接キャンセルすることで、無敵状態を経由しない
 * 特殊なダメージ経路も塞ぐ）。
 */
public final class WorldCouncilGuard implements Listener {

    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onBreak(BlockBreakEvent event) {
        if (!WorldCouncilArena.isArena(event.getBlock().getLocation())) {
            return;
        }
        event.setCancelled(true);
    }

    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onPlace(BlockPlaceEvent event) {
        if (!WorldCouncilArena.isArena(event.getBlock().getLocation())) {
            return;
        }
        event.setCancelled(true);
    }

    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onDrop(PlayerDropItemEvent event) {
        if (!WorldCouncilArena.isArena(event.getPlayer().getLocation())) {
            return;
        }
        event.setCancelled(true);
    }

    /** 体力の消費をしない（§「専用ワールドの規則」）。被ダメージそのものを無効化する。 */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onDamage(EntityDamageEvent event) {
        if (!(event.getEntity() instanceof Player player)) {
            return;
        }
        if (!WorldCouncilArena.isArena(player.getLocation())) {
            return;
        }
        event.setCancelled(true);
    }

    /** 空腹度の消費をしない（§「専用ワールドの規則」）。 */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onFoodChange(FoodLevelChangeEvent event) {
        if (!(event.getEntity() instanceof Player player)) {
            return;
        }
        if (!WorldCouncilArena.isArena(player.getLocation())) {
            return;
        }
        event.setCancelled(true);
    }
}
