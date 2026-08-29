package jp.mcserver.plugin;

import java.util.HashMap;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import jp.mcserver.core.Raid;
import jp.mcserver.core.raid.Animation;
import jp.mcserver.core.raid.KnightDefinition;
import jp.mcserver.core.raid.MotionSelector;
import jp.mcserver.core.raid.MotionSpec;
import jp.mcserver.core.raid.PartTracker;
import jp.mcserver.core.raid.RageMeter;
import jp.mcserver.core.raid.RaidSpecies;
import jp.mcserver.core.raid.Transform;
import org.bukkit.Bukkit;
import org.bukkit.Location;
import org.bukkit.Particle;
import org.bukkit.World;
import org.bukkit.block.Block;
import org.bukkit.boss.BarColor;
import org.bukkit.boss.BarStyle;
import org.bukkit.boss.BossBar;
import org.bukkit.entity.Player;
import org.bukkit.scheduler.BukkitTask;
import org.bukkit.util.Vector;

/**
 * 騎士型の挙動（§12.7）。定義（{@link KnightDefinition}）どおりに周期を回す。
 *
 * <p>周期は 待機 → 移動 → 攻撃モーション（§12.6）。攻撃モーションの選択は固定順ではなく、
 * 距離と囲まれ具合に応じて {@link MotionSelector} が決める。
 *
 * <p>被弾側には2つの機構がある。
 * <ul>
 *   <li>弱点（頭）— パリイ・妨害・空振りの直後だけ露出し、倍率が乗る</li>
 *   <li>激昂 — 個体の攻撃が長く命中しないと発動し、待機が縮みダメージが増すかわりに弱点が閉じる</li>
 * </ul>
 */
final class KnightBoss {

    /** 槍の判定が届く距離（ブロック）。実測で調整する。 */
    private static final double REACH = 5.0;

    /** 回旋突進の判定が届く距離（ブロック）。 */
    private static final double LONG_REACH = 7.0;

    /** 接地を探す高さの範囲（ブロック）。段差と坂を登り、崖では落ちる。 */
    private static final int GROUND_UP = 3;
    private static final int GROUND_DOWN = 8;

    private enum State { IDLE, APPROACH, MOTION }

    private final RaidPlugin plugin;
    private final RaidSpecies species;
    private final long maxHealth;
    private final int participants;
    private final MotionSelector selector = new MotionSelector();
    private final RageMeter rage = new RageMeter();
    private final BossBar bar;

    private double health;
    private RaidSpecies.Phase phase;
    private BossRig rig;
    private PartTracker parts;
    private BukkitTask task;

    private State state = State.IDLE;
    private int stateTick;
    private int totalTick;
    private int idleTarget;
    private MotionSpec motion;
    private final Set<Integer> firedWindows = new HashSet<>();
    private boolean interrupted;
    private boolean landedThisMotion;
    private boolean wasExposed;

    KnightBoss(RaidPlugin plugin, Location origin) {
        this.plugin = plugin;
        this.species = KnightDefinition.boss();
        this.participants = Math.max(1, origin.getWorld().getPlayers().size());
        this.maxHealth = species.healthFor(participants);
        this.health = maxHealth;
        this.phase = species.phaseAt(100);
        this.rig = new BossRig(species.rigFor(phase), grounded(origin));
        this.parts = new PartTracker(species.rigFor(phase));
        this.idleTarget = phase.behavior().idleTicks();
        this.bar = Bukkit.createBossBar(species.displayName(), BarColor.WHITE,
                BarStyle.SEGMENTED_10);
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
        StringBuilder text = new StringBuilder(String.format(
                "%s / %s / 体力 %.0f / %d（%s %dtick）", species.displayName(), phase.name(),
                health, maxHealth, state, stateTick));
        if (motion != null && state == State.MOTION) {
            text.append(" モーション ").append(motion.name());
        }
        text.append(parts.exposed() ? " 弱点露出 " + parts.exposureRemaining() + "tick" : " 弱点非露出");
        text.append(rage.enraged()
                ? " 激昂 残り " + rage.remaining() + "tick"
                : " 激昂まで " + rage.untilEnrage() + "tick");
        text.append(String.format(" / 足元 Y %.1f", rig.origin().getY()));
        return text.toString();
    }

    void spawn() {
        rig.spawn();
        updateBar();
        sound("entity.ravager.roar", 1.4f, 0.7f);
        particles(Particle.EXPLOSION_EMITTER, rig.origin().add(0, 1, 0), 2, 0.8);
        task = plugin.getServer().getScheduler().runTaskTimer(plugin, this::tick, 1L, 1L);
    }

    void despawn() {
        if (task != null) {
            task.cancel();
        }
        bar.removeAll();
        rig.despawn();
    }

    // ------------------------------------------------------------ 周期

    private void tick() {
        if (isDead()) {
            return;
        }
        checkPhase();
        stateTick++;
        totalTick++;
        parts.tick();
        boolean wasEnraged = rage.enraged();
        rage.tick();
        if (!wasEnraged && rage.enraged()) {
            onEnrage();
        }
        syncExposure();
        updateBar();

        switch (state) {
            case IDLE -> {
                animateLoop(phase.behavior().idleAnimation().orElse(null));
                if (stateTick >= idleTarget) {
                    enter(State.APPROACH);
                }
            }
            case APPROACH -> {
                animateLoop(phase.behavior().walkAnimation().orElse(null));
                approach();
                if (stateTick % 8 == 0) {
                    sound("entity.iron_golem.step", 0.7f, 0.8f);
                }
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

    /** 待機・歩行のループモーションを流す。止まって見えないようにするため常に動かす。 */
    private void animateLoop(Animation animation) {
        if (animation == null || totalTick % BossRig.UPDATE_INTERVAL != 0) {
            return;
        }
        Map<String, Transform> sampled = new HashMap<>();
        for (String part : animation.animatedParts()) {
            sampled.put(part, animation.sample(part, totalTick));
        }
        rig.applyMotion(sampled, yawToTarget());
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
        rig.moveTo(grounded(origin.add(direction)));
    }

    /** 状況に応じて次のモーションを選ぶ（§12.6）。 */
    private void startMotion() {
        MotionSelector.Situation situation = new MotionSelector.Situation(
                distanceToNearest(), surrounding(), rage.enraged());
        motion = selector.select(phase, situation, totalTick).motion();
        firedWindows.clear();
        interrupted = false;
        landedThisMotion = false;
        enter(State.MOTION);
        announceMotion();
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
                trail();
            }
        });
        motion.orbit().ifPresent(orbit -> {
            if (tick <= orbit.ticks()) {
                orbitStep(orbit);
                if (tick % 4 == 0) {
                    trail();
                }
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
            // 空振りは隙になる。突進を避けきったプレイヤーに弱点を開く（§12.6）
            if (!interrupted && !landedThisMotion && motion.charge().isPresent()) {
                parts.expose(PartTracker.WHIFF_EXPOSURE_TICKS);
                announce("§e" + motion.name() + " を空振りした — 弱点が露出");
                sound("block.beacon.activate", 1.0f, 1.6f);
            }
            idleTarget = rage.idleTicks(interrupted
                    ? motion.interrupt().map(MotionSpec.Interrupt::idleTicks)
                            .orElse(phase.behavior().idleTicks())
                    : phase.behavior().idleTicks());
            enter(State.IDLE);
        }
    }

    // ------------------------------------------------------------ 攻撃

    private void strike(MotionSpec.DamageWindow window) {
        double reach = motion.orbit().isPresent() ? LONG_REACH : REACH;
        Player target = nearest();
        swingEffect();
        if (target == null || target.getLocation().distance(rig.origin()) > reach) {
            return;
        }
        // パリイ: 盾で受けていれば無効化し、弱点を露出させる（§12.7）
        if (motion.parryable() && target.isBlocking()) {
            interrupted = true;
            parts.expose(PartTracker.EXPOSURE_TICKS);
            announce("§b" + target.getName() + " がパリイ成功 — 弱点が露出");
            sound("item.shield.block", 1.4f, 0.8f);
            particles(Particle.CRIT, target.getLocation().add(0, 1, 0), 30, 0.4);
            return;
        }
        double amount = roll(window.damage()) * rage.damageMultiplier();
        target.damage(amount);
        landedThisMotion = true;
        rage.landedHit();
        sound("entity.iron_golem.attack", 1.2f, 0.9f);
        particles(Particle.CRIT, target.getLocation().add(0, 1, 0), 12, 0.3);
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
        Location center = rig.origin();
        sound("block.anvil_land", 1.6f, 0.6f);
        particles(Particle.EXPLOSION, center, 8, 1.5);
        for (double angle = 0; angle < 360; angle += 6) {
            double radians = Math.toRadians(angle);
            Location edge = center.clone().add(Math.cos(radians) * area.radiusBlocks(), 0.2,
                    Math.sin(radians) * area.radiusBlocks());
            center.getWorld().spawnParticle(Particle.LARGE_SMOKE, edge, 1, 0, 0, 0, 0);
        }
        for (Player player : center.getWorld().getPlayers()) {
            if (player.getLocation().distance(center) <= area.radiusBlocks()) {
                player.damage(roll(area.damage()) * rage.damageMultiplier());
                landedThisMotion = true;
                rage.landedHit();
            }
        }
    }

    /**
     * 部位への攻撃。倍率と妨害をまとめて処理する。
     *
     * <p>ダメージ量はイベントの値ではなく {@link WeaponDamage} で組み立てる。
     * 当たり判定に使う Interaction は生き物ではないため、イベントが運ぶ値は武器を反映しない。
     */
    boolean handleHit(UUID hitEntity, Player attacker) {
        String part = rig.partOfHitbox(hitEntity);
        if (part == null) {
            return false;
        }
        if (state == State.MOTION && motion != null) {
            motion.interrupt().ifPresent(interrupt -> {
                if (interrupt.part().equals(part) && stateTick <= interrupt.beforeTick()) {
                    interrupted = true;
                    parts.expose(PartTracker.EXPOSURE_TICKS);
                    announce("§a" + attacker.getName() + " が " + motion.name()
                            + " を中断させた — 弱点が露出");
                    sound("item.shield.block", 1.2f, 1.4f);
                }
            });
        }

        PartTracker.Result result = parts.hit(part, WeaponDamage.of(attacker), rage.enraged());
        if (result.immune()) {
            attacker.sendMessage(part + " にダメージは通らない");
            sound("entity.zombie.attack_iron_door", 0.8f, 1.6f);
            return true;
        }
        health -= result.dealt();

        if (result.critical()) {
            attacker.sendMessage(String.format("§c会心 %s に %.1f（×%.1f / 残り %.0f）",
                    part, result.dealt(), result.multiplier(), Math.max(0, health)));
            sound("entity.player.attack.crit", 1.0f, 1.2f);
            particles(Particle.ELECTRIC_SPARK, rig.centerOf(part), 20, 0.4);
        } else {
            attacker.sendMessage(String.format("%s に %.1f（残り %.0f）",
                    part, result.dealt(), Math.max(0, health)));
        }
        return true;
    }

    // ------------------------------------------------------------ 状態の演出

    private void syncExposure() {
        boolean exposed = parts.exposed();
        if (exposed == wasExposed) {
            return;
        }
        wasExposed = exposed;
        rig.setExposed(exposed);
        if (exposed) {
            particles(Particle.END_ROD, rig.centerOf("頭"), 25, 0.4);
        }
    }

    private void onEnrage() {
        announce("§4騎士が激昂した — 待機が縮み、弱点が閉じる");
        sound("entity.ravager.roar", 1.6f, 0.6f);
        particles(Particle.ANGRY_VILLAGER, rig.origin().add(0, 2.5, 0), 30, 0.8);
        particles(Particle.FLAME, rig.origin().add(0, 1.5, 0), 40, 1.0);
        title("§4激 昂", "§7弱点が閉じた。前に出て捌け");
    }

    private void updateBar() {
        double progress = Math.max(0, Math.min(1, health / maxHealth));
        bar.setProgress(progress);
        StringBuilder title = new StringBuilder("§f" + species.displayName() + " §7— " + phase.name());
        if (state == State.MOTION && motion != null) {
            title.append(" §e▶ ").append(motion.name());
        }
        if (rage.enraged()) {
            title.append(" §4[激昂]");
        }
        if (parts.exposed()) {
            title.append(" §c[弱点露出]");
        }
        bar.setTitle(title.toString());
        bar.setColor(rage.enraged() ? BarColor.RED
                : parts.exposed() ? BarColor.YELLOW
                : phase.healthThreshold() < 100 ? BarColor.PURPLE : BarColor.WHITE);
        for (Player player : rig.origin().getWorld().getPlayers()) {
            if (!bar.getPlayers().contains(player)) {
                bar.addPlayer(player);
            }
        }
    }

    private void announceMotion() {
        // モーション名はボスバーに出す。予備動作の音でも同時に伝える
        sound(switch (motion.name()) {
            case "突進切り上げ" -> "item.trident.throw";
            case "なぎ払い" -> "entity.player.attack.sweep";
            case "3段突き" -> "item.trident.riptide_1";
            case "追従4連切り" -> "entity.player.attack.strong";
            case "回旋突進" -> "entity.horse.gallop";
            case "踏みつけ" -> "entity.ravager.step";
            default -> "entity.iron_golem.attack";
        }, 1.3f, 1.0f);
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
        selector.reset();
        rage.reset();
        wasExposed = false;
        // 骨格が変わるので作り直す（§12.7 の形態変化）
        Location origin = rig.origin();
        rig.despawn();
        rig = new BossRig(species.rigFor(phase), origin);
        parts = new PartTracker(species.rigFor(phase));
        rig.spawn();
        enter(State.IDLE);
        announce("§5" + phase.name() + " へ移行 — " + phase.gimmick());
        title("§5" + phase.name(), "§7" + species.displayName() + "の姿が変わった");
        sound("entity.ender_dragon.growl", 1.6f, 0.8f);
        particles(Particle.EXPLOSION_EMITTER, origin.clone().add(0, 1.5, 0), 4, 1.0);
        particles(Particle.SOUL_FIRE_FLAME, origin.clone().add(0, 1.5, 0), 60, 1.5);
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
        rig.moveTo(grounded(origin.add(direction.normalize().multiply(blocks))));
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
        rig.moveTo(grounded(next));
    }

    /**
     * 足元の地面に合わせた位置を返す。
     *
     * <p>これがないと、召喚した高さのまま水平に滑って地形を無視する。
     * 上に {@value #GROUND_UP} ブロックまで登り、下に {@value #GROUND_DOWN} ブロックまで降りる。
     * その範囲に地面が無ければ元の高さを保ち、空中へ吸い込まれないようにする。
     */
    private Location grounded(Location target) {
        World world = target.getWorld();
        int x = target.getBlockX();
        int z = target.getBlockZ();
        int from = target.getBlockY() + GROUND_UP;
        int to = target.getBlockY() - GROUND_DOWN;
        for (int y = from; y >= to; y--) {
            Block block = world.getBlockAt(x, y, z);
            if (block.getType().isSolid()) {
                Location grounded = target.clone();
                grounded.setY(y + 1);
                return grounded;
            }
        }
        return target;
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

    private double distanceToNearest() {
        Player target = nearest();
        return target == null ? 0 : target.getLocation().distance(rig.origin());
    }

    /** 近接圏内のプレイヤー数。なぎ払いや踏みつけの条件になる（§12.6）。 */
    private int surrounding() {
        Location origin = rig.origin();
        int count = 0;
        for (Player player : origin.getWorld().getPlayers()) {
            if (player.isDead() || player.getGameMode().name().equals("SPECTATOR")) {
                continue;
            }
            if (player.getLocation().distance(origin) <= MotionSpec.Usage.CROWD_RADIUS) {
                count++;
            }
        }
        return count;
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

    // ------------------------------------------------------------ 演出の下請け

    private void announce(String message) {
        for (Player player : rig.origin().getWorld().getPlayers()) {
            player.sendMessage("[騎士] " + message);
        }
    }

    private void title(String main, String sub) {
        for (Player player : rig.origin().getWorld().getPlayers()) {
            player.sendTitle(main, sub, 5, 30, 10);
        }
    }

    /** 音は名前で鳴らす。列挙の改名に左右されない。 */
    private void sound(String key, float volume, float pitch) {
        Location origin = rig.origin();
        origin.getWorld().playSound(origin, "minecraft:" + key, volume, pitch);
    }

    private void particles(Particle particle, Location location, int count, double spread) {
        location.getWorld().spawnParticle(particle, location, count, spread, spread, spread, 0);
    }

    /** 槍の軌跡。 */
    private void swingEffect() {
        Location tip = rig.centerOf("穂先");
        particles(Particle.SWEEP_ATTACK, tip, 3, 0.3);
        particles(Particle.END_ROD, tip, 6, 0.2);
    }

    /** 突進・回旋の足跡。 */
    private void trail() {
        Location origin = rig.origin();
        particles(Particle.LARGE_SMOKE, origin.clone().add(0, 0.1, 0), 4, 0.3);
    }

    /** 討伐の演出。 */
    void playDefeat() {
        Location origin = rig.origin();
        sound("entity.ender_dragon.death", 1.6f, 1.2f);
        particles(Particle.EXPLOSION_EMITTER, origin.clone().add(0, 1.5, 0), 6, 1.2);
        particles(Particle.SOUL_FIRE_FLAME, origin.clone().add(0, 1.5, 0), 80, 1.5);
        title("§6討 伐", "§7" + species.displayName() + "を倒した");
    }

    /** 参加人数に応じた体力（§12.3）の確認用。 */
    static long healthFor(int participants) {
        return KnightDefinition.BASE_HEALTH
                * Raid.difficulty(participants).healthPercent() / 100;
    }
}
