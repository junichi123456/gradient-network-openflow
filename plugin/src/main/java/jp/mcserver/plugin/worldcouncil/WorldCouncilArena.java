package jp.mcserver.plugin.worldcouncil;

import java.io.File;
import java.io.IOException;
import java.util.Base64;
import java.util.HashMap;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import java.util.logging.Level;
import org.bukkit.Bukkit;
import org.bukkit.GameMode;
import org.bukkit.Location;
import org.bukkit.World;
import org.bukkit.WorldCreator;
import org.bukkit.attribute.Attribute;
import org.bukkit.configuration.file.YamlConfiguration;
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
 *
 * <p><b>退避はファイルにも書く</b>（{@code worldcouncil/stash/<UUID>.yml}）。メモリにだけ
 * 持つと、開催中の再起動やクラッシュで参加者の持ち物が消える。インベントリを空にするのは
 * ファイルへ書けたあとに限り、書けなければ移送そのものを取りやめる。
 */
public final class WorldCouncilArena {

    /** ワールドのフォルダ名。 */
    static final String WORLD = "worldcouncil";

    private final Plugin plugin;
    private final File stashFolder;
    private final Map<UUID, Stash> stashed = new HashMap<>();

    /** 退避した本来の状態。 */
    private record Stash(String worldName, double x, double y, double z, float yaw, float pitch,
                         GameMode gameMode, ItemStack[] contents, ItemStack[] armor,
                         ItemStack offHand, double health, int foodLevel, float saturation) {}

    public WorldCouncilArena(Plugin plugin) {
        this.plugin = plugin;
        this.stashFolder = new File(plugin.getDataFolder(), "worldcouncil/stash");
    }

    /**
     * 前回までに書いた退避ファイルを読み込む。再起動をまたいで残っている退避は、
     * 持ち主がまだ復帰していないことを意味する。
     *
     * @return 読み込んだ件数
     */
    public int loadPending() {
        File[] files = stashFolder.listFiles((dir, name) -> name.endsWith(".yml"));
        if (files == null) {
            return 0;
        }
        int loaded = 0;
        for (File file : files) {
            String name = file.getName();
            UUID id;
            try {
                id = UUID.fromString(name.substring(0, name.length() - ".yml".length()));
            } catch (IllegalArgumentException e) {
                plugin.getLogger().warning("世界協議: 退避ファイル名が UUID ではない: " + name);
                continue;
            }
            try {
                stashed.put(id, read(file));
                loaded++;
            } catch (RuntimeException e) {
                plugin.getLogger().log(Level.SEVERE,
                        "世界協議: 退避ファイルを読めない（手で確認すること）: " + name, e);
            }
        }
        return loaded;
    }

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

    /** そのプレイヤーの退避が残っているか（＝専用ワールドから復帰していない）。 */
    public boolean isInside(Player player) {
        return stashed.containsKey(player.getUniqueId());
    }

    /** 退避が残っているプレイヤー。オフラインの者も含む。 */
    public Set<UUID> pending() {
        return Set.copyOf(stashed.keySet());
    }

    /** その場所がこの専用ワールドか。 */
    public static boolean isArena(Location at) {
        return at != null && at.getWorld() != null && at.getWorld().getName().equals(WORLD);
    }

    /**
     * 専用ワールドへ移送する（§「専用ワールドへの移送」）。
     *
     * <p>オーバーワールド（国家ワールド）のインベントリ・位置・ゲームモードを退避し、
     * 会場へテレポートしてからインベントリを空にする。体力は満タンへ回復し、
     * 空腹度は満腹に固定する（{@link WorldCouncilGuard} が以後の消費を無効化する）。
     *
     * @return 移送できたか。退避を書けない・テレポートできない場合は何も変えずに false
     */
    public boolean enter(Player player) {
        if (isInside(player)) {
            return true;
        }
        Location origin = player.getLocation();
        Stash stash = new Stash(origin.getWorld().getName(), origin.getX(), origin.getY(),
                origin.getZ(), origin.getYaw(), origin.getPitch(), player.getGameMode(),
                player.getInventory().getContents().clone(),
                player.getInventory().getArmorContents().clone(),
                player.getInventory().getItemInOffHand().clone(),
                player.getHealth(), player.getFoodLevel(), player.getSaturation());
        try {
            write(player.getUniqueId(), stash);
        } catch (IOException e) {
            plugin.getLogger().log(Level.SEVERE,
                    "世界協議: 退避ファイルを書けないため移送を取りやめた: " + player.getName(), e);
            return false;
        }
        if (!player.teleport(entryPoint(world(plugin)))) {
            deleteFile(player.getUniqueId());
            return false;
        }
        stashed.put(player.getUniqueId(), stash);

        player.getInventory().clear();
        player.getInventory().setArmorContents(new ItemStack[4]);
        player.getInventory().setItemInOffHand(null);
        player.setGameMode(GameMode.ADVENTURE);
        player.setHealth(player.getAttribute(Attribute.MAX_HEALTH).getValue());
        player.setFoodLevel(20);
        player.setSaturation(20f);
        player.setInvulnerable(true);
        return true;
    }

    /**
     * 国家ワールドへ復帰させる（§「国家ワールドへの復帰」）。
     *
     * <p>退避しておいた位置・ゲームモード・インベントリを復元し、退避ファイルを消す。
     * 元のワールドが無くなっていればメインワールドのスポーン地点へ戻す。
     *
     * @return 退避が残っていて復元したか
     */
    public boolean exit(Player player) {
        Stash stash = stashed.remove(player.getUniqueId());
        if (stash == null) {
            return false;
        }
        player.setInvulnerable(false);
        player.getInventory().setContents(stash.contents());
        player.getInventory().setArmorContents(stash.armor());
        player.getInventory().setItemInOffHand(stash.offHand());
        player.teleport(origin(stash));
        player.setGameMode(stash.gameMode());
        player.setHealth(Math.min(stash.health(),
                player.getAttribute(Attribute.MAX_HEALTH).getValue()));
        player.setFoodLevel(stash.foodLevel());
        player.setSaturation(stash.saturation());
        deleteFile(player.getUniqueId());
        return true;
    }

    private static Location origin(Stash stash) {
        World world = Bukkit.getWorld(stash.worldName());
        if (world == null) {
            return Bukkit.getWorlds().get(0).getSpawnLocation();
        }
        return new Location(world, stash.x(), stash.y(), stash.z(), stash.yaw(), stash.pitch());
    }

    // ------------------------------------------------------------ 退避ファイル

    private File fileOf(UUID id) {
        return new File(stashFolder, id + ".yml");
    }

    private void write(UUID id, Stash stash) throws IOException {
        YamlConfiguration yaml = new YamlConfiguration();
        yaml.set("world", stash.worldName());
        yaml.set("x", stash.x());
        yaml.set("y", stash.y());
        yaml.set("z", stash.z());
        yaml.set("yaw", stash.yaw());
        yaml.set("pitch", stash.pitch());
        yaml.set("gameMode", stash.gameMode().name());
        yaml.set("contents", encode(stash.contents()));
        yaml.set("armor", encode(stash.armor()));
        yaml.set("offHand", encode(new ItemStack[] {stash.offHand()}));
        yaml.set("health", stash.health());
        yaml.set("foodLevel", stash.foodLevel());
        yaml.set("saturation", stash.saturation());
        if (!stashFolder.exists() && !stashFolder.mkdirs()) {
            throw new IOException("フォルダを作成できない: " + stashFolder);
        }
        yaml.save(fileOf(id));
    }

    private static Stash read(File file) {
        YamlConfiguration yaml = YamlConfiguration.loadConfiguration(file);
        String world = yaml.getString("world");
        String gameMode = yaml.getString("gameMode");
        if (world == null || gameMode == null) {
            throw new IllegalStateException("必須の項目が無い");
        }
        ItemStack[] offHand = decode(yaml.getString("offHand"));
        return new Stash(world, yaml.getDouble("x"), yaml.getDouble("y"), yaml.getDouble("z"),
                (float) yaml.getDouble("yaw"), (float) yaml.getDouble("pitch"),
                GameMode.valueOf(gameMode),
                decode(yaml.getString("contents")), decode(yaml.getString("armor")),
                offHand.length > 0 ? offHand[0] : null,
                yaml.getDouble("health", 20), yaml.getInt("foodLevel", 20),
                (float) yaml.getDouble("saturation", 5));
    }

    private void deleteFile(UUID id) {
        File file = fileOf(id);
        if (file.exists() && !file.delete()) {
            plugin.getLogger().warning("世界協議: 退避ファイルを消せない: " + file.getName());
        }
    }

    private static String encode(ItemStack[] items) {
        return Base64.getEncoder().encodeToString(ItemStack.serializeItemsAsBytes(items));
    }

    private static ItemStack[] decode(String encoded) {
        if (encoded == null) {
            throw new IllegalStateException("持ち物の項目が無い");
        }
        return ItemStack.deserializeItemsFromBytes(Base64.getDecoder().decode(encoded));
    }
}
