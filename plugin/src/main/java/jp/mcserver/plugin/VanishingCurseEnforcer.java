package jp.mcserver.plugin;

import java.util.LinkedHashMap;
import java.util.Map;
import java.util.logging.Logger;
import jp.mcserver.core.VanishingCurse;
import org.bukkit.enchantments.Enchantment;
import org.bukkit.entity.Item;
import org.bukkit.entity.Player;
import org.bukkit.event.EventHandler;
import org.bukkit.event.EventPriority;
import org.bukkit.event.Listener;
import org.bukkit.event.enchantment.EnchantItemEvent;
import org.bukkit.event.entity.EntityDeathEvent;
import org.bukkit.event.entity.EntityPickupItemEvent;
import org.bukkit.event.inventory.PrepareAnvilEvent;
import org.bukkit.event.player.PlayerFishEvent;
import org.bukkit.event.world.LootGenerateEvent;
import org.bukkit.inventory.ItemStack;
import org.bukkit.inventory.meta.ItemMeta;

/**
 * 『消滅の呪い』の全面付与（§3.1）。
 *
 * <p><b>漏れが1経路でもあれば、そこが蓄積の抜け道になる。</b>経路ごとに捕捉点を置き、
 * 判定は {@link VanishingCurse} に集約する。最後の受け皿として拾得も見る。
 *
 * <table>
 *   <tr><th>経路</th><th>捕捉点</th></tr>
 *   <tr><td>エンチャントテーブル</td><td>{@link EnchantItemEvent}</td></tr>
 *   <tr><td>金床（本＋道具）</td><td>{@link PrepareAnvilEvent}。<b>増えた場合のみ</b></td></tr>
 *   <tr><td>構造物チェスト</td><td>{@link LootGenerateEvent}</td></tr>
 *   <tr><td>モブドロップ</td><td>{@link EntityDeathEvent}</td></tr>
 *   <tr><td>釣り</td><td>{@link PlayerFishEvent}</td></tr>
 *   <tr><td>拾得（受け皿）</td><td>{@link EntityPickupItemEvent}</td></tr>
 * </table>
 *
 * <p>司書村人の本と村人が売る既エンチャント装備は、<b>§3.2 により取引そのものが存在しない</b>
 * （{@link VillagerTradeFilter}）。行商人も同じ扱いである。
 */
final class VanishingCurseEnforcer implements Listener {

    private final Logger logger;

    VanishingCurseEnforcer(Logger logger) {
        this.logger = logger;
    }

    // -------------------------------------------------------------- エンチャントテーブル

    /**
     * エンチャントの結果へ呪いを足す。
     *
     * <p>本は対象外である（§3.1）。本へ足すと「保管された呪い」として道具へ移り、
     * 金床の経路と二重になる。
     */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onEnchant(EnchantItemEvent event) {
        if (isBook(event.getItem())) {
            return;
        }
        if (event.getEnchantsToAdd().containsKey(curse())) {
            return;
        }
        event.getEnchantsToAdd().put(curse(), 1);
    }

    // -------------------------------------------------------------- 金床

    /**
     * 金床の結果へ呪いを足す。<b>エンチャントが増えた場合だけ</b>である。
     *
     * <p>修理と命名では増えないため、そのまま対象外になる（§3.1）。
     */
    @EventHandler(priority = EventPriority.HIGH)
    public void onAnvil(PrepareAnvilEvent event) {
        ItemStack result = event.getResult();
        if (result == null || isBook(result)) {
            return;
        }
        ItemStack base = event.getInventory().getItem(0);
        Map<String, Integer> before = base == null ? Map.of() : enchantments(base);
        if (!VanishingCurse.gained(before, enchantments(result))) {
            return;
        }
        ItemStack cursed = result.clone();
        if (apply(cursed)) {
            event.setResult(cursed);
        }
    }

    // -------------------------------------------------------------- ルートテーブル

    /** 構造物チェストなどの生成物。 */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onLoot(LootGenerateEvent event) {
        event.getLoot().forEach(this::apply);
    }

    /** モブドロップ。 */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onDeath(EntityDeathEvent event) {
        event.getDrops().forEach(this::apply);
    }

    /** 釣り。 */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onFish(PlayerFishEvent event) {
        if (event.getCaught() instanceof Item caught) {
            ItemStack stack = caught.getItemStack();
            if (apply(stack)) {
                caught.setItemStack(stack);
            }
        }
    }

    // -------------------------------------------------------------- 受け皿

    /**
     * 拾得。<b>上のどれでも捕まえられなかったものの受け皿である。</b>
     *
     * <p>導入前から world にあった品、コマンドで出された品、ほかのプラグインが
     * 落とした品はここで捕まる。仕様は「手段を問わず」であるため、
     * 経路を数え上げきれない前提で網を置く。
     */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onPickup(EntityPickupItemEvent event) {
        if (!(event.getEntity() instanceof Player)) {
            return;
        }
        ItemStack stack = event.getItem().getItemStack();
        if (apply(stack)) {
            event.getItem().setItemStack(stack);
            logger.fine("拾得で『消滅の呪い』を付けた: " + stack.getType());
        }
    }

    // -------------------------------------------------------------- 共通

    /**
     * 必要なら呪いを付ける。
     *
     * @return 付けたか
     */
    private boolean apply(ItemStack stack) {
        if (stack == null || !VanishingCurse.needs(stack.getType().name(), enchantments(stack))) {
            return false;
        }
        ItemMeta meta = stack.getItemMeta();
        if (meta == null) {
            return false;
        }
        meta.addEnchant(curse(), 1, true);
        stack.setItemMeta(meta);
        return true;
    }

    /** 付いているエンチャント。名前 → 水準。コア層の判定に渡す形に揃える。 */
    private static Map<String, Integer> enchantments(ItemStack stack) {
        Map<String, Integer> levels = new LinkedHashMap<>();
        stack.getEnchantments().forEach(
                (enchantment, level) -> levels.put(name(enchantment), level));
        return levels;
    }

    private static boolean isBook(ItemStack stack) {
        return stack != null
                && stack.getType().name().equals(VanishingCurse.ENCHANTED_BOOK);
    }

    /**
     * 呪いのエンチャント。
     *
     * <p>定数で引く。1.20.5 の改名（{@code DURABILITY} → {@code UNBREAKING} など）では
     * <b>この名前は変わっていない</b>。バニラの識別子 {@code vanishing_curse} と
     * 定数名が一致しているため、改名の対象にならなかった。
     */
    private static Enchantment curse() {
        return Enchantment.VANISHING_CURSE;
    }

    /** エンチャントの名前を、コア層が使う形（大文字の列挙名ふう）に直す。 */
    private static String name(Enchantment enchantment) {
        return enchantment.getKey().getKey().toUpperCase(java.util.Locale.ROOT);
    }
}
