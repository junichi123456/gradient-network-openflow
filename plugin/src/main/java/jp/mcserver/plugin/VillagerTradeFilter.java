package jp.mcserver.plugin;

import java.util.List;
import jp.mcserver.core.VillagerTrades;
import org.bukkit.entity.AbstractVillager;
import org.bukkit.event.EventHandler;
import org.bukkit.event.EventPriority;
import org.bukkit.event.Listener;
import org.bukkit.event.entity.VillagerAcquireTradeEvent;
import org.bukkit.event.inventory.InventoryOpenEvent;
import org.bukkit.event.player.PlayerInteractEntityEvent;
import org.bukkit.inventory.ItemStack;
import org.bukkit.inventory.Merchant;
import org.bukkit.inventory.MerchantInventory;
import org.bukkit.inventory.MerchantRecipe;
import org.bukkit.inventory.meta.EnchantmentStorageMeta;
import org.bukkit.inventory.meta.ItemMeta;

/**
 * 村人の取引テーブルからエンチャント本とエンチャント済みの品を外す（§3.2）。
 *
 * <p>三箇所で押さえる。
 * <ul>
 *   <li><b>取得時</b>（{@link VillagerAcquireTradeEvent}）— 就職・レベルアップで枠が増える
 *       たびに弾く。ここが本体で、以降の二つは取りこぼしの受け皿である</li>
 *   <li><b>右クリック時</b> — 導入前から居る個体や、コマンドで取引を与えられた個体を、
 *       取引画面が開く前に整える</li>
 *   <li><b>取引画面を開いた時</b> — 右クリック以外の経路（他プラグイン等）の最後の砦</li>
 * </ul>
 *
 * <p>行商人も {@link AbstractVillager} なので同じ扱いになる。
 */
public final class VillagerTradeFilter implements Listener {

    /** 就職・レベルアップで得た枠が禁止品を渡すなら、枠ごと与えない。 */
    @EventHandler(ignoreCancelled = true)
    public void onAcquireTrade(VillagerAcquireTradeEvent event) {
        if (VillagerTrades.blocked(offerOf(event.getRecipe().getResult()))) {
            event.setCancelled(true);
        }
    }

    /** 取引画面が開く前に整える。 */
    @EventHandler(priority = EventPriority.LOWEST, ignoreCancelled = true)
    public void onInteract(PlayerInteractEntityEvent event) {
        if (event.getRightClicked() instanceof AbstractVillager villager) {
            sanitize(villager);
        }
    }

    /** 右クリック以外の経路で開かれた場合の受け皿。 */
    @EventHandler(priority = EventPriority.LOWEST, ignoreCancelled = true)
    public void onOpen(InventoryOpenEvent event) {
        if (event.getInventory() instanceof MerchantInventory merchantInventory) {
            sanitize(merchantInventory.getMerchant());
        }
    }

    /**
     * 禁止品を渡す枠を取り除く。
     *
     * <p>残す枠は同じ {@link MerchantRecipe} を使い回す。取引回数・需要による値上がり・
     * 治療による割引は枠そのものが持つ状態であり、作り直すと消えるためである。
     *
     * @return 取り除いた枠があれば true
     */
    static boolean sanitize(Merchant merchant) {
        List<MerchantRecipe> recipes = merchant.getRecipes();
        List<MerchantRecipe> kept = VillagerTrades.filter(recipes, r -> offerOf(r.getResult()));
        if (kept.size() == recipes.size()) {
            return false;
        }
        merchant.setRecipes(kept);
        return true;
    }

    /** 取引が渡す品を、コア層が判定できる形に落とす。 */
    private static VillagerTrades.Offer offerOf(ItemStack result) {
        if (result == null || result.getType().isAir()) {
            return VillagerTrades.Offer.plain("AIR");
        }
        int enchantments = 0;
        int stored = 0;
        if (result.hasItemMeta()) {
            ItemMeta meta = result.getItemMeta();
            enchantments = meta.getEnchants().size();
            if (meta instanceof EnchantmentStorageMeta storage) {
                stored = storage.getStoredEnchants().size();
            }
        }
        // Material の名前は名前空間を持たないが、コア層は両方を受け付ける
        return new VillagerTrades.Offer(result.getType().name(), enchantments, stored);
    }
}
