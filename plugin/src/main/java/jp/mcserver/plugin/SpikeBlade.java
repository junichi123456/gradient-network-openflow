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
 * 発光したあと（{@code telegraphTicks}）、その中心から刀身が {@link GroundSpike#RISE_TICKS}
 * で一気に生え上がる。柄は表示せず、刀身の高さぶんだけ {@link BladeDisplay} を伸ばす。
 * 発光時間は生成のたびにランダム（{@code jp.mcserver.core.raid.SpecialWaveTiming}、
 * 実機で確認して固定値から直した）。
 *
 * <p><b>盾で防がれると、生え上がる途中でもその場で消える</b>（実機で確認して追加）。
 * 第三形態の金のオノ（{@link jp.mcserver.core.raid.GoldenAxe}）もこのクラスで表す
 * ——動き方は同じで、素材・ダメージ・「盾を一時的に使えなくするか」だけが違う。
 */
final class SpikeBlade implements FloatingBlade {

    private static final double HIT_RADIUS = 0.6;

    /** 全長まで生え切ってから消えるまでの猶予（tick）。 */
    private static final int LINGER_TICKS = 10;

    private final RaidBossBase boss;
    private final Location base;
    private final Material material;
    private final double length;
    private final double damage;
    private final double knockbackBlocks;
    private final boolean disablesShieldOnGuard;
    private final int telegraphTicks;
    private BladeDisplay display;
    private int tick;
    private int ticksAfterFullyRisen;
    private final Set<UUID> struck = new HashSet<>();

    SpikeBlade(RaidBossBase boss, Location feet, int telegraphTicks) {
        this(boss, feet, Material.DIAMOND_SWORD, GroundSpike.SWORD_LENGTH, GroundSpike.DAMAGE,
                GroundSpike.KNOCKBACK_BLOCKS, false, telegraphTicks);
    }

    /** 第三形態の金のオノなど、素材・ダメージ・盾無効化の有無だけが違う同じ動き方の1本。 */
    SpikeBlade(RaidBossBase boss, Location feet, Material material, double length, double damage,
              double knockbackBlocks, boolean disablesShieldOnGuard, int telegraphTicks) {
        this.boss = boss;
        this.base = feet.clone();
        this.base.setY(boss.groundY(feet));
        this.material = material;
        this.length = length;
        this.damage = damage;
        this.knockbackBlocks = knockbackBlocks;
        this.disablesShieldOnGuard = disablesShieldOnGuard;
        this.telegraphTicks = telegraphTicks;
        telegraph();
    }

    private void telegraph() {
        boss.particles(Particle.END_ROD, base.clone().add(0, 0.1, 0), 20, GroundSpike.TELEGRAPH_RADIUS);
        boss.sound("block.beacon.ambient", 1.0f, 0.6f);
    }

    @Override
    public boolean tick() {
        tick++;
        if (tick % 4 == 0 && tick < telegraphTicks) {
            boss.particles(Particle.END_ROD, base.clone().add(0, 0.1, 0), 6,
                    GroundSpike.TELEGRAPH_RADIUS);
        }
        double height = GroundSpike.risenHeight(tick, telegraphTicks);
        if (height <= 0) {
            return false;
        }
        if (display == null) {
            display = new BladeDisplay(base, material, length * 0.9);
            boss.sound("item.trident.hit_ground", 1.3f, 0.9f);
        }
        display.placeRising(base, height, length);
        if (GroundSpike.fullyRisen(tick, telegraphTicks)) {
            if (strikeNearby()) {
                return true;
            }
            ticksAfterFullyRisen++;
            return ticksAfterFullyRisen > LINGER_TICKS;
        }
        return false;
    }

    /** @return 盾に防がれたか。防がれたら、この後 {@link #tick()} は true を返して消える */
    private boolean strikeNearby() {
        for (Player player : boss.playersInRange(base.clone().add(0, length / 2, 0), HIT_RADIUS)) {
            if (!struck.add(player.getUniqueId())) {
                continue;
            }
            if (boss.independentHit(player, base, damage, knockbackBlocks, 0.3, true,
                    disablesShieldOnGuard)) {
                return true;
            }
        }
        return false;
    }

    @Override
    public void despawn() {
        if (display != null) {
            display.despawn();
        }
    }
}
