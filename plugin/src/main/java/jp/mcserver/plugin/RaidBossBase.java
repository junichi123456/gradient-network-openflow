package jp.mcserver.plugin;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import jp.mcserver.core.Raid;
import jp.mcserver.core.raid.Animation;
import jp.mcserver.core.raid.MotionSelector;
import jp.mcserver.core.raid.MotionSpec;
import jp.mcserver.core.raid.PartTracker;
import jp.mcserver.core.raid.PoseTransition;
import jp.mcserver.core.raid.RageMeter;
import jp.mcserver.core.raid.RaidDrop;
import jp.mcserver.core.raid.RaidSpecies;
import jp.mcserver.core.raid.Rig;
import jp.mcserver.core.raid.ShieldGuard;
import jp.mcserver.core.raid.Stage;
import jp.mcserver.core.raid.TrackingDelay;
import jp.mcserver.core.raid.Transform;
import net.kyori.adventure.text.Component;
import org.bukkit.Bukkit;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.Particle;
import org.bukkit.World;
import org.bukkit.block.Block;
import org.bukkit.boss.BarColor;
import org.bukkit.boss.BarStyle;
import org.bukkit.boss.BossBar;
import org.bukkit.enchantments.Enchantment;
import org.bukkit.entity.Player;
import org.bukkit.inventory.ItemStack;
import org.bukkit.inventory.meta.Damageable;
import org.bukkit.scheduler.BukkitTask;
import org.bukkit.util.Vector;

/**
 * レイド個体の挙動の土台（§12.6）。周期は 待機 → 移動 → 攻撃モーション。
 * 攻撃モーションの選択は固定順ではなく、距離と囲まれ具合に応じて
 * {@link MotionSelector} が決める（共通の規約）。
 *
 * <p><b>{@link KnightBoss} と {@link HollowGuardBoss} の重複を括り出したものである。</b>
 * 被弾・盾のガード・接地・向き直り・パリイ・跳躍・回旋・中心経由といった§12.6の共通の規約は
 * 種によらず同じ形をしており、種ごとに違うのは
 * <ul>
 *   <li>骨格とモーションの中身（{@link RaidSpecies} — {@code KnightDefinition} /
 *       {@code HollowGuardDefinition} が持つ）</li>
 *   <li>間合い・リーチなどの定数（{@link Tuning}）</li>
 *   <li>効果音・パーティクルの「色」（下の abstract メソッド群）</li>
 * </ul>
 * だけである。3種目以降は {@link Tuning} を1つ作り、効果音の一覧を実装すれば済む。
 *
 * <p>跳躍・回旋・パリイ・妨害・範囲攻撃は、どの種も同じコードで処理する。
 * {@link MotionSpec} 側がこれらを {@code Optional} で持つため、使わない種では
 * 該当する分岐が実行されないだけで、個体側に特別な対応は要らない。
 */
abstract class RaidBossBase implements RaidBoss {

    /**
     * 種ごとに違う定数（§12.6 の間合い・リーチなど）。
     *
     * @param weaponReach             武器の判定に足す余裕（ブロック）
     * @param standoffBlocks          密着せず止まる距離（ブロック）
     * @param playerHeight            プレイヤーの立ち姿の高さ（ブロック）
     * @param attackRangeBlocks       攻撃を受け付ける距離（ブロック）
     * @param maxTurnDegrees          1回の姿勢更新で回れる角度の上限（度）
     * @param baseKnockback           どの攻撃にも乗る最小の押し出し（ブロック）
     * @param idleTrackingDelayTicks  待機中に相手の位置を遅らせて追う量（tick）
     * @param announceTag             チャットの角括弧に出す個体名（例: 騎士・虚刃）
     */
    record Tuning(double weaponReach, double standoffBlocks, double playerHeight,
                  double attackRangeBlocks, double maxTurnDegrees, double baseKnockback,
                  int idleTrackingDelayTicks, String announceTag) {
    }

    private static final int TURN_MAX_TICKS = 40;
    private static final double TURN_TOLERANCE_DEGREES = 12.0;

    private static final double FALL_GRAVITY = 0.08;
    private static final double FALL_MAX = 1.5;

    private static final int GROUND_UP = 3;
    private static final int GROUND_DOWN = 8;

    private static final int RETURN_WALK_MIN_TICKS = 20;
    private static final int RETURN_WALK_MAX_TICKS = 40;

    enum State {
        /** 落下中。地表面に着くまで技を出さない（レイド次元の登場） */
        ENTER,
        IDLE, APPROACH, MOTION, RETURN, TURN
    }

    private final RaidPlugin plugin;
    private final RaidSpecies species;
    private final Tuning tuning;
    private final long maxHealth;
    private final int participants;
    private final MotionSelector selector = new MotionSelector();
    private final RageMeter rage = new RageMeter();
    private final PoseTransition transition = new PoseTransition();
    private final Stage stage;
    private final Stage.CenterVisit centerVisit = new Stage.CenterVisit();
    private final Location stageCenter;
    private Map<String, Transform> lastPose = new HashMap<>();
    private final BossBar bar;

    private double health;
    private RaidSpecies.Phase phase;
    private BossRig rig;
    private PartTracker parts;
    private BukkitTask task;

    private State state;
    private int stateTick;
    private int totalTick;
    private int idleTarget;
    private MotionSpec motion;
    private final Map<Integer, Set<UUID>> struckByWindow = new HashMap<>();

    private double bodyYaw;
    private double orbitStartAngle;
    private double orbitStartRadius;
    private boolean interrupted;
    private boolean landedThisMotion;
    private boolean wasExposed;
    private double chargeTravelled;
    private Vector chargeDirection;
    private double parryDamage;
    private int parryCount;
    private final Set<UUID> struck = new HashSet<>();
    private final java.util.Random random = new java.util.Random();

    private final Map<UUID, Double> contribution = new LinkedHashMap<>();
    private UUID trackedTarget;
    private final TrackingDelay tracking;
    private double fallSpeed;

    private int returnWalkTarget;
    private Location leapFrom;
    private Location leapTo;
    private Location waveCenter;

    /**
     * @param facingYaw 体の向き（度）。0 が南、180 が北
     * @param dropIn    true なら渡された高さから自由落下して地表面に到達する
     */
    RaidBossBase(RaidPlugin plugin, RaidSpecies species, Tuning tuning, Location origin,
                double facingYaw, boolean dropIn) {
        this.plugin = plugin;
        this.species = species;
        this.tuning = tuning;
        this.tracking = new TrackingDelay(tuning.idleTrackingDelayTicks());
        Location spawn = dropIn ? origin.clone() : grounded(origin);
        this.stage = new Stage(spawn.getX(), spawn.getZ());
        this.stageCenter = spawn.clone();
        this.participants = Math.max(1, (int) spawn.getWorld().getPlayers().stream()
                .filter(player -> stage.contains(player.getLocation().getX(),
                        player.getLocation().getZ()))
                .count());
        this.maxHealth = species.healthFor(Math.min(participants, Raid.MAX_PARTICIPANTS));
        this.health = maxHealth;
        this.phase = species.phaseAt(100);
        this.rig = new BossRig(species.rigFor(phase), spawn);
        this.parts = new PartTracker(species.rigFor(phase));
        this.bodyYaw = normalizeDegrees(facingYaw);
        this.state = dropIn ? State.ENTER : State.IDLE;
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

    @Override
    public boolean isDead() {
        return health <= 0;
    }

    @Override
    public List<UUID> rewarded() {
        return contribution.entrySet().stream()
                .filter(entry -> RaidDrop.qualifies(entry.getValue(), maxHealth, participants))
                .sorted(Map.Entry.<UUID, Double>comparingByValue().reversed())
                .map(Map.Entry::getKey)
                .toList();
    }

    @Override
    public double dealtBy(UUID player) {
        return contribution.getOrDefault(player, 0.0);
    }

    @Override
    public double rewardThreshold() {
        return RaidDrop.requiredDamage(maxHealth, participants);
    }

    /** いまの立ち位置。見た目の方式を切り替えて出し直すときに使う（騎士型）。 */
    Location location() {
        return origin();
    }

    @Override
    public String status() {
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
        Location here = rig.origin();
        text.append(String.format(" / 足元 Y %.1f / 中心から %.1f（半径 %.0f）",
                here.getY(), stage.distanceFromCenter(here.getX(), here.getZ()),
                stage.radius()));
        if (centerVisit.owed()) {
            text.append(" / 中心へ帰還中");
        }
        if (motion != null && state == State.MOTION) {
            motion.charge().ifPresent(run -> text.append(String.format(
                    " / 前進 %.1f / %.0f ブロック", chargeTravelled, run.distanceBlocks())));
            motion.parry().ifPresent(parry -> text.append(String.format(
                    " / パリイ %.0f / %.0f（成功 %d回）", parryDamage,
                    parry.requiredDamage(parryCount), parryCount)));
        }
        return text.toString();
    }

    void spawn() {
        rig.spawn();
        updateBar();
        playSpawnEffect();
        task = plugin.getServer().getScheduler().runTaskTimer(plugin, this::tick, 1L, 1L);
    }

    @Override
    public void despawn() {
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

        Location here = rig.origin();
        boolean wasOwed = centerVisit.owed();
        centerVisit.observe(stage, here.getX(), here.getZ());
        if (!wasOwed && centerVisit.owed()) {
            announce("§7戦場の外へ出た — 中心へ戻る");
        }
        if (totalTick % 20 == 0) {
            drawBoundary();
        }
        trackTarget();
        tickSpecial();

        switch (state) {
            case ENTER -> descend();
            case IDLE -> {
                animateLoop(phase.behavior().idleAnimation().orElse(null));
                if (stateTick >= idleTarget) {
                    enter(centerVisit.owed() ? State.RETURN : State.APPROACH);
                }
            }
            case APPROACH -> {
                boolean inRange = approach();
                animateLoop(inRange
                        ? phase.behavior().idleAnimation().orElse(null)
                        : phase.behavior().walkAnimation().orElse(null));
                if (!inRange && stateTick % 8 == 0) {
                    playStepEffect();
                }
                if (inRange || stateTick >= phase.behavior().approachTicks()) {
                    startMotion();
                }
            }
            case RETURN -> returnToCenter();
            case MOTION -> runMotion();
            case TURN -> {
                animateLoop(phase.behavior().idleAnimation().orElse(null));
                if (facingTarget() || stateTick >= TURN_MAX_TICKS) {
                    enter(State.IDLE);
                }
            }
            default -> { }
        }
    }

    private void enter(State next) {
        state = next;
        stateTick = 0;
        transition.begin(lastPose);
        if (next == State.RETURN) {
            returnWalkTarget = RETURN_WALK_MIN_TICKS
                    + random.nextInt(RETURN_WALK_MAX_TICKS - RETURN_WALK_MIN_TICKS + 1);
        }
    }

    private void animateLoop(Animation animation) {
        if (animation == null || totalTick % BossRig.UPDATE_INTERVAL != 0) {
            return;
        }
        Map<String, Transform> sampled = new HashMap<>();
        for (String part : animation.animatedParts()) {
            sampled.put(part, animation.sample(part, totalTick));
        }
        applyPose(sampled);
    }

    private void applyPose(Map<String, Transform> sampled) {
        Map<String, Transform> pose = transition.apply(sampled, BossRig.UPDATE_INTERVAL);
        lastPose = new HashMap<>(pose);
        rig.applyMotion(pose, turnToward(yawToTarget()));
    }

    /** 相手へ歩み寄る。密着までは踏み込まない（{@link Tuning#standoffBlocks()} で止まる）。 */
    private boolean approach() {
        Player target = nearest();
        if (target == null) {
            return false;
        }
        Location origin = rig.origin();
        Vector direction = target.getLocation().toVector().subtract(origin.toVector());
        direction.setY(0);
        double distance = direction.length();
        if (distance <= tuning.standoffBlocks()) {
            return true;
        }
        double step = Math.min(phase.behavior().blocksPerTick(),
                distance - tuning.standoffBlocks());
        direction.normalize().multiply(step);
        rig.moveTo(grounded(origin.add(direction)));
        return false;
    }

    /** 中心へ歩いて戻る（§12.6 の共通の規約「中心経由」）。 */
    private void returnToCenter() {
        animateLoop(phase.behavior().walkAnimation().orElse(null));
        Location here = rig.origin();
        Vector direction = new Vector(stage.centerX() - here.getX(), 0,
                stage.centerZ() - here.getZ());
        if (direction.lengthSquared() < 0.01 || !centerVisit.owed()) {
            announce("§7戦場の中心へ戻った");
            enter(State.APPROACH);
            return;
        }
        if (stateTick >= returnWalkTarget) {
            startMotion();
            return;
        }
        step(direction.normalize(), phase.behavior().blocksPerTick());
        if (stateTick % 8 == 0) {
            playStepEffect();
        }
    }

    private void drawBoundary() {
        for (int degrees = 0; degrees < 360; degrees += 10) {
            double radians = Math.toRadians(degrees);
            Location edge = stageCenter.clone().add(Math.cos(radians) * stage.radius(), 0.4,
                    Math.sin(radians) * stage.radius());
            stageCenter.getWorld().spawnParticle(boundaryParticle(), edge, 1, 0, 0, 0, 0);
        }
    }

    private void startMotion() {
        Location here = rig.origin();
        MotionSelector.Situation situation = new MotionSelector.Situation(
                distanceToNearest(), surrounding(), rage.enraged(),
                !stage.contains(here.getX(), here.getZ()));
        motion = selector.select(phase, situation, totalTick).motion();
        struckByWindow.clear();
        struck.clear();
        interrupted = false;
        landedThisMotion = false;
        chargeTravelled = 0;
        chargeDirection = null;
        parryDamage = 0;
        leapFrom = null;
        leapTo = null;
        waveCenter = null;
        motion.orbit().ifPresent(orbit -> {
            double dx = here.getX() - stage.centerX();
            double dz = here.getZ() - stage.centerZ();
            orbitStartAngle = Math.atan2(dz, dx);
            orbitStartRadius = Math.hypot(dx, dz);
        });
        motion.leap().ifPresent(leap -> {
            leapFrom = rig.origin();
            Location center = rig.origin();
            center.setX(stage.centerX());
            center.setZ(stage.centerZ());
            leapTo = grounded(center);
        });
        enter(State.MOTION);
        playMotionStartEffect();
    }

    private void runMotion() {
        Animation animation = motion.animation();
        int tick = stateTick;

        if (tick % BossRig.UPDATE_INTERVAL == 0) {
            Map<String, Transform> sampled = new HashMap<>();
            for (String part : animation.animatedParts()) {
                sampled.put(part, animation.sample(part, tick));
            }
            applyPose(sampled);
        }

        motion.leap().ifPresent(leap -> runLeap(leap, tick));
        motion.charge().ifPresent(run -> runCharge(run, tick));
        motion.orbit().ifPresent(orbit -> {
            if (tick <= orbit.ticks()) {
                orbitStep(orbit);
                if (tick % 4 == 0) {
                    trail();
                }
            }
        });

        for (int i = 0; i < motion.damageWindows().size(); i++) {
            MotionSpec.DamageWindow window = motion.damageWindows().get(i);
            if (tick >= window.fromTick() && tick <= window.toTick()) {
                applyWindow(i, window);
            }
        }
        motion.area().ifPresent(area -> {
            int start = motion.leap().map(MotionSpec.Leap::landingTick)
                    .orElse(animation.durationTicks());
            if (area.instant()) {
                if (tick == start) {
                    waveCenter = rig.origin();
                    shockwave(area);
                }
            } else if (tick >= start && tick <= start + area.ticksToFullRadius()) {
                expandingWave(area, tick - start);
            }
        });

        if (interrupted || tick >= animation.durationTicks()) {
            if (!interrupted && !landedThisMotion && motion.charge().isPresent()) {
                parts.expose(PartTracker.WHIFF_EXPOSURE_TICKS);
                announce("§e" + motion.name() + " を空振りした — 弱点が露出");
                sound("block.beacon.activate", 1.0f, 1.6f);
            }
            if (interrupted) {
                idleTarget = rage.idleTicks(motion.interrupt()
                        .map(MotionSpec.Interrupt::idleTicks)
                        .orElse(phase.behavior().idleTicks()));
            } else if (motion.idleAfter().fixed()) {
                idleTarget = rage.idleTicks(motion.idleAfter().minTicks());
            } else {
                idleTarget = motion.idleAfter().pick(random);
                announce("§7着地の隙 — " + idleTarget + "tick");
            }
            chargeDirection = null;
            enter(State.TURN);
        }
    }

    /** 登場の落下（レイド次元の会場に落ちてくる）。 */
    private void descend() {
        animateLoop(phase.behavior().idleAnimation().orElse(null));
        Location here = rig.origin();
        double groundY = grounded(here).getY();
        if (here.getY() - groundY <= 1e-3) {
            playLandEffect();
            fallSpeed = 0;
            enter(State.IDLE);
            return;
        }
        fallSpeed = Math.min(FALL_MAX, fallSpeed + FALL_GRAVITY);
        Location next = here.clone();
        next.setY(Math.max(groundY, here.getY() - fallSpeed));
        rig.moveTo(next);
    }

    private boolean facingTarget() {
        double delta = normalizeDegrees(yawToTarget() - bodyYaw);
        return Math.abs(delta) <= TURN_TOLERANCE_DEGREES;
    }

    /**
     * 突進・前進を進める。後ずさり（あれば）→ 加速 → 決めた距離を走り切る。
     * 走り出した時点で向きを固定する。追尾させると避けようがなくなる。
     */
    private void runCharge(MotionSpec.Charge run, int tick) {
        if (tick > run.startTick() && tick <= run.runFromTick()) {
            step(backwardDirection(), run.backstepPerTick());
            return;
        }
        if (tick <= run.runFromTick() || tick > run.endTick()) {
            return;
        }
        if (chargeDirection == null) {
            chargeDirection = forwardDirection();
            playChargeStartEffect();
        }
        // 進行方向の左右 degrees 度以内で最も手前の相手を毎tick追う（§12.6）。
        // 横へ抜ければ追われない
        if (run.homing()) {
            Player homing = nearestInCone(chargeDirection, run.homingDegrees());
            if (homing != null) {
                Vector toward = homing.getLocation().toVector()
                        .subtract(rig.origin().toVector());
                toward.setY(0);
                if (toward.lengthSquared() > 0.0001) {
                    chargeDirection = toward.normalize();
                }
            }
        }
        if (run.throughCenter()) {
            chargeDirection = throughCenter(chargeDirection, run.centerCorridorBlocks());
        }
        int since = tick - run.runFromTick();
        double step = Math.min(run.speedAt(since), run.distanceBlocks() - chargeTravelled);
        if (step <= 0) {
            return;
        }
        step(chargeDirection, step);
        chargeTravelled += step;
        trail();
    }

    /** 中心から {@code corridor} ブロック以内を通る向きへ丸める（§12.6）。 */
    private Vector throughCenter(Vector direction, double corridor) {
        Location here = rig.origin();
        double desired = Math.atan2(direction.getZ(), direction.getX());
        double angle = stage.corridorAngle(here.getX(), here.getZ(), desired, corridor);
        return new Vector(Math.cos(angle), 0, Math.sin(angle));
    }

    /** 進行方向から左右 {@code degrees} 度以内にいる、最も手前のプレイヤー。 */
    private Player nearestInCone(Vector direction, double degrees) {
        Location origin = rig.origin();
        double limit = Math.cos(Math.toRadians(degrees));
        Player best = null;
        double bestDistance = Double.MAX_VALUE;
        for (Player player : origin.getWorld().getPlayers()) {
            if (player.isDead() || player.getGameMode().name().equals("SPECTATOR")) {
                continue;
            }
            Vector toward = player.getLocation().toVector().subtract(origin.toVector());
            toward.setY(0);
            double distance = toward.length();
            if (distance < 0.01 || distance >= bestDistance) {
                continue;
            }
            if (toward.multiply(1 / distance).dot(direction) < limit) {
                continue;
            }
            best = player;
            bestDistance = distance;
        }
        return best;
    }

    /** 跳躍を進める。水平は等速、垂直は放物線を描いて戦場の中心へ着地する。 */
    private void runLeap(MotionSpec.Leap leap, int tick) {
        if (leapFrom == null || leapTo == null) {
            return;
        }
        if (tick == leap.startTick()) {
            sound("entity.ravager.roar", 1.2f, 1.4f);
            particles(Particle.EXPLOSION, rig.origin(), 6, 0.8);
        }
        if (tick <= leap.startTick() || tick > leap.landingTick()) {
            return;
        }
        int since = tick - leap.startTick();
        double progress = leap.progress(since);
        Location next = leapFrom.clone();
        next.setX(leapFrom.getX() + (leapTo.getX() - leapFrom.getX()) * progress);
        next.setZ(leapFrom.getZ() + (leapTo.getZ() - leapFrom.getZ()) * progress);
        next.setY(leapFrom.getY() + (leapTo.getY() - leapFrom.getY()) * progress
                + leap.archHeight(since));
        rig.moveTo(next);
        if (since % 4 == 0) {
            particles(trailParticle(), rig.origin().add(0, 0.5, 0), 3, 0.3);
        }
        if (tick == leap.landingTick()) {
            rig.moveTo(leapTo.clone());
            waveCenter = leapTo.clone();
            sound("entity.generic.explode", 1.8f, 0.6f);
            sound("block.anvil_land", 1.6f, 0.5f);
            particles(Particle.EXPLOSION_EMITTER, leapTo.clone().add(0, 0.5, 0), 3, 0.8);
        }
    }

    /** 広がる衝撃波。着地からの経過に応じて外へ伝わる。 */
    private void expandingWave(MotionSpec.AreaEffect area, int since) {
        if (waveCenter == null) {
            waveCenter = rig.origin();
        }
        double inner = area.radiusAt(since - 1);
        double outer = area.radiusAt(since);
        for (double degrees = 0; degrees < 360; degrees += 6) {
            double radians = Math.toRadians(degrees);
            Location edge = waveCenter.clone().add(Math.cos(radians) * outer,
                    area.heightBlocks(), Math.sin(radians) * outer);
            waveCenter.getWorld().spawnParticle(Particle.SWEEP_ATTACK, edge, 1, 0, 0, 0, 0);
        }
        for (Player player : waveCenter.getWorld().getPlayers()) {
            Location at = player.getLocation();
            if (!stage.contains(at.getX(), at.getZ())) {
                continue;
            }
            double distance = at.toVector().setY(waveCenter.getY())
                    .distance(waveCenter.toVector());
            if (distance <= inner || distance > outer) {
                continue;
            }
            if (!struck.add(player.getUniqueId())) {
                continue;
            }
            hit(player, area.damage(), ShieldGuard.GUARDS_AREA_EFFECTS);
        }
    }

    // ------------------------------------------------------------ 攻撃

    private void applyWindow(int index, MotionSpec.DamageWindow window) {
        List<Location> weapon = rig.hitPointsOf(window.part());
        if (weapon.isEmpty()) {
            return;
        }
        if (stateTick % 2 == 0) {
            swingEffect(weapon);
        }
        Set<UUID> alreadyHit = struckByWindow.computeIfAbsent(index, key -> new HashSet<>());
        for (Player player : rig.origin().getWorld().getPlayers()) {
            if (player.isDead() || player.getGameMode().name().equals("SPECTATOR")) {
                continue;
            }
            Location at = player.getLocation();
            if (!stage.contains(at.getX(), at.getZ())) {
                continue;
            }
            if (!withinWeapon(weapon, at)) {
                continue;
            }
            if (!alreadyHit.add(player.getUniqueId())) {
                continue;
            }
            hit(player, window.damage(), true);
        }
    }

    private boolean withinWeapon(List<Location> weapon, Location at) {
        for (Location point : weapon) {
            double dx = point.getX() - at.getX();
            double dz = point.getZ() - at.getZ();
            double dy = Math.max(0, Math.max(at.getY() - point.getY(),
                    point.getY() - (at.getY() + tuning.playerHeight())));
            if (dx * dx + dy * dy + dz * dz <= tuning.weaponReach() * tuning.weaponReach()) {
                return true;
            }
        }
        return false;
    }

    /** @param guardable 盾で止められるか。武器の判定区間は true、衝撃波は false */
    private void hit(Player target, MotionSpec.Damage damage, boolean guardable) {
        applyDamage(target, damage, guardable);
        playHitEffect(target.getLocation());
        particles(Particle.CRIT, target.getLocation().add(0, 1, 0), 12, 0.3);
    }

    private void applyDamage(Player target, MotionSpec.Damage damage, boolean guardable) {
        double amount = roll(damage) * rage.damageMultiplier();
        if (guardable && guarding(target)) {
            wearShield(target, amount);
            playAt(target.getLocation(), "item.shield.block", 1.0f, 0.9f);
            amount = ShieldGuard.damageThrough(amount);
        }
        if (amount > 0) {
            target.damage(amount);
        }
        landedThisMotion = true;
        rage.landedHit();
        knockback(target);
    }

    private boolean guarding(Player target) {
        if (!target.isBlocking()) {
            return false;
        }
        Location at = target.getLocation();
        Location source = rig.origin();
        Vector view = at.getDirection();
        return ShieldGuard.facing(view.getX(), view.getZ(),
                source.getX() - at.getX(), source.getZ() - at.getZ());
    }

    private void wearShield(Player target, double amount) {
        ItemStack shield = shieldInHand(target);
        if (shield == null) {
            return;
        }
        int cost = ShieldGuard.afterUnbreaking(ShieldGuard.durabilityCost(amount),
                unbreakingLevel(shield), random);
        if (cost <= 0) {
            return;
        }
        if (!(shield.getItemMeta() instanceof Damageable meta)) {
            return;
        }
        int worn = meta.getDamage() + cost;
        if (worn >= shield.getType().getMaxDurability()) {
            shield.setAmount(0);
            playAt(target.getLocation(), "item.shield.break", 1.0f, 1.0f);
            return;
        }
        meta.setDamage(worn);
        shield.setItemMeta(meta);
    }

    private ItemStack shieldInHand(Player target) {
        ItemStack offHand = target.getInventory().getItemInOffHand();
        if (offHand != null && offHand.getType() == Material.SHIELD) {
            return offHand;
        }
        ItemStack mainHand = target.getInventory().getItemInMainHand();
        if (mainHand != null && mainHand.getType() == Material.SHIELD) {
            return mainHand;
        }
        return null;
    }

    private static int unbreakingLevel(ItemStack shield) {
        for (Map.Entry<Enchantment, Integer> entry : shield.getEnchantments().entrySet()) {
            if (entry.getKey().getKey().getKey().equals("unbreaking")) {
                return entry.getValue();
            }
        }
        return 0;
    }

    private static void playAt(Location at, String key, float volume, float pitch) {
        at.getWorld().playSound(at, "minecraft:" + key, volume, pitch);
    }

    /** 押し出す。技ごとの押し出しを持つ技では、{@link Tuning#baseKnockback()} の上に足す。 */
    private void knockback(Player target) {
        Vector away = target.getLocation().toVector().subtract(rig.origin().toVector());
        away.setY(0);
        if (away.lengthSquared() < 0.0001) {
            away = new Vector(0, 0, 1);
        }
        away.normalize();

        double back = tuning.baseKnockback();
        double up = 0;
        if (motion != null && motion.knockback().isPresent()) {
            MotionSpec.Knockback declared = motion.knockback().get();
            back += declared.backBlocks() / 5.0;
            up = declared.upBlocks() / 5.0;
        }
        Vector next = target.getVelocity().add(away.multiply(back));
        if (up > 0) {
            next.setY(up);
        }
        target.setVelocity(next);
    }

    // ------------------------------------------------------------ 特殊系統（浮遊武器）の下請け
    //
    // 「降り注ぐ刃」「空間斬撃」「串刺し」「全域大旋回」（`raid_species.md` §2）は、
    // BossRig の部位ツリーに属さない独立した実体（FloatingBlade 群）として動く。
    // ここから下は、その独立した攻撃源のために MotionSpec に依らない経路を提供する。
    // 個体側（HollowGuardBoss）は tickSpecial() をオーバーライドして駆動する。

    /** 特殊系統を駆動する余地があるか（種によって使わない）。既定では何もしない。 */
    protected void tickSpecial() {
    }

    /** いまの段階。特殊系統の解禁判定（第二形態から）などに使う。 */
    RaidSpecies.Phase currentPhase() {
        return phase;
    }

    /** 戦場。旋回の軸（戦場の中心）などに使う。 */
    Stage stage() {
        return stage;
    }

    /** 指定位置の地表面のY座標。 */
    double groundY(Location at) {
        return grounded(at).getY();
    }

    /** 戦場の内側にいる、有効なプレイヤーの一覧。特殊系統の狙う相手選びに使う。 */
    List<Player> playersInStage() {
        List<Player> found = new ArrayList<>();
        for (Player player : rig.origin().getWorld().getPlayers()) {
            if (player.isDead() || player.getGameMode().name().equals("SPECTATOR")) {
                continue;
            }
            Location at = player.getLocation();
            if (stage.contains(at.getX(), at.getZ())) {
                found.add(player);
            }
        }
        return found;
    }

    /** 特殊モーションが来ることを告げる（実体の {@code playMotionStartEffect} に相当）。 */
    void announceMotion(String motionName) {
        announce("§e特殊 " + motionName + " が来る");
    }

    /** 戦場の内側にいる、指定位置から半径以内のプレイヤー。 */
    List<Player> playersInRange(Location center, double radius) {
        List<Player> found = new ArrayList<>();
        for (Player player : center.getWorld().getPlayers()) {
            if (player.isDead() || player.getGameMode().name().equals("SPECTATOR")) {
                continue;
            }
            Location at = player.getLocation();
            if (!stage.contains(at.getX(), at.getZ())) {
                continue;
            }
            double dy = Math.max(0, Math.max(at.getY() - center.getY(),
                    center.getY() - (at.getY() + tuning.playerHeight())));
            double dx = at.getX() - center.getX();
            double dz = at.getZ() - center.getZ();
            if (dx * dx + dy * dy + dz * dz <= radius * radius) {
                found.add(player);
            }
        }
        return found;
    }

    /**
     * 個体の技（{@link MotionSpec}）に依らない一撃。浮遊武器のように、個体の姿勢と
     * 無関係に動く攻撃源から当てるときに使う。押し出す向きは {@code source} から見た向き。
     */
    void independentHit(Player target, Location source, double damage, double backBlocks,
                        double upBlocks, boolean guardable) {
        double amount = damage * rage.damageMultiplier();
        if (guardable && guarding(target)) {
            wearShield(target, amount);
            playAt(target.getLocation(), "item.shield.block", 1.0f, 0.9f);
            amount = ShieldGuard.damageThrough(amount);
        }
        if (amount > 0) {
            target.damage(amount);
        }
        rage.landedHit();
        knockbackFrom(target, source, backBlocks, upBlocks);
        playHitEffect(target.getLocation());
        particles(Particle.CRIT, target.getLocation().add(0, 1, 0), 12, 0.3);
    }

    private void knockbackFrom(Player target, Location source, double declaredBack,
                               double declaredUp) {
        Vector away = target.getLocation().toVector().subtract(source.toVector());
        away.setY(0);
        if (away.lengthSquared() < 0.0001) {
            away = new Vector(0, 0, 1);
        }
        away.normalize();
        double back = tuning.baseKnockback() + declaredBack / 5.0;
        double up = declaredUp / 5.0;
        Vector next = target.getVelocity().add(away.multiply(back));
        if (up > 0) {
            next.setY(up);
        }
        target.setVelocity(next);
    }

    /** 円形の衝撃波。個体の技とは独立した攻撃源から起こす（浮遊剣の着地など）。 */
    void shockwaveAt(Location center, double radius, double height, double damage) {
        sound("block.anvil_land", 1.4f, 0.7f);
        particles(Particle.EXPLOSION, center, 6, 1.0);
        for (double angle = 0; angle < 360; angle += 8) {
            double radians = Math.toRadians(angle);
            Location edge = center.clone().add(Math.cos(radians) * radius, height,
                    Math.sin(radians) * radius);
            center.getWorld().spawnParticle(trailParticle(), edge, 1, 0, 0, 0, 0);
        }
        for (Player player : playersInRange(center, radius)) {
            independentHit(player, center, damage, 0, 0.2, ShieldGuard.GUARDS_AREA_EFFECTS);
        }
    }

    private void shockwave(MotionSpec.AreaEffect area) {
        Location center = rig.origin();
        sound("block.anvil_land", 1.6f, 0.6f);
        particles(Particle.EXPLOSION, center, 8, 1.5);
        for (double angle = 0; angle < 360; angle += 6) {
            double radians = Math.toRadians(angle);
            Location edge = center.clone().add(Math.cos(radians) * area.radiusBlocks(), 0.2,
                    Math.sin(radians) * area.radiusBlocks());
            center.getWorld().spawnParticle(trailParticle(), edge, 1, 0, 0, 0, 0);
        }
        for (Player player : center.getWorld().getPlayers()) {
            if (player.getLocation().distance(center) <= area.radiusBlocks()) {
                applyDamage(player, area.damage(), ShieldGuard.GUARDS_AREA_EFFECTS);
            }
        }
    }

    @Override
    public List<String> describe() {
        List<String> lines = new ArrayList<>();
        lines.add("状態 " + state + " tick " + stateTick + " / モーション "
                + (motion == null ? "なし" : motion.name()) + " / 体の向き "
                + String.format("%.1f", bodyYaw) + "度");
        lines.addAll(rig.describe());
        return lines;
    }

    @Override
    public boolean handleHit(UUID hitEntity, Player attacker, Location origin, boolean ranged,
                             Material weapon) {
        String part = rig.partOfHitbox(hitEntity);
        if (part == null) {
            return false;
        }
        if (origin.getWorld() != rig.origin().getWorld()
                || origin.distance(rig.origin()) > tuning.attackRangeBlocks()) {
            attacker.sendMessage("§7遠すぎる攻撃は通らない（"
                    + (int) tuning.attackRangeBlocks() + " ブロック以内から）");
            sound("entity.zombie.attack_iron_door", 0.6f, 1.9f);
            return true;
        }
        if (WeaponDamage.rejected(weapon)) {
            attacker.sendMessage("§7その武器は通らない");
            sound("entity.zombie.attack_iron_door", 0.8f, 1.7f);
            return true;
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
        contribution.merge(attacker.getUniqueId(), result.dealt(), Double::sum);
        accumulateParry(result.dealt(), attacker);

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

    /** パリイの判定。盾では成立しない。区間に与えた累積ダメージが閾値に達した時点で成立する。 */
    private void accumulateParry(double dealt, Player attacker) {
        if (motion == null || state != State.MOTION || interrupted) {
            return;
        }
        MotionSpec.Parry parry = motion.parry().orElse(null);
        if (parry == null || !parry.covers(stateTick)) {
            return;
        }
        double required = parry.requiredDamage(parryCount);
        parryDamage += dealt;
        if (parryDamage < required) {
            attacker.sendActionBar(Component.text(String.format("パリイまで %.0f / %.0f",
                    Math.max(0, required - parryDamage), required)));
            return;
        }
        interrupted = true;
        parryCount++;
        parts.expose(PartTracker.EXPOSURE_TICKS);
        announce(String.format("§bパリイ成功（%d回目） — %s を止めた（弱点が露出）"
                + " / 次は %.0f 必要", parryCount, motion.name(),
                parry.requiredDamage(parryCount)));
        sound("item.shield.block", 1.4f, 0.8f);
        sound("block.anvil_land", 1.0f, 1.8f);
        String flashPart = motion.damageWindows().isEmpty()
                ? null : motion.damageWindows().get(0).part();
        particles(Particle.CRIT, flashPart == null ? rig.origin() : rig.centerOf(flashPart), 40, 0.6);
        particles(Particle.FLASH, rig.origin().add(0, 1.5, 0), 1, 0);
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
            weakPointPart().ifPresent(part -> particles(Particle.END_ROD, rig.centerOf(part), 25, 0.4));
        }
    }

    /**
     * 露出する弱点の部位名。骨格から{@code ON_EXPOSURE}の弱点を探す
     * （騎士型の「頭」を決め打ちにしていた箇所の一般化）。持たない種では空。
     */
    private java.util.Optional<String> weakPointPart() {
        Rig underlying = rig.rig();
        for (String name : underlying.partNames()) {
            Rig.Part part = underlying.part(name);
            if (part.isWeakPoint() && part.gate() == Rig.Gate.ON_EXPOSURE) {
                return java.util.Optional.of(name);
            }
        }
        return java.util.Optional.empty();
    }

    private void onEnrage() {
        announce("§4激昂した — 待機が縮み、弱点が閉じる");
        playEnrageEffect();
        title("§4激 昂", "§7弱点が閉じた。前に出て捌け");
    }

    private void updateBar() {
        double progress = Math.max(0, Math.min(1, health / maxHealth));
        bar.setProgress(progress);
        StringBuilder title = new StringBuilder(
                "§f" + species.displayName() + " §7— " + phase.name());
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

    private void playMotionStartEffect() {
        sound(motionSound(motion.name()), 1.3f, 1.0f);
    }

    // ------------------------------------------------------------ 段階

    private void checkPhase() {
        int percent = (int) Math.ceil(health * 100 / maxHealth);
        RaidSpecies.Phase current = species.phaseAt(Math.max(0, Math.min(100, percent)));
        if (current == phase) {
            return;
        }
        RaidSpecies.Phase previous = phase;
        phase = current;
        idleTarget = phase.behavior().idleTicks();
        selector.reset();
        rage.reset();
        wasExposed = false;
        Location origin = rig.origin();
        rig.despawn();
        rig = new BossRig(species.rigFor(phase), origin);
        parts = new PartTracker(species.rigFor(phase));
        rig.spawn();
        lastPose = new HashMap<>();
        transition.clear();
        enter(State.IDLE);
        announce("§5" + phase.name() + " へ移行 — " + phase.gimmick());
        title("§5" + phase.name(), "§7" + species.displayName() + "の姿が変わった");
        sound("entity.ender_dragon.growl", 1.6f, 0.8f);
        particles(Particle.EXPLOSION_EMITTER, origin.clone().add(0, 1.5, 0), 4, 1.0);
        particles(Particle.SOUL_FIRE_FLAME, origin.clone().add(0, 1.5, 0), 60, 1.5);
        onPhaseTransition(previous, phase);
    }

    /**
     * 段階が移行した直後に呼ばれる。既定では何もしない。
     *
     * <p>虚刃の衛士はこれをオーバーライドし、移行のたびに全域大旋回を1回発動する
     * （`raid_species.md` §2「移行トリガー」）。
     */
    protected void onPhaseTransition(RaidSpecies.Phase from, RaidSpecies.Phase to) {
    }

    // ------------------------------------------------------------ 補助

    private void step(Vector direction, double blocks) {
        if (direction == null || blocks <= 0) {
            return;
        }
        Location origin = rig.origin();
        rig.moveTo(grounded(origin.add(direction.clone().multiply(blocks))));
    }

    private Vector forwardDirection() {
        Player target = nearest();
        Location origin = rig.origin();
        if (target == null) {
            return new Vector(0, 0, 1);
        }
        Vector direction = target.getLocation().toVector().subtract(origin.toVector());
        direction.setY(0);
        if (direction.lengthSquared() < 0.01) {
            return new Vector(0, 0, 1);
        }
        return direction.normalize();
    }

    private Vector backwardDirection() {
        return forwardDirection().multiply(-1);
    }

    /** 回旋の1tick。回るのは戦場の外周であり、プレイヤーの周りではない。 */
    private void orbitStep(MotionSpec.Orbit orbit) {
        double radius = orbit.diameterBlocks() / 2;
        double angle = orbitStartAngle + orbit.angleAfter(stateTick);
        double entryTicks = Math.max(1,
                Math.abs(radius - orbitStartRadius) / orbit.speedAfter(0));
        double entry = Math.min(1.0, stateTick / entryTicks);
        double current = orbitStartRadius + (radius - orbitStartRadius) * entry;
        Location next = rig.origin().clone();
        next.setX(stage.centerX() + Math.cos(angle) * current);
        next.setZ(stage.centerZ() + Math.sin(angle) * current);
        rig.moveTo(grounded(next));
    }

    /** 足元の地面に合わせた位置を返す。地形が無ければ元の高さを保つ。 */
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

    /** 最も近いプレイヤー。戦場の内側にいる者だけを狙う。 */
    private Player nearest() {
        Location origin = rig.origin();
        Player closest = null;
        double best = Double.MAX_VALUE;
        for (Player player : origin.getWorld().getPlayers()) {
            if (player.isDead() || player.getGameMode().name().equals("SPECTATOR")) {
                continue;
            }
            Location at = player.getLocation();
            if (!stage.contains(at.getX(), at.getZ())) {
                continue;
            }
            double distance = at.distanceSquared(origin);
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

    private int surrounding() {
        Location origin = rig.origin();
        int count = 0;
        for (Player player : origin.getWorld().getPlayers()) {
            if (player.isDead() || player.getGameMode().name().equals("SPECTATOR")) {
                continue;
            }
            Location at = player.getLocation();
            if (stage.contains(at.getX(), at.getZ())
                    && at.distance(origin) <= MotionSpec.Usage.CROWD_RADIUS) {
                count++;
            }
        }
        return count;
    }

    /**
     * 体の向き（度）。突進中は走っている方向を向く。待機中だけ、狙う位置を
     * {@link Tuning#idleTrackingDelayTicks()} だけ遅らせる（{@link TrackingDelay}）。
     */
    private double yawToTarget() {
        if (chargeDirection != null) {
            return Math.toDegrees(Math.atan2(-chargeDirection.getX(), chargeDirection.getZ()));
        }
        Player target = nearest();
        if (target == null) {
            return bodyYaw;
        }
        Location origin = rig.origin();
        boolean delayed = state == State.IDLE && tracking.has();
        double dx = (delayed ? tracking.x() : target.getLocation().getX()) - origin.getX();
        double dz = (delayed ? tracking.z() : target.getLocation().getZ()) - origin.getZ();
        if (dx * dx + dz * dz < 1.0) {
            return bodyYaw;
        }
        return Math.toDegrees(Math.atan2(-dx, dz));
    }

    private void trackTarget() {
        Player target = nearest();
        if (target == null) {
            trackedTarget = null;
            tracking.reset();
            return;
        }
        if (!target.getUniqueId().equals(trackedTarget)) {
            trackedTarget = target.getUniqueId();
            tracking.reset();
        }
        Location at = target.getLocation();
        tracking.push(at.getX(), at.getZ());
    }

    private double turnToward(double target) {
        double delta = normalizeDegrees(target - bodyYaw);
        double step = Math.max(-tuning.maxTurnDegrees(),
                Math.min(tuning.maxTurnDegrees(), delta));
        bodyYaw = normalizeDegrees(bodyYaw + step);
        return bodyYaw;
    }

    private static double normalizeDegrees(double degrees) {
        double folded = degrees % 360;
        if (folded > 180) {
            folded -= 360;
        }
        if (folded < -180) {
            folded += 360;
        }
        return folded;
    }

    private double roll(MotionSpec.Damage damage) {
        return damage.random()
                ? damage.min() + Math.random() * (damage.max() - damage.min())
                : damage.min();
    }

    // ------------------------------------------------------------ 演出の下請け

    private void announce(String message) {
        for (Player player : rig.origin().getWorld().getPlayers()) {
            player.sendMessage("[" + tuning.announceTag() + "] " + message);
        }
    }

    private void title(String main, String sub) {
        for (Player player : rig.origin().getWorld().getPlayers()) {
            player.sendTitle(main, sub, 5, 30, 10);
        }
    }

    /** 音は名前で鳴らす。列挙の改名に左右されない。 */
    void sound(String key, float volume, float pitch) {
        Location origin = rig.origin();
        origin.getWorld().playSound(origin, "minecraft:" + key, volume, pitch);
    }

    void particles(Particle particle, Location location, int count, double spread) {
        location.getWorld().spawnParticle(particle, location, count, spread, spread, spread, 0);
    }

    private void swingEffect(List<Location> weapon) {
        for (Location point : weapon) {
            particles(boundaryParticle(), point, 2, 0.15);
        }
        if (!weapon.isEmpty()) {
            particles(Particle.SWEEP_ATTACK, weapon.get(weapon.size() - 1), 2, 0.2);
        }
    }

    /** 突進・回旋の足跡。 */
    private void trail() {
        Location origin = rig.origin();
        particles(trailParticle(), origin.clone().add(0, 0.1, 0), 4, 0.3);
    }

    @Override
    public void playDefeat() {
        playDefeatEffect();
        title("§6討 伐", "§7" + species.displayName() + "を倒した");
    }

    Location origin() {
        return rig.origin();
    }

    // ------------------------------------------------------------ 種ごとの効果音・色

    /** 召喚したときの音とパーティクル。 */
    protected abstract void playSpawnEffect();

    /** 歩いているあいだの足音。 */
    protected abstract void playStepEffect();

    /** 落下して着地したときの音とパーティクル。 */
    protected abstract void playLandEffect();

    /** 攻撃が当たったときの音。 */
    protected abstract void playHitEffect(Location targetLocation);

    /** 突進・前進を始めた瞬間の音。 */
    protected abstract void playChargeStartEffect();

    /** 激昂したときの音とパーティクル。 */
    protected abstract void playEnrageEffect();

    /** 討伐されたときの音とパーティクル。 */
    protected abstract void playDefeatEffect();

    /** 武器の軌跡・戦場境界に使うパーティクル。 */
    protected abstract Particle boundaryParticle();

    /** 移動の足跡に使うパーティクル。 */
    protected abstract Particle trailParticle();

    /** モーション開始時に鳴らす音（モーション名で振り分ける）。 */
    protected abstract String motionSound(String motionName);
}
