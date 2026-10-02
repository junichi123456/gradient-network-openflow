package jp.mcserver.plugin;

import java.util.ArrayList;
import java.util.List;
import java.util.Random;
import java.util.logging.Logger;
import jp.mcserver.core.VillagerTrades;
import org.bukkit.Material;
import org.bukkit.NamespacedKey;
import org.bukkit.Registry;
import org.bukkit.enchantments.Enchantment;
import org.bukkit.entity.AbstractVillager;
import org.bukkit.entity.Villager;
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
import org.bukkit.inventory.meta.PotionMeta;
import org.bukkit.potion.PotionType;

/**
 * 村人の取引テーブルを §3.2 に合わせる。
 *
 * <p>エンチャント済みの品を渡す枠を外し、代替表（{@link VillagerTrades#SUBSTITUTES}）に
 * 定めた枠だけレベル1のエンチャント本または効能付きの矢に差し替える。
 *
 * <p>三箇所で押さえる。
 * <ul>
 *   <li><b>取得時</b>（{@link VillagerAcquireTradeEvent}）— 就職・レベルアップで枠が増える
 *       たびに弾く。代替がある枠は同じ位置で差し替えるので、並び順が崩れない</li>
 *   <li><b>右クリック時</b> — 取引画面が開く前に整える。導入前から居る個体や、取得時に
 *       取りこぼした代替はここで揃う</li>
 *   <li><b>取引画面を開いた時</b> — 右クリック以外の経路（他プラグイン等）の最後の砦</li>
 * </ul>
 *
 * <p>代替の追加は<b>何度呼んでも増えない</b>。同じ品を渡す取引が既にあれば置かないため、
 * 右クリックのたびに取引回数が戻ることもない。
 */
public final class VillagerTradeFilter implements Listener {

    private final Logger log;

    public VillagerTradeFilter(Logger log) {
        this.log = log;
    }

    /**
     * 就職・レベルアップで得た枠が禁止品を渡すなら、代替に差し替えるか、枠ごと与えない。
     */
    @EventHandler(ignoreCancelled = true)
    public void onAcquireTrade(VillagerAcquireTradeEvent event) {
        List<VillagerTrades.Substitute> allowed = allowedFor(event.getEntity());
        if (!VillagerTrades.blocked(offerOf(event.getRecipe().getResult()), allowed)) {
            return;
        }
        MerchantRecipe replacement = replacementFor(event.getEntity(), allowed);
        if (replacement == null) {
            event.setCancelled(true);
        } else {
            event.setRecipe(replacement);
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

    // ------------------------------------------------------------------ 整形

    /**
     * 禁止品を渡す枠を取り除き、足りない代替を足す。
     *
     * <p>残す枠は同じ {@link MerchantRecipe} を使い回す。取引回数・需要による値上がり・
     * 治療による割引は枠そのものが持つ状態であり、作り直すと消えるためである。
     *
     * @return 取引表を書き換えたら true
     */
    boolean sanitize(Merchant merchant) {
        List<VillagerTrades.Substitute> allowed = allowedFor(merchant);
        List<MerchantRecipe> recipes = merchant.getRecipes();
        List<MerchantRecipe> kept =
                VillagerTrades.filter(recipes, r -> offerOf(r.getResult()), allowed);
        boolean changed = kept.size() != recipes.size();
        if (merchant instanceof Villager villager) {
            changed |= addMissing(villager, allowed, kept);
        }
        if (!changed) {
            return false;
        }
        merchant.setRecipes(kept);
        return true;
    }

    /** その村人に認めた代替のうち、取引表にまだ無いものを足す。 */
    private boolean addMissing(Villager villager, List<VillagerTrades.Substitute> allowed,
                               List<MerchantRecipe> recipes) {
        boolean added = false;
        for (VillagerTrades.Substitute substitute : allowed) {
            if (contains(recipes, substitute)) {
                continue;
            }
            MerchantRecipe recipe = build(villager, substitute);
            if (recipe != null) {
                recipes.add(recipe);
                added = true;
            }
        }
        return added;
    }

    /** その代替を渡す取引が既にあるか。 */
    private static boolean contains(List<MerchantRecipe> recipes,
                                    VillagerTrades.Substitute substitute) {
        for (MerchantRecipe recipe : recipes) {
            if (VillagerTrades.provides(offerOf(recipe.getResult()), substitute)) {
                return true;
            }
        }
        return false;
    }

    /** この村人のレベルまでに認めた代替。行商人や無職には無い。 */
    private static List<VillagerTrades.Substitute> allowedFor(Merchant merchant) {
        if (!(merchant instanceof Villager villager)) {
            return List.of();
        }
        return VillagerTrades.substitutesUpTo(
                villager.getProfession().getKey().getKey(), villager.getVillagerLevel());
    }

    /**
     * 取得時に差し替えるための代替。まだ渡していないものだけを対象とする
     * （同じレベルに該当枠が2つある場合、2つ目は代替ではなく削除になる）。
     */
    private MerchantRecipe replacementFor(AbstractVillager villager,
                                          List<VillagerTrades.Substitute> allowed) {
        if (!(villager instanceof Villager profession)) {
            return null;
        }
        List<MerchantRecipe> recipes = villager.getRecipes();
        for (VillagerTrades.Substitute substitute : allowed) {
            if (substitute.level() == profession.getVillagerLevel()
                    && !contains(recipes, substitute)) {
                return build(profession, substitute);
            }
        }
        return null;
    }

    // ------------------------------------------------------------------ 生成

    /** 代替の取引を組み立てる。引けない品（版差）なら null を返して枠を消す。 */
    private MerchantRecipe build(Villager villager, VillagerTrades.Substitute substitute) {
        return substitute.book() ? buildBook(villager, substitute) : buildArrows(villager, substitute);
    }

    /** エンチャント本の枠。値段は司書のバニラ式に合わせ、個体ごとに決まる。 */
    private MerchantRecipe buildBook(Villager villager, VillagerTrades.Substitute substitute) {
        Enchantment enchantment =
                Registry.ENCHANTMENT.get(NamespacedKey.minecraft(substitute.enchantment()));
        if (enchantment == null) {
            log.warning("エンチャントを引けませんでした: " + substitute.enchantment());
            return null;
        }
        ItemStack book = new ItemStack(Material.ENCHANTED_BOOK);
        EnchantmentStorageMeta meta = (EnchantmentStorageMeta) book.getItemMeta();
        meta.addStoredEnchant(enchantment, VillagerTrades.SUBSTITUTE_ENCHANTMENT_LEVEL, true);
        book.setItemMeta(meta);

        int price = VillagerTrades.bookPrice(VillagerTrades.SUBSTITUTE_ENCHANTMENT_LEVEL, false,
                seeded(villager, substitute.level()));
        MerchantRecipe recipe = recipe(book, substitute.level());
        recipe.addIngredient(new ItemStack(Material.EMERALD, price));
        recipe.addIngredient(new ItemStack(Material.BOOK));
        return recipe;
    }

    /** 効能付きの矢の枠。矢師の達人枠と同じ構成にする。 */
    private MerchantRecipe buildArrows(Villager villager, VillagerTrades.Substitute substitute) {
        Random random = seeded(villager, substitute.level());
        String effect = VillagerTrades.ARROW_EFFECTS.get(
                random.nextInt(VillagerTrades.ARROW_EFFECTS.size()));
        PotionType type = Registry.POTION.get(NamespacedKey.minecraft(effect));
        if (type == null) {
            log.warning("効能を引けませんでした: " + effect);
            return null;
        }
        ItemStack arrows = new ItemStack(Material.TIPPED_ARROW, VillagerTrades.ARROW_RESULT);
        PotionMeta meta = (PotionMeta) arrows.getItemMeta();
        meta.setBasePotionType(type);
        arrows.setItemMeta(meta);

        MerchantRecipe recipe = recipe(arrows, substitute.level());
        recipe.addIngredient(new ItemStack(Material.EMERALD, VillagerTrades.ARROW_EMERALDS));
        recipe.addIngredient(new ItemStack(Material.ARROW, VillagerTrades.ARROW_INGREDIENT));
        return recipe;
    }

    private static MerchantRecipe recipe(ItemStack result, int villagerLevel) {
        return new MerchantRecipe(result, 0, VillagerTrades.SUBSTITUTE_MAX_USES, true,
                VillagerTrades.tradeExperience(villagerLevel),
                (float) VillagerTrades.PRICE_MULTIPLIER);
    }

    /**
     * 個体ごとに固定の乱数。値段と効能が右クリックのたびに変わらないよう、
     * UUID から種を作る。
     */
    private static Random seeded(Villager villager, int salt) {
        return new Random(villager.getUniqueId().getMostSignificantBits()
                ^ (villager.getUniqueId().getLeastSignificantBits() * 31L + salt));
    }

    /** 取引が渡す品を、コア層が判定できる形に落とす。 */
    private static VillagerTrades.Offer offerOf(ItemStack result) {
        if (result == null || result.getType().isAir()) {
            return VillagerTrades.Offer.plain("AIR");
        }
        List<VillagerTrades.Enchant> enchantments = new ArrayList<>();
        List<VillagerTrades.Enchant> stored = new ArrayList<>();
        if (result.hasItemMeta()) {
            ItemMeta meta = result.getItemMeta();
            meta.getEnchants().forEach((enchantment, level) ->
                    enchantments.add(new VillagerTrades.Enchant(
                            enchantment.getKey().getKey(), level)));
            if (meta instanceof EnchantmentStorageMeta storage) {
                storage.getStoredEnchants().forEach((enchantment, level) ->
                        stored.add(new VillagerTrades.Enchant(
                                enchantment.getKey().getKey(), level)));
            }
        }
        // Material の名前は名前空間を持たないが、コア層は両方を受け付ける
        return new VillagerTrades.Offer(result.getType().name(), enchantments, stored);
    }
}
