package jp.mcserver.plugin;

import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import jp.mcserver.core.Raid;
import jp.mcserver.core.raid.Animation;
import jp.mcserver.core.raid.KnightDefinition;
import jp.mcserver.core.raid.MotionSelector;
import jp.mcserver.core.raid.MotionSpec;
import jp.mcserver.core.raid.PartTracker;
import jp.mcserver.core.raid.PoseTransition;
import jp.mcserver.core.raid.RageMeter;
import jp.mcserver.core.raid.RaidSpecies;
import jp.mcserver.core.raid.Stage;
import jp.mcserver.core.raid.Transform;
import net.kyori.adventure.text.Component;
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
 *
 * <p>パリイは盾では成立しない。<b>その区間に個体へ与えた累積ダメージ</b>で判定する（§12.6）。
 * 突進は走り出したらパリイされない限り止まらず、決めた距離を走り切る。
 */
final class KnightBoss {

    /**
     * 武器の周りに取る判定の余裕（ブロック）。
     *
     * <p>間合いは<b>足元からではなく武器そのものから</b>測る。槍は3.4ブロックあり、
     * 足元からの距離で測ると間合いが武器の長さぶん短くなる。
     * ここで足すのは、プレイヤーの体の太さと当たりやすさのぶんである。
     */
    private static final double WEAPON_REACH = 1.8;

    /** 接地を探す高さの範囲（ブロック）。段差と坂を登り、崖では落ちる。 */
    private static final int GROUND_UP = 3;
    private static final int GROUND_DOWN = 8;

    /**
     * 帰還の歩行を1回あたり何tickまで続けるか（§12.6）。
     *
     * <p>上限を超えても中心に着かない場合は<b>攻撃モーションを挟む</b>。
     * 歩いて戻るだけの時間が長く続くと、戦闘が止まって見えるためである。
     */
    private static final int RETURN_WALK_MIN_TICKS = 20;
    private static final int RETURN_WALK_MAX_TICKS = 40;

    private enum State { IDLE, APPROACH, MOTION, RETURN }

    private final RaidPlugin plugin;
    private final RaidSpecies species;
    private final long maxHealth;
    private final int participants;
    private final MotionSelector selector = new MotionSelector();
    private final RageMeter rage = new RageMeter();
    /** モーションの切り替わりを埋めるつなぎ（§12.6） */
    private final PoseTransition transition = new PoseTransition();
    /** 戦場。召喚位置の x, z を中心とした半径30ブロックの円筒（§12.6） */
    private final Stage stage;
    /** 戦場の外へ出るたびに負う「中心を経由する」義務 */
    private final Stage.CenterVisit centerVisit = new Stage.CenterVisit();
    /** 戦場の中心の足元。境界の描画に使う */
    private final Location stageCenter;
    /** 直前に適用した姿勢。つなぎの起点になる */
    private Map<String, Transform> lastPose = new HashMap<>();
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
    /** 判定区間ごとに、すでに当てたプレイヤー。区間の中で二重に当てない */
    private final Map<Integer, Set<UUID>> struckByWindow = new HashMap<>();
    private boolean interrupted;
    private boolean landedThisMotion;
    private boolean wasExposed;
    /** 突進で走った距離。決めた距離を走り切るまで止まらない */
    private double chargeTravelled;
    /** 突進の向き。走り出した時点で固定する。追尾させると避けられない */
    private Vector chargeDirection;
    /** パリイの区間に与えられた累積ダメージ */
    private double parryDamage;
    /** その戦闘で成功したパリイの回数。成功するたびに次の必要量が増える（§12.6） */
    private int parryCount;
    /** この突進ですでに当てたプレイヤー。走り抜けても二重に当てない */
    private final Set<UUID> struck = new HashSet<>();
    /** 待機の長さや帰還の歩行時間を選ぶ乱数 */
    private final java.util.Random random = new java.util.Random();
    /** 今回の帰還で歩き続ける tick。これを超えたら攻撃モーションを挟む */
    private int returnWalkTarget;
    /** 跳躍の始点と着地点 */
    private Location leapFrom;
    private Location leapTo;
    /** 広がる衝撃波の中心 */
    private Location waveCenter;

    KnightBoss(RaidPlugin plugin, Location origin) {
        this.plugin = plugin;
        this.species = KnightDefinition.boss();
        Location spawn = grounded(origin);
        this.stage = new Stage(spawn.getX(), spawn.getZ());
        this.stageCenter = spawn.clone();
        // 参加人数は戦場の内側にいる者で数える。外の見物人で体力が増えては困る
        this.participants = Math.max(1, (int) spawn.getWorld().getPlayers().stream()
                .filter(player -> stage.contains(player.getLocation().getX(),
                        player.getLocation().getZ()))
                .count());
        this.maxHealth = species.healthFor(Math.min(participants, Raid.MAX_PARTICIPANTS));
        this.health = maxHealth;
        this.phase = species.phaseAt(100);
        this.rig = new BossRig(species.rigFor(phase), spawn);
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
        Location here = rig.origin();
        text.append(String.format(" / 足元 Y %.1f / 中心から %.1f（半径 %.0f）",
                here.getY(), stage.distanceFromCenter(here.getX(), here.getZ()),
                stage.radius()));
        if (centerVisit.owed()) {
            text.append(" / 中心へ帰還中");
        }
        if (motion != null && state == State.MOTION) {
            motion.charge().ifPresent(run -> text.append(String.format(
                    " / 突進 %.1f / %.0f ブロック", chargeTravelled, run.distanceBlocks())));
            motion.parry().ifPresent(parry -> text.append(String.format(
                    " / パリイ %.0f / %.0f（成功 %d回）", parryDamage,
                    parry.requiredDamage(parryCount), parryCount)));
        }
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

        Location here = rig.origin();
        boolean wasOwed = centerVisit.owed();
        centerVisit.observe(stage, here.getX(), here.getZ());
        if (!wasOwed && centerVisit.owed()) {
            announce("§7騎士が戦場の外へ出た — 中心へ戻る");
        }
        if (totalTick % 20 == 0) {
            drawBoundary();
        }

        switch (state) {
            case IDLE -> {
                animateLoop(phase.behavior().idleAnimation().orElse(null));
                if (stateTick >= idleTarget) {
                    // 戦場の外へ出ていたら、暴れる前に中心を経由する（§12.6）
                    enter(centerVisit.owed() ? State.RETURN : State.APPROACH);
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
            case RETURN -> returnToCenter();
            case MOTION -> runMotion();
            default -> { }
        }
    }

    /**
     * 状態を切り替える。姿勢が飛ばないよう、直前の姿勢からのつなぎを始める（§12.6）。
     */
    private void enter(State next) {
        state = next;
        stateTick = 0;
        transition.begin(lastPose);
        if (next == State.RETURN) {
            returnWalkTarget = RETURN_WALK_MIN_TICKS
                    + random.nextInt(RETURN_WALK_MAX_TICKS - RETURN_WALK_MIN_TICKS + 1);
        }
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
        applyPose(sampled);
    }

    /**
     * 姿勢を適用する。切り替わりの直後はつなぎを通し、前の姿勢から混ぜて渡す。
     *
     * <p>これがないと、攻撃モーションの最終姿勢から待機の姿勢へ次の更新で一気に飛ぶ。
     */
    private void applyPose(Map<String, Transform> sampled) {
        Map<String, Transform> pose = transition.apply(sampled, BossRig.UPDATE_INTERVAL);
        lastPose = new HashMap<>(pose);
        rig.applyMotion(pose, yawToTarget());
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

    /**
     * 中心へ歩いて戻る（§12.6）。
     *
     * <p>瞬間移動はしない。歩いて戻るあいだは攻撃モーションを取らないため、
     * <b>突進を釣って避けることが、そのまま反撃の時間になる</b>。
     */
    private void returnToCenter() {
        animateLoop(phase.behavior().walkAnimation().orElse(null));
        Location here = rig.origin();
        Vector direction = new Vector(stage.centerX() - here.getX(), 0,
                stage.centerZ() - here.getZ());
        if (direction.lengthSquared() < 0.01 || !centerVisit.owed()) {
            announce("§7騎士が戦場の中心へ戻った");
            enter(State.APPROACH);
            return;
        }
        // 歩き続けるのは上限まで。着かなければ攻撃モーションを挟む（§12.6）
        if (stateTick >= returnWalkTarget) {
            startMotion();
            return;
        }
        step(direction.normalize(), phase.behavior().blocksPerTick());
        if (stateTick % 8 == 0) {
            sound("entity.iron_golem.step", 0.7f, 0.8f);
        }
    }

    /** 戦場の境界を粒子で示す。どこから撃っても通らないかを目で分かるようにする。 */
    private void drawBoundary() {
        for (int degrees = 0; degrees < 360; degrees += 10) {
            double radians = Math.toRadians(degrees);
            Location edge = stageCenter.clone().add(Math.cos(radians) * stage.radius(), 0.4,
                    Math.sin(radians) * stage.radius());
            stageCenter.getWorld().spawnParticle(Particle.END_ROD, edge, 1, 0, 0, 0, 0);
        }
    }

    /** 状況に応じて次のモーションを選ぶ（§12.6）。 */
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
        motion.leap().ifPresent(leap -> {
            leapFrom = rig.origin();
            Location center = rig.origin();
            center.setX(stage.centerX());
            center.setZ(stage.centerZ());
            leapTo = grounded(center);
        });
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
            applyPose(sampled);
        }

        // 跳躍・突進・回旋の移動
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

        // ダメージ判定は区間のあいだ毎tick行う。
        // 開始tickだけで判定すると、そこはまだ振りかぶりの位置であり、
        // 実際に当たる瞬間（区間の終わり）を取りこぼす
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
            // 空振りは隙になる。突進を避けきったプレイヤーに弱点を開く（§12.6）
            if (!interrupted && !landedThisMotion && motion.charge().isPresent()) {
                parts.expose(PartTracker.WHIFF_EXPOSURE_TICKS);
                announce("§e" + motion.name() + " を空振りした — 弱点が露出");
                sound("block.beacon.activate", 1.0f, 1.6f);
            }
            // モーションが自前の待機を持つ場合はそれに従う。
            // 幅のある待機（大ジャンプ衝撃波の20〜60tickなど）は「必ず挟む」ものであり、
            // 激昂による短縮の対象にしない
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
            enter(State.IDLE);
        }
    }

    /**
     * 突進を進める。後ずさり → 加速 → 決めた距離を走り切る（§12.6）。
     *
     * <p>走り出した時点で向きを固定する。追尾させると避けようがなくなる。
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
            sound("entity.ravager.attack", 1.3f, 1.1f);
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

    /**
     * 跳躍を進める（§12.6）。水平は等速、垂直は放物線を描いて戦場の中心へ着地する。
     *
     * <p>滞空中は接地させない。着地の瞬間だけ地面へ合わせる。
     */
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
            particles(Particle.SOUL_FIRE_FLAME, rig.origin().add(0, 0.5, 0), 3, 0.3);
        }
        if (tick == leap.landingTick()) {
            rig.moveTo(leapTo.clone());
            waveCenter = leapTo.clone();
            sound("entity.generic.explode", 1.8f, 0.6f);
            sound("block.anvil_land", 1.6f, 0.5f);
            particles(Particle.EXPLOSION_EMITTER, leapTo.clone().add(0, 0.5, 0), 3, 0.8);
        }
    }

    /**
     * 広がる衝撃波（§12.6）。着地からの経過に応じて外へ伝わる。
     *
     * <p>距離があれば見てから逃げられ、近ければ間に合わない。即時の範囲攻撃と違い、
     * <b>立っている位置で猶予が変わる</b>。
     */
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
            hit(player, area.damage());
        }
    }

    // ------------------------------------------------------------ 攻撃

    /**
     * 判定区間を1tickぶん適用する。
     *
     * <p>当たるかは<b>その区間が指定した部位からの距離</b>で測る。槍の攻撃なら
     * 槍の全体が判定を持ち、穂先の側にいても当たる。
     * 同じ区間の中では、同じプレイヤーに二度当てない。
     */
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
            hit(player, window.damage());
        }
    }

    /**
     * 武器の並びのいずれかに届いているか。
     *
     * <p>プレイヤーの足元だけでなく<b>胴の高さでも</b>測る。槍は胸の高さを薙ぐため、
     * 足元との距離だけで測ると、頭上や足元をかすめた判定を取りこぼす。
     */
    private boolean withinWeapon(List<Location> weapon, Location at) {
        Location chest = at.clone().add(0, 1.0, 0);
        for (Location point : weapon) {
            if (point.distance(at) <= WEAPON_REACH || point.distance(chest) <= WEAPON_REACH) {
                return true;
            }
        }
        return false;
    }

    /** 1人に当てる。ダメージ・ノックバック・演出をまとめる。 */
    private void hit(Player target, MotionSpec.Damage damage) {
        double amount = roll(damage) * rage.damageMultiplier();
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
    boolean handleHit(UUID hitEntity, Player attacker, Location origin, boolean ranged) {
        String part = rig.partOfHitbox(hitEntity);
        if (part == null) {
            return false;
        }
        // 戦場の外から放たれた攻撃は通さない（§12.6）
        if (!stage.allowsAttackFrom(origin.getX(), origin.getZ())) {
            attacker.sendMessage(ranged
                    ? "§7戦場の外から放たれた攻撃は通らない（中心から半径 "
                            + (int) stage.radius() + " ブロック以内から撃つこと）"
                    : "§7戦場の外からの攻撃は通らない");
            sound("entity.zombie.attack_iron_door", 0.6f, 1.9f);
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

    /**
     * パリイの判定（§12.6）。盾では成立しない。
     *
     * <p>パリイの区間に与えた累積ダメージが閾値に達した時点で成立する。
     * 成立すると突進は止まり、弱点が露出する。
     */
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
        particles(Particle.CRIT, rig.centerOf("槍"), 40, 0.6);
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
        // パリイの回数は引き継ぐ。形態が変わっても「止め続けられない」制約は続く
        // 骨格が変わるので作り直す（§12.7 の形態変化）
        Location origin = rig.origin();
        rig.despawn();
        rig = new BossRig(species.rigFor(phase), origin);
        parts = new PartTracker(species.rigFor(phase));
        rig.spawn();
        // 骨格ごと入れ替わるため、前の形態の姿勢は引き継げない
        lastPose = new HashMap<>();
        transition.clear();
        enter(State.IDLE);
        announce("§5" + phase.name() + " へ移行 — " + phase.gimmick());
        title("§5" + phase.name(), "§7" + species.displayName() + "の姿が変わった");
        sound("entity.ender_dragon.growl", 1.6f, 0.8f);
        particles(Particle.EXPLOSION_EMITTER, origin.clone().add(0, 1.5, 0), 4, 1.0);
        particles(Particle.SOUL_FIRE_FLAME, origin.clone().add(0, 1.5, 0), 60, 1.5);
    }

    // ------------------------------------------------------------ 補助

    /** 指定の向きへ1歩進める。 */
    private void step(Vector direction, double blocks) {
        if (direction == null || blocks <= 0) {
            return;
        }
        Location origin = rig.origin();
        rig.moveTo(grounded(origin.add(direction.clone().multiply(blocks))));
    }

    /** 最も近いプレイヤーへの向き（水平・単位ベクトル）。 */
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

    /** 後ずさりの向き。 */
    private Vector backwardDirection() {
        return forwardDirection().multiply(-1);
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

    /**
     * 最も近いプレイヤー。<b>戦場の内側にいる者だけを狙う</b>。
     *
     * <p>外にいる者を追うと、際限なく引き離されて戦場が意味をなさなくなる。
     * 外に出た者には攻撃も通らないため、追う理由もない。
     */
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

    /** 近接圏内のプレイヤー数。なぎ払いや踏みつけの条件になる（§12.6）。 */
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
     * 体の向き（度）。突進中は<b>走っている方向</b>を向く。
     * 追尾させると、走りながら向きだけ変わって不自然になる。
     */
    private double yawToTarget() {
        if (chargeDirection != null) {
            return Math.toDegrees(Math.atan2(-chargeDirection.getX(), chargeDirection.getZ()));
        }
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

    /** 武器の軌跡。判定を持つ範囲をそのまま光らせるので、間合いが目で分かる。 */
    private void swingEffect(List<Location> weapon) {
        for (Location point : weapon) {
            particles(Particle.END_ROD, point, 2, 0.15);
        }
        if (!weapon.isEmpty()) {
            particles(Particle.SWEEP_ATTACK, weapon.get(weapon.size() - 1), 2, 0.2);
        }
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
