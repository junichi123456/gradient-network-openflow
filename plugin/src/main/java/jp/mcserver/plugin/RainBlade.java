package jp.mcserver.plugin;

import java.util.HashSet;
import java.util.Set;
import java.util.UUID;
import jp.mcserver.core.raid.SwordRain;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.entity.Player;
import org.joml.Vector3f;

/**
 * 「降り注ぐ刃」の1本（`raid_species.md` §2）。個体の召喚位置の足元Yから
 * {@link SwordRain#SPAWN_Y_OFFSET} だけ上から自由落下し、地面に刺さると0.5ブロック
 * 埋まって衝撃波を残す。落下しているあいだ、剣本体に触れたプレイヤーへも一度だけ当たる。
 */
final class RainBlade implements FloatingBlade {

    private static final Vector3f DOWN = new Vector3f(0, -1, 0);

    /** 剣本体の当たり判定に使う、進行方向まわりの半径（ブロック）。 */
    private static final double HIT_RADIUS = 0.6;

    /** 着地後、衝撃波の余韻を見せてから消えるまでの猶予（tick）。 */
    private static final int LINGER_TICKS = 6;

    private final RaidBossBase boss;
    private final BladeDisplay display;
    private final Location position;
    private double fallSpeed;
    private boolean landed;
    private int ticksSinceLand;
    private final Set<UUID> struck = new HashSet<>();

    RainBlade(RaidBossBase boss, Location spawnXZ) {
        this.boss = boss;
        this.position = spawnXZ.clone();
        this.position.setY(boss.origin().getY() + SwordRain.SPAWN_Y_OFFSET);
        this.display = new BladeDisplay(position, Material.IRON_BLOCK, 0.3, SwordRain.SWORD_LENGTH);
        display.place(position, DOWN);
    }

    @Override
    public boolean tick() {
        if (landed) {
            ticksSinceLand++;
            return ticksSinceLand > LINGER_TICKS;
        }
        double groundY = boss.groundY(position);
        fallSpeed = SwordRain.nextFallSpeed(fallSpeed);
        double nextY = position.getY() - fallSpeed;
        if (nextY <= groundY) {
            position.setY(groundY - SwordRain.EMBED_DEPTH);
            land();
            return false;
        }
        position.setY(nextY);
        display.place(position, DOWN);
        strikeNearby();
        return false;
    }

    private void land() {
        landed = true;
        display.place(position, DOWN);
        boss.shockwaveAt(position.clone().add(0, SwordRain.EMBED_DEPTH, 0),
                SwordRain.SHOCKWAVE_RADIUS, SwordRain.SHOCKWAVE_HEIGHT, SwordRain.SHOCKWAVE_DAMAGE);
    }

    private void strikeNearby() {
        for (Player player : boss.playersInRange(position, HIT_RADIUS)) {
            if (!struck.add(player.getUniqueId())) {
                continue;
            }
            boss.independentHit(player, position, SwordRain.BLADE_DAMAGE,
                    SwordRain.KNOCKBACK_BLOCKS, 0.2, true);
        }
    }

    @Override
    public void despawn() {
        display.despawn();
    }
}
