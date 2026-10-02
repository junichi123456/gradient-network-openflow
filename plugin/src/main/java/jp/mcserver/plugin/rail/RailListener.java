package jp.mcserver.plugin.rail;

import java.util.Optional;
import java.util.Set;
import jp.mcserver.core.rail.DiplomacyQuota;
import jp.mcserver.core.rail.RailCost;
import jp.mcserver.core.rail.RailType;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.entity.Player;
import org.bukkit.event.EventHandler;
import org.bukkit.event.EventPriority;
import org.bukkit.event.Listener;
import org.bukkit.event.block.BlockBreakEvent;
import org.bukkit.event.block.BlockPlaceEvent;
import org.bukkit.event.entity.EntitySpawnEvent;

/**
 * F-01（レール設置制限・課金）と F-04 後半（認定駅舎内の Mob スポーン全面キャンセル）を
 * Bukkit のイベントへつなぐ（`rail_infra_spec.md`）。
 */
public final class RailListener implements Listener {

    private static final Set<Material> RAIL_MATERIALS = Set.of(
            Material.RAIL, Material.POWERED_RAIL, Material.DETECTOR_RAIL, Material.ACTIVATOR_RAIL);

    private final RailDatabase db;
    private final StationIndex stationIndex;

    public RailListener(RailDatabase db, StationIndex stationIndex) {
        this.db = db;
        this.stationIndex = stationIndex;
    }

    private static RailType typeOf(Material material) {
        return switch (material) {
            case RAIL -> RailType.RAIL;
            case POWERED_RAIL -> RailType.POWERED_RAIL;
            case DETECTOR_RAIL -> RailType.DETECTOR_RAIL;
            case ACTIVATOR_RAIL -> RailType.ACTIVATOR_RAIL;
            default -> null;
        };
    }

    /** F-01: 設置イベント発生 → 条件判定 → 国庫残高確認 → 引き落とし → 記録 → カウント+1。 */
    @EventHandler(priority = EventPriority.HIGH)
    public void onPlace(BlockPlaceEvent event) {
        Material material = event.getBlockPlaced().getType();
        if (!RAIL_MATERIALS.contains(material)) {
            return;
        }
        RailType type = typeOf(material);
        Player player = event.getPlayer();
        Location at = event.getBlockPlaced().getLocation();

        Optional<String> nationOpt = db.nationOfPlayer(player.getUniqueId());
        boolean affiliated = nationOpt.isPresent();
        String nationId = nationOpt.orElse(null);

        int placedThisMonth = affiliated ? db.placedThisMonth(nationId) : 0;
        int monthlyLimit = affiliated
                ? (int) DiplomacyQuota.effectiveLimit(db.allianceCount(nationId),
                        db.suzerainOfVassalCount(nationId))
                : 0;

        RailCost.Check check = RailCost.canPlace(at.getBlockY(), affiliated, placedThisMonth,
                monthlyLimit);
        if (!check.allowed()) {
            event.setCancelled(true);
            player.sendMessage("§c設置できません: " + check.message());
            return;
        }

        boolean outsideTerritory = db.isOutsideTerritory(at.getWorld().getName(),
                at.getBlockX() >> 4, at.getBlockZ() >> 4, nationId);

        RailCost.Charge charge = RailCost.charge(db.balances(nationId), type, placedThisMonth,
                outsideTerritory);
        if (!charge.paid()) {
            event.setCancelled(true);
            player.sendMessage("§c国庫が足りません（必要 " + charge.amount() + "）");
            return;
        }

        db.saveBalances(nationId, charge.after());
        db.incrementPlacedThisMonth(nationId);
        db.insertRail(at.getWorld().getName(), at.getBlockX(), at.getBlockY(), at.getBlockZ(),
                type, nationId, outsideTerritory);
        player.sendMessage("§7設置しました（" + charge.amount() + " を国庫から徴収。今月 "
                + (placedThisMonth + 1) + "/" + monthlyLimit + " 本）");
    }

    /**
     * レールを壊したら記録も消す。<b>返金はしない</b>（要件定義書に解体の規定が無いため、
     * 設置時に一度払った費用は戻さない扱いとした——実装上の判断）。当月の設置カウントも
     * 減らさない（月間上限は「その月に何本置いたか」という消費枠であり、壊しても枠が
     * 戻る仕組みではないと読んだ）。
     */
    @EventHandler(priority = EventPriority.MONITOR, ignoreCancelled = true)
    public void onBreak(BlockBreakEvent event) {
        if (!RAIL_MATERIALS.contains(event.getBlock().getType())) {
            return;
        }
        Location at = event.getBlock().getLocation();
        db.deleteRail(at.getWorld().getName(), at.getBlockX(), at.getBlockY(), at.getBlockZ());
    }

    /**
     * F-04: 認定駅舎の範囲内での Mob スポーンを、明るさ等に関わらず全面キャンセルする。
     *
     * <p><b>サーバー内のあらゆるモブのスポーンのたびに呼ばれるため、SQLite には触れない。</b>
     * {@link #db} ではなく、起動時に読み込んでおいたメモリ上の {@link StationIndex} だけで
     * 判定する（負荷対策の相談で見つかった点。§6）。
     */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onEntitySpawn(EntitySpawnEvent event) {
        Location at = event.getLocation();
        if (stationIndex.contains(at.getWorld().getName(), at.getBlockX(), at.getBlockY(),
                at.getBlockZ())) {
            event.setCancelled(true);
        }
    }
}
