package jp.mcserver.plugin;

import java.util.logging.Logger;
import org.bukkit.Material;
import org.bukkit.block.Block;
import org.bukkit.configuration.file.FileConfiguration;
import org.bukkit.event.Event.Result;
import org.bukkit.event.EventHandler;
import org.bukkit.event.EventPriority;
import org.bukkit.event.Listener;
import org.bukkit.event.block.Action;
import org.bukkit.event.player.PlayerInteractEvent;

/**
 * クッションの座る機能を無効化する（`minecraft_server_spec.md` §1.4）。
 *
 * <p>「クッション」は Minecraft 1.26.3 で追加されたブロックで、対応する
 * {@link Material} 定数名をまだ確認していない（`rail_infra_spec.md` §6 の
 * SULFUR_BRICKS/CINNABAR_BRICKS と同じ事情）。定数を直接書くとビルドできる
 * Paper API のバージョンが確定するまでコンパイルが通らないため、{@code config.yml}
 * の {@code cushion.material-name} を {@link Material#matchMaterial(String)}
 * で解決する——名前が変わっても config.yml を直すだけで対応でき、未解決でも
 * ビルドは常に通る（その場合はこの制限自体が働かず、通常どおり座れてしまう
 * ので起動時に警告する）。
 *
 * <p>座る動作は右クリックでの {@link PlayerInteractEvent} を起点にバニラが
 * 処理すると見て、対象ブロックへの {@code RIGHT_CLICK_BLOCK} をキャンセルする。
 * 設置・破壊は別イベント（{@code BlockPlaceEvent}/{@code BlockBreakEvent}）で
 * 扱われるため、この制限では妨げない。
 */
final class CushionSitRestriction implements Listener {

    private final Material cushion;

    private CushionSitRestriction(Material cushion) {
        this.cushion = cushion;
    }

    static CushionSitRestriction load(FileConfiguration config, Logger logger) {
        String name = config.getString("cushion.material-name", "CUSHION");
        Material material = Material.matchMaterial(name);
        if (material == null) {
            logger.warning("cushion.material-name の \"" + name + "\" はこの Paper の "
                    + "Material に無い——クッションの座る機能を無効化できない"
                    + "（config.yml を実際の Material 名に直すこと）");
        }
        return new CushionSitRestriction(material);
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onInteract(PlayerInteractEvent event) {
        if (event.getAction() != Action.RIGHT_CLICK_BLOCK || cushion == null) {
            return;
        }
        Block clicked = event.getClickedBlock();
        if (clicked != null && clicked.getType() == cushion) {
            event.setCancelled(true);
            event.setUseInteractedBlock(Result.DENY);
        }
    }
}
