package jp.mcserver.plugin.worldcouncil;

import java.util.HashMap;
import java.util.Map;
import java.util.UUID;
import org.bukkit.GameMode;
import org.bukkit.Location;
import org.bukkit.World;
import org.bukkit.WorldCreator;
import org.bukkit.entity.Player;
import org.bukkit.inventory.ItemStack;
import org.bukkit.plugin.Plugin;

/**
 * 「世界協議」専用ワールド（`world_council_spec.md`）の会場管理。
 *
 * <p>BLOCK CONQUEST（アーティファクト仕様）のボードゲーム本体（盤面・カード・進行）は
 * このセッションでは対象外——今回作るのは「国家ワールドとこの専用ワールドを行き来する」
 * 統合レイヤーのみ（ユーザーへ確認して決定）。盤面の実体（データパック）が用意されたら、
 * {@link #entryPoint} は draft で決まる開始マスへの一次的な降下点として使われる。
 *
 * <p><b>インベントリの退避と復元</b>: 入場時に本来のインベントリ・位置・ゲームモードを
 * {@link #enter} が記憶し、退場時に {@link #exit} で復元する。専用ワールドの中では
 * 実カードゲーム側（ホットバーを手札等に使う想定。BLOCK CONQUEST §15.3）が自由に
 * ホットバーを使えるよう、入場時にインベントリを空にする。
 */
public final class WorldCouncilArena {

    /** ワールドのフォルダ名。 */
    static final String WORLD = "worldcouncil";

    private final Map<UUID, Stash> stashed = new HashMap<>();

    /** 退避した本来の状態。 */
    private record Stash(Location origin, GameMode gameMode,
                         ItemStack[] contents, ItemStack[] armor, ItemStack offHand,
                         double health, int foodLevel, float saturation) {}

    /** 会場のワールド。無ければ作る。 */
    public static World world(Plugin plugin) {
        World loaded = plugin.getServer().getWorld(WORLD);
        if (loaded == null) {
            loaded = plugin.getServer().createWorld(new WorldCreator(WORLD));
        }
        return loaded;
    }

    /**
     * 参加者を降ろす点。<b>盤面本体（BLOCK CONQUESTのデータパック）は未実装のため、
     * 現状はワールドスポーン地点を使う。</b>盤面が用意され次第、draft の開始マスへの
     * 降下に置き換わる想定。
     */
    public static Location entryPoint(World world) {
        return world.getSpawnLocation();
    }

    /** そのプレイヤーが現在この専用ワールドに滞在中か。 */
    public boolean isInside(Player player) {
        return stashed.containsKey(player.getUniqueId());
    }

    /** その場所がこの専用ワールドか。 */
    public static boolean isArena(Location at) {
        return at != null && at.getWorld() != null && at.getWorld().getName().equals(WORLD);
    }

    /**
     * 専用ワールドへ移送する（§「専用ワールドへの移送」）。
     *
     * <p>オーバーワールド（国家ワールド）のインベントリ・位置・ゲームモードを退避し、
     * インベントリを空にしてから会場へテレポートする。体力は満タンへ回復し、
     * 空腹度は満腹に固定する（{@link WorldCouncilGuard} が以後の消費を無効化する）。
     */
    public void enter(Plugin plugin, Player player) {
        if (isInside(player)) {
            return;
        }
        World world = world(plugin);
        stashed.put(player.getUniqueId(), new Stash(
                player.getLocation().clone(), player.getGameMode(),
                player.getInventory().getContents().clone(),
                player.getInventory().getArmorContents().clone(),
                player.getInventory().getItemInOffHand().clone(),
                player.getHealth(), player.getFoodLevel(), player.getSaturation()));

        player.getInventory().clear();
        player.getInventory().setArmorContents(new ItemStack[4]);
        player.getInventory().setItemInOffHand(null);

        player.teleport(entryPoint(world));
        player.setGameMode(GameMode.ADVENTURE);
        player.setHealth(player.getAttribute(org.bukkit.attribute.Attribute.MAX_HEALTH).getValue());
        player.setFoodLevel(20);
        player.setSaturation(20f);
        player.setInvulnerable(true);
    }

    /**
     * 国家ワールドへ復帰させる（§「国家ワールドへの復帰」）。
     *
     * <p>退避しておいた位置・ゲームモード・インベントリを復元する。
     */
    public void exit(Player player) {
        Stash stash = stashed.remove(player.getUniqueId());
        if (stash == null) {
            return;
        }
        player.setInvulnerable(false);
        player.getInventory().setContents(stash.contents());
        player.getInventory().setArmorContents(stash.armor());
        player.getInventory().setItemInOffHand(stash.offHand());
        player.teleport(stash.origin());
        player.setGameMode(stash.gameMode());
        player.setHealth(Math.min(stash.health(),
                player.getAttribute(org.bukkit.attribute.Attribute.MAX_HEALTH).getValue()));
        player.setFoodLevel(stash.foodLevel());
        player.setSaturation(stash.saturation());
    }
}
