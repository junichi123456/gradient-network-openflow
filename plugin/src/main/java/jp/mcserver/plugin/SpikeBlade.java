package jp.mcserver.plugin;

import java.util.HashSet;
import java.util.Set;
import java.util.UUID;
import jp.mcserver.core.raid.GroundSpike;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.Particle;
import org.bukkit.entity.Player;

/**
 * 「串刺し」の1本（`raid_species.md` §2、足元ギミック）。狙ったプレイヤーの足元が
 * 発光したあと（{@link GroundSpike#TELEGRAPH_TICKS}）、その中心から刀身が
 * {@link GroundSpike#RISE_TICKS} で一気に生え上がる。柄は表示せず、刀身の高さぶんだけ
 * {@link BladeDisplay} を伸ばす。
 */
final class SpikeBlade implements FloatingBlade {

    private static final double HIT_RADIUS = 0.6;

    /** 全長まで生え切ってから消えるまでの猶予（tick）。 */
    private static final int LINGER_TICKS = 10;

    private final RaidBossBase boss;
    private final Location base;
    private BladeDisplay display;
    private int tick;
    private int ticksAfterFullyRisen;
    private final Set<UUID> struck = new HashSet<>();

    SpikeBlade(RaidBossBase boss, Location feet) {
        this.boss = boss;
        this.base = feet.clone();
        this.base.setY(boss.groundY(feet));
        telegraph();
    }

    private void telegraph() {
        boss.particles(Particle.END_ROD, base.clone().add(0, 0.1, 0), 20, GroundSpike.TELEGRAPH_RADIUS);
        boss.sound("block.beacon.ambient", 1.0f, 0.6f);
    }

    @Override
    public boolean tick() {
        tick++;
        if (tick % 4 == 0 && tick < GroundSpike.TELEGRAPH_TICKS) {
            boss.particles(Particle.END_ROD, base.clone().add(0, 0.1, 0), 6,
                    GroundSpike.TELEGRAPH_RADIUS);
        }
        double height = GroundSpike.risenHeight(tick);
        if (height <= 0) {
            return false;
        }
        if (display == null) {
            display = new BladeDisplay(base, Material.DIAMOND_BLOCK, 0.35, GroundSpike.SWORD_LENGTH);
            boss.sound("item.trident.hit_ground", 1.3f, 0.9f);
        }
        display.placeRising(base, height);
        if (GroundSpike.fullyRisen(tick)) {
            strikeNearby();
            ticksAfterFullyRisen++;
            return ticksAfterFullyRisen > LINGER_TICKS;
        }
        return false;
    }

    private void strikeNearby() {
        for (Player player : boss.playersInRange(base.clone().add(0, GroundSpike.SWORD_LENGTH / 2, 0),
                HIT_RADIUS)) {
            if (!struck.add(player.getUniqueId())) {
                continue;
            }
            boss.independentHit(player, base, GroundSpike.DAMAGE, GroundSpike.KNOCKBACK_BLOCKS,
                    0.3, true);
        }
    }

    @Override
    public void despawn() {
        if (display != null) {
            display.despawn();
        }
    }
}
