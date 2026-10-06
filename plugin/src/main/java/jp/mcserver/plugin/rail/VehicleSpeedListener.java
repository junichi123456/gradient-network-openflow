package jp.mcserver.plugin.rail;

import java.util.Set;
import jp.mcserver.core.rail.VehicleSpeed;
import org.bukkit.Material;
import org.bukkit.entity.Boat;
import org.bukkit.entity.Minecart;
import org.bukkit.event.EventHandler;
import org.bukkit.event.Listener;
import org.bukkit.event.vehicle.VehicleCreateEvent;
import org.bukkit.event.vehicle.VehicleMoveEvent;
import org.bukkit.util.Vector;

/**
 * 乗り物速度制御（F-05、`rail_infra_spec.md`）。
 */
public final class VehicleSpeedListener implements Listener {

    /** {@code Minecart#setMaxSpeed} は block/tick 単位（1秒 = 20tick）。 */
    private static final double TICKS_PER_SECOND = 20.0;

    /**
     * ボートを減速させる対象ブロック。要件定義書は「氷・薄氷・氷塊・青氷」と書いており、
     * 「氷」→{@code ICE}・「氷塊」→{@code PACKED_ICE}・「青氷」→{@code BLUE_ICE} は確実だが、
     * 「薄氷」は<b>解釈である</b>——バニラの氷系ブロックで残るのは {@code FROSTED_ICE}
     * （フロストウォーカーで張る、時間で溶ける氷）だけであり、これを指すと読んだ。
     * 正式な和名は「霜氷」であって「薄氷」ではないため、違えば直すこと。
     */
    private static final Set<Material> ICE_BLOCKS = Set.of(
            Material.ICE, Material.FROSTED_ICE, Material.PACKED_ICE, Material.BLUE_ICE);

    @EventHandler
    public void onCreate(VehicleCreateEvent event) {
        if (event.getVehicle() instanceof Minecart minecart) {
            minecart.setMaxSpeed(VehicleSpeed.MINECART_MAX_SPEED / TICKS_PER_SECOND);
        }
    }

    @EventHandler
    public void onMove(VehicleMoveEvent event) {
        if (!(event.getVehicle() instanceof Boat boat)) {
            return;
        }
        Material below = boat.getLocation().clone().subtract(0, 1, 0).getBlock().getType();
        if (!ICE_BLOCKS.contains(below)) {
            return;
        }
        double capPerTick = VehicleSpeed.BOAT_ON_ICE_MAX_SPEED / TICKS_PER_SECOND;
        Vector velocity = boat.getVelocity();
        double speed = velocity.length();
        if (speed > capPerTick && speed > 0) {
            boat.setVelocity(velocity.multiply(capPerTick / speed));
        }
    }
}
