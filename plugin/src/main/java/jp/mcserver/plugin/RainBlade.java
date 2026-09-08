package jp.mcserver.plugin;

import java.util.HashSet;
import java.util.Set;
import java.util.UUID;
import jp.mcserver.core.raid.SwordRain;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.entity.Player;

/**
 * 「降り注ぐ刃」の1本（`raid_species.md` §2）。個体の召喚位置の足元Yから
 * {@link SwordRain#SPAWN_Y_OFFSET} だけ上から自由落下し、地面に刺さると0.5ブロック
 * 埋まって衝撃波を残す。落下しているあいだ、剣本体に触れたプレイヤーへも一度だけ当たる。
 *
 * <p><b>実機で確認したところ、狙った相手が落下中に動いてしまい、まったく当たらなかった。</b>
 * そこで、落下しているあいだも水平位置を狙った相手の現在位置へ寄せ続けるよう直した
 * （空間斬撃・全域大旋回の追従修正と同じ考え方——{@code HomingBlade}）。
 *
 * <p><b>盾で防がれると、その場で消える</b>（実機で確認して追加。§2「軌道の修正」）。
 * 第三形態の金のオノ（{@link jp.mcserver.core.raid.GoldenAxe}）もこのクラスで表す
 * ——動き方は同じで、素材・ダメージ・「盾を一時的に使えなくするか」だけが違う。
 */
final class RainBlade implements FloatingBlade {

    /** 剣本体の当たり判定に使う、進行方向まわりの半径（ブロック）。 */
    private static final double HIT_RADIUS = 0.6;

    /** 着地後、衝撃波の余韻を見せてから消えるまでの猶予（tick）。 */
    private static final int LINGER_TICKS = 6;

    /** 落下中、1tickに水平位置を相手へ寄せる割合（0〜1）。1に近いほど強く追う。 */
    private static final double HORIZONTAL_HOMING_RATE = 0.06;

    private final RaidBossBase boss;
    private final Player tracked;
    private final BladeDisplay display;
    private final Location position;
    private final double damage;
    private final double knockbackBlocks;
    private final boolean disablesShieldOnGuard;
    private double fallSpeed;
    private boolean landed;
    private int ticksSinceLand;
    private final Set<UUID> struck = new HashSet<>();

    RainBlade(RaidBossBase boss, Location spawnXZ, Player tracked) {
        this(boss, spawnXZ, tracked, Material.IRON_SWORD, SwordRain.SWORD_LENGTH,
                SwordRain.BLADE_DAMAGE, SwordRain.KNOCKBACK_BLOCKS, false);
    }

    /** 第三形態の金のオノなど、素材・ダメージ・盾無効化の有無だけが違う同じ動き方の1本。 */
    RainBlade(RaidBossBase boss, Location spawnXZ, Player tracked, Material material, double length,
              double damage, double knockbackBlocks, boolean disablesShieldOnGuard) {
        this.boss = boss;
        this.tracked = tracked;
        this.damage = damage;
        this.knockbackBlocks = knockbackBlocks;
        this.disablesShieldOnGuard = disablesShieldOnGuard;
        this.position = spawnXZ.clone();
        this.position.setY(boss.origin().getY() + SwordRain.SPAWN_Y_OFFSET);
        this.display = new BladeDisplay(position, material, length * 0.9);
        display.placeFlying(position);
    }

    @Override
    public boolean tick() {
        if (landed) {
            ticksSinceLand++;
            return ticksSinceLand > LINGER_TICKS;
        }
        homeHorizontally();
        double groundY = boss.groundY(position);
        fallSpeed = SwordRain.nextFallSpeed(fallSpeed);
        double nextY = position.getY() - fallSpeed;
        if (nextY <= groundY) {
            position.setY(groundY - SwordRain.EMBED_DEPTH);
            land();
            return false;
        }
        position.setY(nextY);
        display.placeFlying(position);
        return strikeNearby();
    }

    /** 落下中、水平位置を狙った相手の現在位置へ少しずつ寄せる。 */
    private void homeHorizontally() {
        if (tracked == null || !tracked.isOnline() || tracked.isDead()) {
            return;
        }
        Location at = tracked.getLocation();
        position.setX(position.getX() + (at.getX() - position.getX()) * HORIZONTAL_HOMING_RATE);
        position.setZ(position.getZ() + (at.getZ() - position.getZ()) * HORIZONTAL_HOMING_RATE);
    }

    private void land() {
        landed = true;
        display.placeFlying(position);
        boss.shockwaveAt(position.clone().add(0, SwordRain.EMBED_DEPTH, 0),
                SwordRain.SHOCKWAVE_RADIUS, SwordRain.SHOCKWAVE_HEIGHT, SwordRain.SHOCKWAVE_DAMAGE);
    }

    /** @return 盾に防がれたか。防がれたら、この後 {@link #tick()} は true を返して消える */
    private boolean strikeNearby() {
        for (Player player : boss.playersInRange(position, HIT_RADIUS)) {
            if (!struck.add(player.getUniqueId())) {
                continue;
            }
            if (boss.independentHit(player, position, damage, knockbackBlocks, 0.2, true,
                    disablesShieldOnGuard)) {
                return true;
            }
        }
        return false;
    }

    @Override
    public void despawn() {
        display.despawn();
    }
}
