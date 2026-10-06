package jp.mcserver.plugin;

import java.util.random.RandomGenerator;
import org.bukkit.Difficulty;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.World;
import org.bukkit.WorldCreator;
import org.bukkit.block.ShulkerBox;
import org.bukkit.entity.Item;
import org.bukkit.entity.Player;
import org.bukkit.entity.ThrownPotion;
import org.bukkit.event.EventHandler;
import org.bukkit.event.EventPriority;
import org.bukkit.event.Listener;
import org.bukkit.event.block.Action;
import org.bukkit.event.block.BlockBreakEvent;
import org.bukkit.event.block.BlockIgniteEvent;
import org.bukkit.event.block.BlockPlaceEvent;
import org.bukkit.event.entity.EntityDamageByEntityEvent;
import org.bukkit.event.entity.EntityPickupItemEvent;
import org.bukkit.event.entity.EntityPlaceEvent;
import org.bukkit.event.entity.EntityResurrectEvent;
import org.bukkit.event.entity.EntityToggleGlideEvent;
import org.bukkit.event.entity.ProjectileLaunchEvent;
import org.bukkit.event.player.PlayerBucketEmptyEvent;
import org.bukkit.event.player.PlayerInteractEvent;
import org.bukkit.event.player.PlayerTeleportEvent;
import org.bukkit.inventory.ItemStack;
import org.bukkit.inventory.meta.BlockStateMeta;
import org.bukkit.inventory.meta.BundleMeta;
import org.bukkit.plugin.Plugin;

/**
 * レイド専用次元（§12.1 の会場）。
 *
 * <p><b>寸法はワールドの実物から読んだ値である。</b>作った側の説明と実物が食い違う箇所は
 * 実物に合わせてある（説明では赤いネザーレンガが半径36から燃えるとあるが、実際に火が
 * 置かれているのは半径35からである）。
 *
 * <table>
 *   <tr><th>場所</th><th>高さ・半径</th><th>中身</th></tr>
 *   <tr><td>床</td><td>y=0 の天面（立つのは y=1）</td><td>安山岩・丸石・赤いネザーレンガ</td></tr>
 *   <tr><td>安全域</td><td>半径 0〜34</td><td>火が無い</td></tr>
 *   <tr><td>炎上域</td><td>半径 35〜100</td><td>y=1 に火。逃げ回る場所ではない</td></tr>
 *   <tr><td>壁</td><td>半径 101、y=1〜20</td><td>バリア</td></tr>
 *   <tr><td>天井</td><td>y=19</td><td>バリア</td></tr>
 * </table>
 *
 * <p><b>戦場（{@code Stage} の半径30）は安全域の内側に収まる。</b>回旋突進も半径30の
 * 円周を回るため、火に触れずに成立する。余裕は4ブロックである。
 *
 * <p>ブロックの設置と破壊は禁じる。作った側の計画どおりで、地形が変わらないことが
 * 「終了後に再生成する」を軽くしている（変わるのは落下物と実体だけになる）。
 *
 * <p>設置に準じるもの（バケツ・ボートやエンドクリスタルの設置・着火）も禁じる。
 * 個体は常に接地している（§12.6）ので、プレイヤー側の空中・瞬間移動（エリトラ・激流・
 * エンダーパール・コーラスフルーツ）も止め、飛び道具は発射そのものを止める
 * （回復用の投げるポーションだけは通す。個体には効かず、味方を助ける用途しかない）。
 *
 * <p><b>不死のトーテムは持ち込めない。</b>レイド次元へ移る瞬間に所持品を調べ、
 * シュルカーボックスやバンドルの中まで含めて見つかれば移動を取り消す（没収はしない）。
 * 何らかの経路で持ち込まれても、次元内では発動させない。
 */
final class RaidArena implements Listener {

    /** ワールドのフォルダ名。<b>level.dat の LevelName（knight）ではなくフォルダ名で引く。</b> */
    static final String WORLD = "raidboss";

    /** 立つ高さ。床の天面は y=0 である。 */
    static final int FLOOR_Y = 1;

    /** 火の無い半径。ここまでが戦える場所である。 */
    static final double SAFE_RADIUS = 34;

    /** 火が置かれている内側の半径。 */
    static final double FIRE_RADIUS = 35;

    /** バリアの壁の半径。 */
    static final double WALL_RADIUS = 101;

    /** バリアの天井の高さ。<b>頭上に使えるのは 19−1=18 ブロックである。</b> */
    static final int CEILING_Y = 19;

    /** 参加者を降ろす範囲の半径（中心から）。 */
    static final double ENTRY_RADIUS = 13;

    /** 参加者を降ろす高さ。 */
    static final int ENTRY_Y = 2;

    /** 個体が落ちてくる高さ（床からの差）。<b>自由落下で地表面に到達する。</b> */
    static final double DROP_HEIGHT = 3;

    /** 参加者を降ろしてから個体が出るまで（tick）。20秒である。 */
    static final int SPAWN_DELAY_TICKS = 20 * 20;

    /** 個体の向き。<b>常に北向きで出す。</b>体の向きは 0 が南なので 180 が北である。 */
    static final double FACING_NORTH = 180;

    private RaidArena() {
    }

    /** 事象の購読用。 */
    static RaidArena guard() {
        return new RaidArena();
    }

    /**
     * 会場のワールド。無ければ null。
     *
     * <p><b>難易度を上げる。</b>配布されたワールドはピースフルで保存されているため、
     * そのままでは自然回復が速すぎて手応えが変わる。
     */
    static World world(Plugin plugin) {
        World loaded = plugin.getServer().getWorld(WORLD);
        if (loaded == null) {
            loaded = plugin.getServer().createWorld(new WorldCreator(WORLD));
        }
        if (loaded != null && loaded.getDifficulty() == Difficulty.PEACEFUL) {
            loaded.setDifficulty(Difficulty.NORMAL);
            plugin.getLogger().info("会場の難易度をノーマルへ上げました（保存値はピースフル）");
        }
        return loaded;
    }

    /** 会場の中心（床の上）。 */
    static Location center(World world) {
        return new Location(world, 0.5, FLOOR_Y, 0.5);
    }

    /** 個体を出す位置。床より {@value #DROP_HEIGHT} ブロック高い中心である。 */
    static Location bossDrop(World world) {
        return new Location(world, 0.5, FLOOR_Y + DROP_HEIGHT, 0.5);
    }

    /**
     * 参加者を降ろす点。中心から半径 {@value #ENTRY_RADIUS} 以内に散らす。
     *
     * <p>1点に重ねると押し出しで弾かれるため、円板上に一様に散らす。
     * 平方根を取るのは、そうしないと中心に偏るためである。
     */
    static Location entryPoint(World world, RandomGenerator random) {
        double angle = random.nextDouble() * Math.PI * 2;
        double distance = ENTRY_RADIUS * Math.sqrt(random.nextDouble());
        Location at = new Location(world,
                Math.cos(angle) * distance + 0.5, ENTRY_Y,
                Math.sin(angle) * distance + 0.5);
        // 中心を向かせる。降りた瞬間に個体が視界に入る
        at.setDirection(center(world).toVector().subtract(at.toVector()));
        return at;
    }

    /** その場所が会場のワールドか。 */
    static boolean isArena(Location at) {
        return at != null && at.getWorld() != null
                && at.getWorld().getName().equals(WORLD);
    }

    /** 中心からの距離。 */
    static double distanceFromCenter(Location at) {
        return Math.hypot(at.getX(), at.getZ());
    }

    /** 火の上に立っているか。案内に使う。 */
    static boolean inFire(Location at) {
        return isArena(at) && distanceFromCenter(at) >= FIRE_RADIUS;
    }

    /**
     * 落下物と余計な実体を片付ける（§12.1 の「終了後に再生成する」）。
     *
     * <p><b>地形は変わらないため、片付けるのは落下物だけで足りる。</b>設置と破壊を
     * 禁じてあり、火の広がりも {@code doFireTick=false} で止まっている。
     * ファイルごと作り直す必要があるのは、地形が変わりうる場合である。
     *
     * @return 片付けた数
     */
    static int sweep(World world) {
        int swept = 0;
        for (Item item : world.getEntitiesByClass(Item.class)) {
            item.remove();
            swept++;
        }
        return swept;
    }

    // ------------------------------------------------------------ 保護

    /**
     * 設置を禁じる。
     *
     * <p>作った側の計画どおりである。地形が変わらないことが、終了後の片付けを
     * 落下物だけに絞れる根拠になっている。
     */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onPlace(BlockPlaceEvent event) {
        if (!isArena(event.getBlock().getLocation())) {
            return;
        }
        event.setCancelled(true);
        event.getPlayer().sendActionBar(net.kyori.adventure.text.Component.text(
                "レイド次元ではブロックを置けません"));
    }

    /** 破壊を禁じる。 */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onBreak(BlockBreakEvent event) {
        if (!isArena(event.getBlock().getLocation())) {
            return;
        }
        event.setCancelled(true);
        event.getPlayer().sendActionBar(net.kyori.adventure.text.Component.text(
                "レイド次元ではブロックを壊せません"));
    }

    /** PvP を禁じる（§0 の全体規定）。 */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onPvp(EntityDamageByEntityEvent event) {
        if (!(event.getEntity() instanceof Player hurt)
                || !(event.getDamager() instanceof Player)) {
            return;
        }
        if (isArena(hurt.getLocation())) {
            event.setCancelled(true);
        }
    }

    // ------------------------------------------------------------ 不死のトーテム

    /** 不死のトーテムを持っているか。容器（シュルカーボックス・バンドル）の中まで見る。 */
    static boolean carriesTotem(Player player) {
        for (ItemStack item : player.getInventory().getContents()) {
            if (containsTotem(item)) {
                return true;
            }
        }
        return containsTotem(player.getItemOnCursor());
    }

    private static boolean containsTotem(ItemStack item) {
        if (item == null || item.getType() == Material.AIR) {
            return false;
        }
        if (item.getType() == Material.TOTEM_OF_UNDYING) {
            return true;
        }
        if (!item.hasItemMeta()) {
            return false;
        }
        if (item.getItemMeta() instanceof BundleMeta bundle) {
            for (ItemStack inner : bundle.getItems()) {
                if (containsTotem(inner)) {
                    return true;
                }
            }
        }
        if (item.getItemMeta() instanceof BlockStateMeta state
                && state.hasBlockState() && state.getBlockState() instanceof ShulkerBox box) {
            for (ItemStack inner : box.getInventory().getContents()) {
                if (containsTotem(inner)) {
                    return true;
                }
            }
        }
        return false;
    }

    /** トーテムを持ったままレイド次元へ移ることを取り消す。入場のすべての経路がここを通る。 */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onEnter(PlayerTeleportEvent event) {
        if (!isArena(event.getTo()) || isArena(event.getFrom())
                || !carriesTotem(event.getPlayer())) {
            return;
        }
        event.setCancelled(true);
        event.getPlayer().sendMessage(
                "§c不死のトーテムはレイド次元へ持ち込めません。預けてから入り直してください");
    }

    /** 持ち込まれていても次元内では発動させない。 */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onResurrect(EntityResurrectEvent event) {
        if (isArena(event.getEntity().getLocation())) {
            event.setCancelled(true);
        }
    }

    /** 次元内でトーテムを拾わせない。 */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onPickup(EntityPickupItemEvent event) {
        if (isArena(event.getEntity().getLocation())
                && containsTotem(event.getItem().getItemStack())) {
            event.setCancelled(true);
        }
    }

    // ------------------------------------------------------------ 設置に準じるもの・移動

    /** 水・溶岩・粉雪を流させない。 */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onBucket(PlayerBucketEmptyEvent event) {
        if (isArena(event.getBlock().getLocation())) {
            event.setCancelled(true);
            event.getPlayer().sendActionBar(net.kyori.adventure.text.Component.text(
                    "レイド次元ではバケツを使えません"));
        }
    }

    /** ボート・トロッコ・防具立て・エンドクリスタルを置かせない。 */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onEntityPlace(EntityPlaceEvent event) {
        if (isArena(event.getEntity().getLocation())) {
            event.setCancelled(true);
        }
    }

    /** プレイヤーによる着火を止める。炎上域の火は会場の一部であり、ここでは触れない。 */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onIgnite(BlockIgniteEvent event) {
        if (event.getPlayer() != null && isArena(event.getBlock().getLocation())) {
            event.setCancelled(true);
        }
    }

    /** エンダーパール・コーラスフルーツによる瞬間移動を止める。 */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onWarp(PlayerTeleportEvent event) {
        if (!isArena(event.getFrom())) {
            return;
        }
        switch (event.getCause()) {
            // コーラスフルーツは CONSUMABLE_EFFECT で届く（CHORUS_FRUIT は旧名の別名）
            case ENDER_PEARL, CONSUMABLE_EFFECT -> event.setCancelled(true);
            default -> { }
        }
    }

    /** 飛び道具を撃たせない。回復用の投げるポーションだけは通す。 */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onLaunch(ProjectileLaunchEvent event) {
        if (!(event.getEntity().getShooter() instanceof Player shooter)
                || !isArena(shooter.getLocation())
                || event.getEntity() instanceof ThrownPotion) {
            return;
        }
        event.setCancelled(true);
        shooter.sendActionBar(net.kyori.adventure.text.Component.text(
                "レイド次元では飛び道具を使えません"));
    }

    /** トライデントの構え（激流を含む）を止める。投げる側は {@link #onLaunch} が止める。 */
    @EventHandler(priority = EventPriority.HIGH)
    public void onTrident(PlayerInteractEvent event) {
        if ((event.getAction() == Action.RIGHT_CLICK_AIR
                        || event.getAction() == Action.RIGHT_CLICK_BLOCK)
                && event.getItem() != null && event.getItem().getType() == Material.TRIDENT
                && isArena(event.getPlayer().getLocation())) {
            event.setUseItemInHand(org.bukkit.event.Event.Result.DENY);
        }
    }

    /** 滑空させない。 */
    @EventHandler(priority = EventPriority.HIGH, ignoreCancelled = true)
    public void onGlide(EntityToggleGlideEvent event) {
        if (event.isGliding() && isArena(event.getEntity().getLocation())) {
            event.setCancelled(true);
        }
    }
}
