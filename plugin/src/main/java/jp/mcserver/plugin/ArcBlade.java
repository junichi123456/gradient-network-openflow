package jp.mcserver.plugin;

import java.util.HashSet;
import java.util.Random;
import java.util.Set;
import java.util.UUID;
import jp.mcserver.core.raid.ArcSweep;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.entity.Player;
import org.joml.Vector3f;

/**
 * 「空間斬撃」「全域大旋回」で共通に使う、戦場の中心を軸に弧を描いて旋回する浮遊剣
 * （`raid_species.md` §2）。軌道の幾何は {@code core} の {@link ArcSweep} に置く。
 *
 * <p>召喚してから {@code startDelayTicks} が明けるまでは静止し、そのあいだ
 * 「軌道を通りうるプレイヤー」——ここでは追う対象として渡された {@link Player}
 * の現在位置——を追尾する。明けた瞬間の位置（戦場の中心からの距離）を半径として固定し、
 * 以降はその半径を保ったまま、Y座標だけ直線的に下げながら旋回する
 * （{@code RaidBossBase} の回旋突進と同じ「中心を軸とした円」の考え方）。
 */
final class ArcBlade implements FloatingBlade {

    private static final double HIT_RADIUS = 0.7;

    private final RaidBossBase boss;
    private final BladeDisplay display;
    private final Player tracked;
    private final int startDelayTicks;
    private final double spawnY;
    private final double endY;
    private final double distanceBlocks;
    private final double speedBlocksPerSecond;
    private final double damage;
    private final double knockbackBlocks;
    private final int direction;
    private final Set<UUID> struck = new HashSet<>();
    private final Location position;

    private int tick;
    private boolean started;
    private double centerX;
    private double centerZ;
    private double radius;
    private double startAngle;
    private double sweepTotal;
    private int durationTicks;

    ArcBlade(RaidBossBase boss, Location spawnXZ, Player tracked, double spawnY, double endY,
            double distanceBlocks, double speedBlocksPerSecond, int startDelayTicks,
            double damage, double knockbackBlocks, Material material, double width,
            double length, Random random) {
        this.boss = boss;
        this.tracked = tracked;
        this.spawnY = spawnY;
        this.endY = endY;
        this.distanceBlocks = distanceBlocks;
        this.speedBlocksPerSecond = speedBlocksPerSecond;
        this.startDelayTicks = startDelayTicks;
        this.damage = damage;
        this.knockbackBlocks = knockbackBlocks;
        this.direction = random.nextBoolean() ? 1 : -1;
        this.position = spawnXZ.clone();
        this.position.setY(spawnY);
        this.display = new BladeDisplay(position, material, width, length);
        display.place(position, new Vector3f(0, -1, 0));
    }

    @Override
    public boolean tick() {
        tick++;
        if (!started) {
            if (tracked != null && tracked.isOnline() && !tracked.isDead()) {
                Location at = tracked.getLocation();
                position.setX(at.getX());
                position.setZ(at.getZ());
            }
            display.place(position, new Vector3f(0, -1, 0));
            if (tick < startDelayTicks) {
                return false;
            }
            begin();
            return false;
        }
        int since = tick - startDelayTicks;
        if (since > durationTicks) {
            return true;
        }
        double[] horizontal = ArcSweep.horizontalAt(centerX, centerZ, startAngle, radius,
                direction, sweepTotal, durationTicks, since);
        position.setX(horizontal[0]);
        position.setZ(horizontal[1]);
        position.setY(ArcSweep.yAt(spawnY, endY, durationTicks, since));
        display.place(position, tangentAt(horizontal[0], horizontal[1]));
        strikeNearby();
        return since == durationTicks;
    }

    /** 待機明け。位置から中心までの距離を半径として固定し、旋回の尺・掃過角を求める。 */
    private void begin() {
        started = true;
        var stage = boss.stage();
        centerX = stage.centerX();
        centerZ = stage.centerZ();
        double dx = position.getX() - centerX;
        double dz = position.getZ() - centerZ;
        radius = Math.max(1.0, Math.hypot(dx, dz));
        startAngle = Math.atan2(dz, dx);
        durationTicks = ArcSweep.durationTicks(distanceBlocks, speedBlocksPerSecond);
        sweepTotal = ArcSweep.sweepRadians(radius, distanceBlocks);
    }

    /** 円弧の接線方向（進行方向）。中心から見た角度に、旋回の向きぶんの垂線を立てる。 */
    private Vector3f tangentAt(double x, double z) {
        double angle = Math.atan2(z - centerZ, x - centerX);
        double perpAngle = angle + direction * Math.PI / 2;
        return new Vector3f((float) Math.cos(perpAngle), 0, (float) Math.sin(perpAngle));
    }

    private void strikeNearby() {
        for (Player player : boss.playersInRange(position, HIT_RADIUS)) {
            if (!struck.add(player.getUniqueId())) {
                continue;
            }
            boss.independentHit(player, position, damage, knockbackBlocks, 0.15, true);
        }
    }

    @Override
    public void despawn() {
        display.despawn();
    }
}
