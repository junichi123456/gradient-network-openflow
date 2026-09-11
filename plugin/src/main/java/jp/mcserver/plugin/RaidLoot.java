package jp.mcserver.plugin;

import java.util.ArrayList;
import java.util.List;
import jp.mcserver.core.raid.RaidDrop;
import org.bukkit.Material;
import org.bukkit.NamespacedKey;
import org.bukkit.entity.Player;
import org.bukkit.inventory.ItemStack;
import org.bukkit.inventory.meta.ItemMeta;
import org.bukkit.persistence.PersistentDataType;
import org.bukkit.plugin.Plugin;

/**
 * ドロップ品を実際の品に組み立てる（§12.4）。
 *
 * <p><b>品の正体は付け札（PersistentDataContainer）で持つ。</b>見た目に使うバニラの品を
 * 差し替えても、複製の禁止や配布の処理は書き換えずに済む。
 *
 * <p><b>いまは見た目にバニラの品を借りている。</b>本物の旗模様と鍛治型を足すには
 * データパック（{@code banner_pattern} / {@code trim_pattern} の登録）が必要で、
 * リソースパックだけでは作れない。付け札で正体を持たせてあるので、
 * データパックを用意したときは{@link #base}の1行を変えるだけで移れる。
 */
final class RaidLoot {

    private RaidLoot() {
    }

    /** 付け札の名前。値はドロップ品の識別子である。 */
    static final String TAG = "raid_drop";

    static NamespacedKey key(Plugin plugin) {
        return new NamespacedKey(plugin, TAG);
    }

    /**
     * 見た目に借りるバニラの品。
     *
     * <p>版のあいだで消えない品だけを選んである。新しい版で足された品を使うと、
     * 手元のサーバーの版によってはビルドが通らない。
     */
    private static Material base(String id) {
        return switch (id) {
            case "banner_pattern" -> Material.SKULL_BANNER_PATTERN;
            case "smithing_template" -> Material.SILENCE_ARMOR_TRIM_SMITHING_TEMPLATE;
            case "solo_permit" -> Material.PAPER;
            // 「通常と同等」なので、性能はバニラの馬鎧そのままである
            case "horse_armor" -> Material.DIAMOND_HORSE_ARMOR;
            // 個体の頭は骨である。装飾トロフィーとして骨の頭を借りる
            case "trophy_head" -> Material.WITHER_SKELETON_SKULL;
            default -> Material.PAPER;
        };
    }

    /** 品の説明。性能を持たないことが分かるようにする。 */
    private static List<String> lore(String id) {
        return switch (id) {
            case "banner_pattern" -> List.of("§7騎士型の討伐の証", "§8旗に織り込める");
            case "smithing_template" -> List.of("§7騎士型の討伐の証",
                    "§c複製できない", "§84部位を揃えるには4度討たねばならない");
            case "solo_permit" -> List.of("§7騎士型への単身討伐に挑める",
                    "§8開催期間の外でも使える／ソロ限定");
            case "horse_armor" -> List.of("§7騎士型の討伐の証", "§8性能は通常の馬鎧と同等");
            case "trophy_head" -> List.of("§7騎士型の討伐の証", "§8装飾。性能は持たない");
            default -> List.of("§7騎士型の討伐の証");
        };
    }

    /** 1つぶんの品を組み立てる。 */
    static ItemStack build(RaidDrop.Grant grant, Plugin plugin) {
        ItemStack stack = new ItemStack(base(grant.id()), grant.amount());
        ItemMeta meta = stack.getItemMeta();
        if (meta == null) {
            return stack;
        }
        meta.setDisplayName("§6" + grant.displayName());
        meta.setLore(new ArrayList<>(lore(grant.id())));
        meta.getPersistentDataContainer()
                .set(key(plugin), PersistentDataType.STRING, grant.id());
        stack.setItemMeta(meta);
        return stack;
    }

    /** その品がドロップ品なら識別子を返す。違えば null。 */
    static String idOf(ItemStack stack, Plugin plugin) {
        if (stack == null || !stack.hasItemMeta()) {
            return null;
        }
        ItemMeta meta = stack.getItemMeta();
        if (meta == null) {
            return null;
        }
        return meta.getPersistentDataContainer()
                .get(key(plugin), PersistentDataType.STRING);
    }

    /**
     * 渡す。<b>持ち物が満杯でも落とす</b>。
     *
     * <p>確定贈与であるため、受け取れずに消えることがあってはならない。
     */
    static void give(Player player, ItemStack stack) {
        player.getInventory().addItem(stack).values()
                .forEach(left -> player.getWorld().dropItemNaturally(
                        player.getLocation(), left));
    }
}
