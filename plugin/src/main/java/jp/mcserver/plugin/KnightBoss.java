package jp.mcserver.plugin;

import java.util.HashMap;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import jp.mcserver.core.Raid;
import jp.mcserver.core.raid.Animation;
import jp.mcserver.core.raid.KnightDefinition;
import jp.mcserver.core.raid.MotionSpec;
import jp.mcserver.core.raid.RaidSpecies;
import jp.mcserver.core.raid.Transform;
import org.bukkit.Location;
import org.bukkit.entity.Player;
import org.bukkit.scheduler.BukkitTask;
import org.bukkit.util.Vector;

/**
 * 騎士型の挙動（§12.7）。定義（{@link KnightDefinition}）どおりに周期を回す。
 *
 * <p>周期は 待機 → 移動 → 攻撃モーション（§12.6）。
 */
final class KnightBoss {

    /** 槍の判定が届く距離（ブロック）。実測で調整する。 */
    private static final double REACH = 5.0;

    private enum State { IDLE, APPROACH, MOTION }

    private final RaidPlugin plugin;
    private final RaidSpecies species;
    private final long maxHealth;
    private final int participants;

    private double health;
    private RaidSpecies.Phase phase;
    private BossRig rig;
    private BukkitTask task;

    private State state = State.IDLE;
    private int stateTick;
    private int idleTarget;
    private MotionSpec motion;
    private int motionIndex;
    private final Set<Integer> firedWindows = new HashSet<>();
    private boolean interrupted;

    KnightBoss(RaidPlugin plugin, Location origin) {
        this.plugin = plugin;
        this.species = KnightDefinition.boss();
        this.participants = Math.max(1, origin.getWorld().getPlayers().size());
        this.maxHealth = species.healthFor(participants);
        this.health = maxHealth;
        this.phase = species.phaseAt(100);
        this.rig = new BossRig(species.rigFor(phase), origin);
        this.idleTarget = phase.behavior().idleTicks();
    }

    int participants() {
        return participants;
    }

    long maxHealth() {
        return maxHealth;
    }

    boolean isDead() {
        return health <= 0;
    }

    String status() {
        return String.format("%s / %s / 体力 %.0f / %d（%s %dtick）",
                species.displayName(), phase.name(), health, maxHealth, state,
                stateTick);
    }

    void spawn() {
        rig.spawn();
        task = plugin.getServer().getScheduler().runTaskTimer(plugin, this::tick, 1L, 1L);
    }

    void despawn() {
        if (task != null) {
            task.cancel();
        }
        rig.despawn();
    }

    // ------------------------------------------------------------ 周期

    private void tick() {
        if (isDead()) {
            return;
        }
        checkPhase();
        stateTick++;

        switch (state) {
            case IDLE -> {
                if (stateTick >= idleTarget) {
                    enter(State.APPROACH);
                }
            }
            case APPROACH -> {
                approach();
                if (stateTick >= phase.behavior().approachTicks()) {
                    startMotion();
                }
            }
            case MOTION -> runMotion();
            default -> { }
        }
    }

    private void enter(State next) {
        state = next;
        stateTick = 0;
    }

    private void approach() {
        Player target = nearest();
        if (target == null) {
            return;
        }
        Location origin = rig.origin();
        Vector direction = target.getLocation().toVector().subtract(origin.toVector());
        direction.setY(0);
        if (direction.lengthSquared() < 0.01) {
            return;
        }
        direction.normalize().multiply(phase.behavior().blocksPerTick());
        rig.moveTo(origin.add(direction));
    }

    private void startMotion() {
        var names = phase.motionNames();
        motion = phase.motion(names.get(motionIndex++ % names.size()));
        firedWindows.clear();
        interrupted = false;
        enter(State.MOTION);
        plugin.getServer().broadcastMessage("[騎士] " + motion.name());
    }

    private void runMotion() {
        Animation animation = motion.animation();
        int tick = stateTick;

        // 姿勢の更新は更新間隔ごと（§12.6）
        if (tick % BossRig.UPDATE_INTERVAL == 0) {
            Map<String, Transform> sampled = new HashMap<>();
            for (String part : animation.animatedParts()) {
                sampled.put(part, animation.sample(part, tick));
            }
            rig.applyMotion(sampled, yawToTarget());
        }

        // 突進・回旋の移動
        motion.charge().ifPresent(charge -> {
            if (tick >= animation.durationTicks() - charge.perTicks()) {
                moveForward(charge.blocksPerTick());
            }
        });
        motion.orbit().ifPresent(orbit -> {
            if (tick <= orbit.ticks()) {
                orbitStep(orbit);
            }
        });

        // ダメージ判定
        for (int i = 0; i < motion.damageWindows().size(); i++) {
            MotionSpec.DamageWindow window = motion.damageWindows().get(i);
            if (tick == window.fromTick() && firedWindows.add(i)) {
                strike(window);
            }
        }
        motion.area().ifPresent(area -> {
            if (tick == animation.durationTicks()) {
                shockwave(area);
            }
        });

        if (interrupted || tick >= animation.durationTicks()) {
            idleTarget = interrupted
                    ? motion.interrupt().map(MotionSpec.Interrupt::idleTicks)
                            .orElse(phase.behavior().idleTicks())
                    : phase.behavior().idleTicks();
            enter(State.IDLE);
        }
    }

    // ------------------------------------------------------------ 攻撃

    private void strike(MotionSpec.DamageWindow window) {
        Player target = nearest();
        if (target == null || target.getLocation().distance(rig.origin()) > REACH) {
            return;
        }
        // パリイ: 盾で受けていれば無効化し、大きな隙を作る（§12.7）
        if (motion.parryable() && target.isBlocking()) {
            interrupted = true;
            target.sendMessage("パリイ成功");
            return;
        }
        double amount = roll(window.damage());
        target.damage(amount);
        motion.knockback().ifPresent(knockback -> {
            Vector push = target.getLocation().toVector()
                    .subtract(rig.origin().toVector()).setY(0);
            if (push.lengthSquared() > 0.01) {
                push.normalize().multiply(knockback.backBlocks() / 5.0);
            }
            push.setY(knockback.upBlocks() / 5.0);
            target.setVelocity(push);
        });
    }

    private void shockwave(MotionSpec.AreaEffect area) {
        for (Player player : rig.origin().getWorld().getPlayers()) {
            if (player.getLocation().distance(rig.origin()) <= area.radiusBlocks()) {
                player.damage(roll(area.damage()));
            }
        }
    }

    /** 部位への攻撃。被弾する部位なら体力を減らし、妨害の対象なら中断する。 */
    boolean handleHit(UUID hitEntity, Player attacker, double damage) {
        String part = rig.partOfHitbox(hitEntity);
        if (part == null) {
            return false;
        }
        if (state == State.MOTION && motion != null) {
            motion.interrupt().ifPresent(interrupt -> {
                if (interrupt.part().equals(part) && stateTick <= interrupt.beforeTick()) {
                    interrupted = true;
                    attacker.sendMessage("[騎士] " + motion.name() + " を中断させた");
                }
            });
        }
        if (rig.rig().part(part).damageable()) {
            health -= damage;
            attacker.sendMessage(String.format("%s に %.1f（残り %.0f）", part, damage, Math.max(0, health)));
        } else {
            attacker.sendMessage(part + " にダメージは通らない");
        }
        return true;
    }

    // ------------------------------------------------------------ 段階

    private void checkPhase() {
        int percent = (int) Math.ceil(health * 100 / maxHealth);
        RaidSpecies.Phase current = species.phaseAt(Math.max(0, Math.min(100, percent)));
        if (current == phase) {
            return;
        }
        phase = current;
        idleTarget = phase.behavior().idleTicks();
        // 骨格が変わる場合は作り直す（§12.7 の形態変化）
        Location origin = rig.origin();
        rig.despawn();
        rig = new BossRig(species.rigFor(phase), origin);
        rig.spawn();
        enter(State.IDLE);
        plugin.getServer().broadcastMessage("[騎士] " + phase.name() + " へ移行");
    }

    // ------------------------------------------------------------ 補助

    private void moveForward(double blocks) {
        Player target = nearest();
        if (target == null) {
            return;
        }
        Location origin = rig.origin();
        Vector direction = target.getLocation().toVector().subtract(origin.toVector());
        direction.setY(0);
        if (direction.lengthSquared() < 0.01) {
            return;
        }
        rig.moveTo(origin.add(direction.normalize().multiply(blocks)));
    }

    private void orbitStep(MotionSpec.Orbit orbit) {
        Player target = nearest();
        if (target == null) {
            return;
        }
        double radius = orbit.diameterBlocks() / 2;
        double anglePerTick = 2 * Math.PI * orbit.laps() / orbit.ticks();
        double angle = anglePerTick * stateTick;
        Location center = target.getLocation();
        Location next = center.clone().add(
                Math.cos(angle) * radius, 0, Math.sin(angle) * radius);
        next.setY(center.getY());
        rig.moveTo(next);
    }

    private Player nearest() {
        Location origin = rig.origin();
        Player closest = null;
        double best = Double.MAX_VALUE;
        for (Player player : origin.getWorld().getPlayers()) {
            if (player.isDead() || player.getGameMode().name().equals("SPECTATOR")) {
                continue;
            }
            double distance = player.getLocation().distanceSquared(origin);
            if (distance < best) {
                best = distance;
                closest = player;
            }
        }
        return closest;
    }

    private double yawToTarget() {
        Player target = nearest();
        if (target == null) {
            return 0;
        }
        Location origin = rig.origin();
        double dx = target.getLocation().getX() - origin.getX();
        double dz = target.getLocation().getZ() - origin.getZ();
        return Math.toDegrees(Math.atan2(-dx, dz));
    }

    private double roll(MotionSpec.Damage damage) {
        return damage.random()
                ? damage.min() + Math.random() * (damage.max() - damage.min())
                : damage.min();
    }

    /** 参加人数に応じた体力（§12.3）の確認用。 */
    static long healthFor(int participants) {
        return KnightDefinition.BASE_HEALTH
                * Raid.difficulty(participants).healthPercent() / 100;
    }
}
