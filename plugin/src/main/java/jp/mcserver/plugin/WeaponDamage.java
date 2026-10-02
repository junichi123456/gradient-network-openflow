package jp.mcserver.plugin;

import java.util.Map;
import org.bukkit.Material;
import org.bukkit.enchantments.Enchantment;
import org.bukkit.entity.Player;
import org.bukkit.inventory.ItemStack;

/**
 * プレイヤーの攻撃力を自前で求める。
 *
 * <p>当たり判定に使う {@link org.bukkit.entity.Interaction} は生き物ではないため、
 * Minecraft は武器のダメージ計算を通さない。ダメージイベントが運んでくる値は固定であり、
 * <b>ネザライトの剣で殴っても 1 になる</b>。したがって武器・エンチャント・攻撃クールダウンから
 * こちら側で組み立てる。
 *
 * <p>再現するのはバニラの近接ダメージのうち、レイドで効いてくる要素に絞る。
 * 武器の基礎値、ダメージ増加、クールダウンによる減衰、落下中のクリティカルである。
 */
final class WeaponDamage {

    private WeaponDamage() {}

    /** 素手のダメージ。 */
    private static final double FIST = 1.0;

    /**
     * 受け付けない武器（§12.6）。
     *
     * <p>トライデントは<b>投げて回収できる遠距離武器</b>であり、忠誠を付ければ
     * 距離を取ったまま削り続けられる。メイスは落下の高さがそのまま威力になり、
     * 足場を組むだけで設計した体力を無視できる。どちらも噛み合わないため通さない。
     */
    private static final java.util.Set<Material> REJECTED =
            java.util.Set.of(Material.TRIDENT, Material.MACE);

    /** その武器による攻撃を通さないか。 */
    static boolean rejected(Material weapon) {
        return weapon != null && REJECTED.contains(weapon);
    }

    /**
     * その攻撃で入るべきダメージ量。
     */
    static double of(Player attacker) {
        ItemStack weapon = attacker.getInventory().getItemInMainHand();
        double damage = base(weapon.getType()) + sharpness(weapon);

        // クールダウンによる減衰。連打すると威力が落ちる（バニラと同じ式）
        float cooled = attacker.getAttackCooldown();
        damage *= 0.2 + cooled * cooled * 0.8;

        // 落下中の一撃はクリティカル
        if (cooled > 0.9f && attacker.getFallDistance() > 0 && !attacker.isOnGround()) {
            damage *= 1.5;
        }
        return Math.max(0.5, damage);
    }

    /** 武器の基礎攻撃力（プレイヤーの素の1を含む合計）。 */
    private static double base(Material material) {
        return switch (material) {
            case NETHERITE_AXE -> 10.0;
            case DIAMOND_AXE, IRON_AXE, STONE_AXE -> 9.0;
            case TRIDENT -> 9.0;
            case NETHERITE_SWORD -> 8.0;
            case DIAMOND_SWORD -> 7.0;
            case WOODEN_AXE, GOLDEN_AXE -> 7.0;
            case IRON_SWORD -> 6.0;
            case MACE -> 6.0;
            case NETHERITE_SHOVEL -> 6.5;
            case NETHERITE_PICKAXE -> 6.0;
            case STONE_SWORD -> 5.0;
            case DIAMOND_SHOVEL -> 5.5;
            case DIAMOND_PICKAXE -> 5.0;
            case WOODEN_SWORD, GOLDEN_SWORD -> 4.0;
            case IRON_SHOVEL -> 4.5;
            case IRON_PICKAXE -> 4.0;
            case STONE_SHOVEL -> 3.5;
            case STONE_PICKAXE -> 3.0;
            case WOODEN_SHOVEL, GOLDEN_SHOVEL -> 2.5;
            case WOODEN_PICKAXE, GOLDEN_PICKAXE -> 2.0;
            default -> FIST;
        };
    }

    /**
     * ダメージ増加の加算分。
     *
     * <p>列挙の定数名は版によって変わるため、名前空間キーで引く。
     */
    private static double sharpness(ItemStack weapon) {
        if (weapon == null || weapon.getType() == Material.AIR) {
            return 0;
        }
        for (Map.Entry<Enchantment, Integer> entry : weapon.getEnchantments().entrySet()) {
            if ("sharpness".equals(entry.getKey().getKey().getKey())) {
                return 0.5 * entry.getValue() + 0.5;
            }
        }
        return 0;
    }
}
