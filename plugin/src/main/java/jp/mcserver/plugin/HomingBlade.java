package jp.mcserver.plugin;

import java.util.HashSet;
import java.util.Set;
import java.util.UUID;
import jp.mcserver.core.raid.HomingDart;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.entity.Player;

/**
 * 「空間斬撃」「全域大旋回」で共通に使う、狙った相手へ追尾しながら直進する浮遊剣
 * （`raid_species.md` §2）。第三形態の金のオノもこのクラスで表す——動き方は同じで、
 * 素材・速度・ダメージ・「盾を一時的に使えなくするか」だけが違う。
 *
 * <p>召喚してから {@code startDelayTicks} が明けるまでは静止し、そのあいだ
 * 「軌道を通りうるプレイヤー」——ここでは追う対象として渡された {@link Player}
 * の現在位置——を追尾する。明けたら移動を始め、{@code retargetIntervalTicks} ごとに
 * 狙いを相手の現在位置へ更新し直しながら、{@code speedBlocksPerTick} で直進する
 * （{@link HomingDart}）。{@code trackingDurationTicks} が尽きるか、合計移動距離が
 * {@code maxDistanceBlocks} に達したら止まる。
 *
 * <p><b>一次実装は戦場の中心を軸に旋回する円弧だった。実機で確認したところ、
 * 旋回を始めた時点の位置で軌道を固定してしまうため、動く相手にほとんど当たらなかった。</b>
 * そこで、狙いを更新し続けながら直進するこの方式に直した。
 *
 * <p><b>盾で防がれるか、地面に接触すると、その場で消える</b>（実機で確認して追加）。
 * 一次実装は地形との当たり判定を持たず、地中を飛び抜けることがあった。
 */
final class HomingBlade implements FloatingBlade {

    private static final double HIT_RADIUS = 0.7;

    private final RaidBossBase boss;
    private final BladeDisplay display;
    private final Player tracked;
    private final int startDelayTicks;
    private final double speedBlocksPerTick;
    private final double maxDistanceBlocks;
    private final int trackingDurationTicks;
    private final int retargetIntervalTicks;
    private final double damage;
    private final double knockbackBlocks;
    private final boolean disablesShieldOnGuard;
    private final Set<UUID> struck = new HashSet<>();
    private final Location position;

    private int tick;
    private int movementTick = -1;
    private double traveled;
    private double aimX;
    private double aimY;
    private double aimZ;

    HomingBlade(RaidBossBase boss, Location spawnXZ, Player tracked, double spawnY,
               double speedBlocksPerTick, double maxDistanceBlocks, int startDelayTicks,
               int trackingDurationTicks, int retargetIntervalTicks, double damage,
               double knockbackBlocks, Material material, double visualScale) {
        this(boss, spawnXZ, tracked, spawnY, speedBlocksPerTick, maxDistanceBlocks, startDelayTicks,
                trackingDurationTicks, retargetIntervalTicks, damage, knockbackBlocks, material,
                visualScale, false);
    }

    /** 第三形態の金のオノなど、盾を一時的に使えなくする一本。 */
    HomingBlade(RaidBossBase boss, Location spawnXZ, Player tracked, double spawnY,
               double speedBlocksPerTick, double maxDistanceBlocks, int startDelayTicks,
               int trackingDurationTicks, int retargetIntervalTicks, double damage,
               double knockbackBlocks, Material material, double visualScale,
               boolean disablesShieldOnGuard) {
        this.boss = boss;
        this.tracked = tracked;
        this.speedBlocksPerTick = speedBlocksPerTick;
        this.maxDistanceBlocks = maxDistanceBlocks;
        this.startDelayTicks = startDelayTicks;
        this.trackingDurationTicks = trackingDurationTicks;
        this.retargetIntervalTicks = retargetIntervalTicks;
        this.damage = damage;
        this.knockbackBlocks = knockbackBlocks;
        this.disablesShieldOnGuard = disablesShieldOnGuard;
        this.position = spawnXZ.clone();
        this.position.setY(spawnY);
        this.aimX = position.getX();
        this.aimY = position.getY();
        this.aimZ = position.getZ();
        this.display = new BladeDisplay(position, material, visualScale);
        display.placeFlying(position);
    }

    @Override
    public boolean tick() {
        tick++;
        if (tick <= startDelayTicks) {
            if (isTrackedValid()) {
                Location at = tracked.getLocation();
                position.setX(at.getX());
                position.setZ(at.getZ());
            }
            display.placeFlying(position);
            return false;
        }
        movementTick++;
        if (movementTick % retargetIntervalTicks == 0) {
            reaim();
        }
        double[] next = HomingDart.stepToward(position.getX(), position.getY(), position.getZ(),
                aimX, aimY, aimZ, speedBlocksPerTick);
        traveled += distance(position.getX(), position.getY(), position.getZ(),
                next[0], next[1], next[2]);
        position.setX(next[0]);
        position.setY(next[1]);
        position.setZ(next[2]);
        if (boss.solidAt(position)) {
            return true;
        }
        display.placeFlying(position);
        if (strikeNearby()) {
            return true;
        }
        return movementTick + 1 >= trackingDurationTicks || traveled >= maxDistanceBlocks;
    }

    /** 狙いを相手の現在位置へ更新し直す。相手が居なくなっていれば、直前の狙いのまま直進する。 */
    private void reaim() {
        if (!isTrackedValid()) {
            return;
        }
        Location at = tracked.getLocation();
        aimX = at.getX();
        aimY = at.getY() + 1.0;
        aimZ = at.getZ();
    }

    private boolean isTrackedValid() {
        return tracked != null && tracked.isOnline() && !tracked.isDead();
    }

    /** @return 盾に防がれたか。防がれたら、この後 {@link #tick()} は true を返して消える */
    private boolean strikeNearby() {
        for (Player player : boss.playersInRange(position, HIT_RADIUS)) {
            if (!struck.add(player.getUniqueId())) {
                continue;
            }
            if (boss.independentHit(player, position, damage, knockbackBlocks, 0.15, true,
                    disablesShieldOnGuard)) {
                return true;
            }
        }
        return false;
    }

    private static double distance(double x1, double y1, double z1, double x2, double y2, double z2) {
        double dx = x2 - x1;
        double dy = y2 - y1;
        double dz = z2 - z1;
        return Math.sqrt(dx * dx + dy * dy + dz * dz);
    }

    @Override
    public void despawn() {
        display.despawn();
    }
}
