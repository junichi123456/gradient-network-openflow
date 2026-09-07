package jp.mcserver.plugin;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import jp.mcserver.core.raid.Animation;
import jp.mcserver.core.raid.HollowGuardDefinition;
import jp.mcserver.core.raid.MotionSelector;
import jp.mcserver.core.raid.MotionSpec;
import jp.mcserver.core.raid.PartTracker;
import jp.mcserver.core.raid.PoseTransition;
import jp.mcserver.core.raid.RageMeter;
import jp.mcserver.core.raid.RaidDrop;
import jp.mcserver.core.raid.RaidSpecies;
import jp.mcserver.core.raid.ShieldGuard;
import jp.mcserver.core.raid.Stage;
import jp.mcserver.core.raid.Transform;
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
 * 虚刃の衛士の挙動（`raid_species.md` §2）。<b>実体（本体の近接攻撃）だけを持つ一次実装。</b>
 *
 * <p>{@link KnightBoss} と同じ骨組み（待機→接近→技→向き直り）を使うが、以下を持たない。
 * <ul>
 *   <li>特殊系統（浮遊する剣・斧）— 独立した並行系統として別途実装する（§2「実装上の注意」）</li>
 *   <li>段階移行 — {@link HollowGuardDefinition} はいまのところ第一形態しか持たない</li>
 *   <li>跳躍・回旋・パリイ — 3つの実体モーション（投げ払い・アッパー・シールドマッシュ）は
 *       どれも持たない。前進は {@link MotionSpec.Charge} を使うが、追尾も中心通過の義務も無い</li>
 * </ul>
 *
 * <p>被弾・盾のガード・ドロップの資格・弱すぎる攻撃の拒否は {@link KnightBoss} と同じ規則を
 * 踏襲する。共通の規約（`raid_species.md` の「共通の規約」表）に従うためである。
 */
final class HollowGuardBoss implements RaidBoss {

    private static final double WEAPON_REACH = HollowGuardDefinition.WEAPON_REACH;

    private static final int TURN_MAX_TICKS = 40;
    private static final double TURN_TOLERANCE_DEGREES = 12.0;

    private static final double FALL_GRAVITY = 0.08;
    private static final double FALL_MAX = 1.5;

    private static final int GROUND_UP = 3;
    private static final int GROUND_DOWN = 8;

    private enum State { ENTER, IDLE, APPROACH, MOTION, TURN }

    private final RaidPlugin plugin;
    private final RaidSpecies species;
    private final long maxHealth;
    private final int participants;
    private final MotionSelector selector = new MotionSelector();
    private final RageMeter rage = new RageMeter();
    private final PoseTransition transition = new PoseTransition();
    private final Stage stage;
    private final Location stageCenter;
    private Map<String, Transform> lastPose = new HashMap<>();
    private final BossBar bar;

    private double health;
    private final RaidSpecies.Phase phase;
    private BossRig rig;
    private PartTracker parts;
    private BukkitTask task;

    private State state = State.IDLE;
    private int stateTick;
    private int totalTick;
    private int idleTarget;
    private MotionSpec motion;
    private final Map<Integer, Set<UUID>> struckByWindow = new HashMap<>();

    private double bodyYaw;
    private boolean interrupted;
    private boolean landedThisMotion;
    private boolean wasExposed;
    /** 前進で進んだ距離。決めた距離を進み切るまで止まらない */
    private double chargeTravelled;
    /** 前進の向き。始めた時点で固定する */
    private Vector chargeDirection;
    private final Set<UUID> struck = new HashSet<>();
    private final java.util.Random random = new java.util.Random();

    private final Map<UUID, Double> contribution = new LinkedHashMap<>();

    /** 落下の速さ（ブロック/tick）。登場のときだけ使う */
    private double fallSpeed;

    HollowGuardBoss(RaidPlugin plugin, Location origin) {
        this(plugin, origin, 0, false);
    }

    /**
     * 向きと登場の仕方を指定して出す。{@link KnightBoss} と同じ形の構成子である。
     *
     * @param facingYaw 体の向き（度）。0 が南、180 が北
     * @param dropIn    true なら渡された高さから自由落下して地表面に到達する
     */
    HollowGuardBoss(RaidPlugin plugin, Location origin, double facingYaw, boolean dropIn) {
        this.plugin = plugin;
        this.species = HollowGuardDefinition.boss();
        Location spawn = dropIn ? origin.clone() : grounded(origin);
        this.stage = new Stage(spawn.getX(), spawn.getZ());
        this.stageCenter = spawn.clone();
        this.participants = Math.max(1, (int) spawn.getWorld().getPlayers().stream()
                .filter(player -> stage.contains(player.getLocation().getX(),
                        player.getLocation().getZ()))
                .count());
        this.maxHealth = species.healthFor(Math.min(participants,
                jp.mcserver.core.Raid.MAX_PARTICIPANTS));
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

    @Override
    public String status() {
        StringBuilder text = new StringBuilder(String.format(
                "%s / %s / 体力 %.0f / %d（%s %dtick）", species.displayName(), phase.name(),
                health, maxHealth, state, stateTick));
        if (motion != null && state == State.MOTION) {
            text.append(" モーション ").append(motion.name());
        }
        text.append(rage.enraged()
                ? " 激昂 残り " + rage.remaining() + "tick"
                : " 激昂まで " + rage.untilEnrage() + "tick");
        Location here = rig.origin();
        text.append(String.format(" / 足元 Y %.1f / 中心から %.1f（半径 %.0f）",
                here.getY(), stage.distanceFromCenter(here.getX(), here.getZ()),
                stage.radius()));
        if (motion != null && state == State.MOTION) {
            motion.charge().ifPresent(run -> text.append(String.format(
                    " / 前進 %.1f / %.0f ブロック", chargeTravelled, run.distanceBlocks())));
        }
        return text.toString();
    }

    void spawn() {
        rig.spawn();
        updateBar();
        sound("entity.wither_skeleton.ambient", 1.2f, 0.6f);
        particles(Particle.SOUL_FIRE_FLAME, rig.origin().add(0, 1, 0), 20, 0.8);
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
        if (totalTick % 20 == 0) {
            drawBoundary();
        }

        switch (state) {
            case ENTER -> descend();
            case IDLE -> {
                animateLoop(phase.behavior().idleAnimation().orElse(null));
                if (stateTick >= idleTarget) {
                    enter(State.APPROACH);
                }
            }
            case APPROACH -> {
                boolean inRange = approach();
                animateLoop(inRange
                        ? phase.behavior().idleAnimation().orElse(null)
                        : phase.behavior().walkAnimation().orElse(null));
                if (!inRange && stateTick % 8 == 0) {
                    sound("entity.wither_skeleton.step", 0.7f, 0.8f);
                }
                if (inRange || stateTick >= phase.behavior().approachTicks()) {
                    startMotion();
                }
            }
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

    /**
     * 相手へ歩み寄る。密着までは踏み込まない
     * （{@link HollowGuardDefinition#STANDOFF_BLOCKS} で止まり、そこから技を出す）。
     */
    private boolean approach() {
        Player target = nearest();
        if (target == null) {
            return false;
        }
        Location origin = rig.origin();
        Vector direction = target.getLocation().toVector().subtract(origin.toVector());
        direction.setY(0);
        double distance = direction.length();
        if (distance <= HollowGuardDefinition.STANDOFF_BLOCKS) {
            return true;
        }
        double step = Math.min(phase.behavior().blocksPerTick(),
                distance - HollowGuardDefinition.STANDOFF_BLOCKS);
        direction.normalize().multiply(step);
        rig.moveTo(grounded(origin.add(direction)));
        return false;
    }

    private void drawBoundary() {
        for (int degrees = 0; degrees < 360; degrees += 10) {
            double radians = Math.toRadians(degrees);
            Location edge = stageCenter.clone().add(Math.cos(radians) * stage.radius(), 0.4,
                    Math.sin(radians) * stage.radius());
            stageCenter.getWorld().spawnParticle(Particle.SOUL, edge, 1, 0, 0, 0, 0);
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
        enter(State.MOTION);
        announceMotion();
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

        motion.charge().ifPresent(run -> runCharge(run, tick));

        for (int i = 0; i < motion.damageWindows().size(); i++) {
            MotionSpec.DamageWindow window = motion.damageWindows().get(i);
            if (tick >= window.fromTick() && tick <= window.toTick()) {
                applyWindow(i, window);
            }
        }

        if (interrupted || tick >= animation.durationTicks()) {
            if (motion.idleAfter().fixed()) {
                idleTarget = rage.idleTicks(motion.idleAfter().minTicks());
            } else {
                idleTarget = motion.idleAfter().pick(random);
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
            land();
            enter(State.IDLE);
            return;
        }
        fallSpeed = Math.min(FALL_MAX, fallSpeed + FALL_GRAVITY);
        Location next = here.clone();
        next.setY(Math.max(groundY, here.getY() - fallSpeed));
        rig.moveTo(next);
    }

    private void land() {
        fallSpeed = 0;
        sound("entity.wither_skeleton.hurt", 1.4f, 0.5f);
        particles(Particle.SOUL, rig.origin(), 30, 1.4);
    }

    private boolean facingTarget() {
        double delta = normalizeDegrees(yawToTarget() - bodyYaw);
        return Math.abs(delta) <= TURN_TOLERANCE_DEGREES;
    }

    /**
     * 前進を進める。{@link MotionSpec.Charge} を流用するが、この個体のモーションは
     * どれも追尾（homing）も中心通過の義務も持たない。始めた時点で向きを固定する。
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
            sound("entity.wither_skeleton.shoot", 1.1f, 0.9f);
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
            hit(player, window.damage());
        }
    }

    private boolean withinWeapon(List<Location> weapon, Location at) {
        for (Location point : weapon) {
            double dx = point.getX() - at.getX();
            double dz = point.getZ() - at.getZ();
            double dy = Math.max(0, Math.max(at.getY() - point.getY(),
                    point.getY() - (at.getY() + HollowGuardDefinition.PLAYER_HEIGHT)));
            if (dx * dx + dy * dy + dz * dz <= WEAPON_REACH * WEAPON_REACH) {
                return true;
            }
        }
        return false;
    }

    private void hit(Player target, MotionSpec.Damage damage) {
        applyDamage(target, damage);
        sound("entity.wither_skeleton.attack", 1.2f, 0.8f);
        particles(Particle.CRIT, target.getLocation().add(0, 1, 0), 12, 0.3);
    }

    private void applyDamage(Player target, MotionSpec.Damage damage) {
        double amount = roll(damage) * rage.damageMultiplier();
        if (guarding(target)) {
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

    private void knockback(Player target) {
        Vector away = target.getLocation().toVector().subtract(rig.origin().toVector());
        away.setY(0);
        if (away.lengthSquared() < 0.0001) {
            away = new Vector(0, 0, 1);
        }
        away.normalize();

        double back = HollowGuardDefinition.BASE_KNOCKBACK;
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
                || origin.distance(rig.origin()) > HollowGuardDefinition.ATTACK_RANGE_BLOCKS) {
            attacker.sendMessage("§7遠すぎる攻撃は通らない（"
                    + (int) HollowGuardDefinition.ATTACK_RANGE_BLOCKS + " ブロック以内から）");
            sound("entity.zombie.attack_iron_door", 0.6f, 1.9f);
            return true;
        }
        if (WeaponDamage.rejected(weapon)) {
            attacker.sendMessage("§7その武器は通らない");
            sound("entity.zombie.attack_iron_door", 0.8f, 1.7f);
            return true;
        }

        PartTracker.Result result = parts.hit(part, WeaponDamage.of(attacker), rage.enraged());
        if (result.immune()) {
            attacker.sendMessage(part + " にダメージは通らない");
            sound("entity.zombie.attack_iron_door", 0.8f, 1.6f);
            return true;
        }
        health -= result.dealt();
        contribution.merge(attacker.getUniqueId(), result.dealt(), Double::sum);

        attacker.sendMessage(String.format("%s に %.1f（残り %.0f）",
                part, result.dealt(), Math.max(0, health)));
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
    }

    private void onEnrage() {
        for (Player player : rig.origin().getWorld().getPlayers()) {
            player.sendMessage("[虚刃] §4激昂した — 待機が縮む");
        }
        sound("entity.wither_skeleton.ambient", 1.6f, 0.5f);
        particles(Particle.SOUL_FIRE_FLAME, rig.origin().add(0, 2.0, 0), 30, 0.8);
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
        bar.setTitle(title.toString());
        bar.setColor(rage.enraged() ? BarColor.RED : BarColor.WHITE);
        for (Player player : rig.origin().getWorld().getPlayers()) {
            if (!bar.getPlayers().contains(player)) {
                bar.addPlayer(player);
            }
        }
    }

    private void announceMotion() {
        sound(switch (motion.name()) {
            case "投げ払い" -> "entity.player.attack.sweep";
            case "アッパー" -> "entity.player.attack.strong";
            case "シールドマッシュ" -> "entity.ravager.attack";
            default -> "entity.wither_skeleton.attack";
        }, 1.3f, 0.9f);
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

    private double yawToTarget() {
        if (chargeDirection != null) {
            return Math.toDegrees(Math.atan2(-chargeDirection.getX(), chargeDirection.getZ()));
        }
        Player target = nearest();
        if (target == null) {
            return bodyYaw;
        }
        Location origin = rig.origin();
        double dx = target.getLocation().getX() - origin.getX();
        double dz = target.getLocation().getZ() - origin.getZ();
        if (dx * dx + dz * dz < 1.0) {
            return bodyYaw;
        }
        return Math.toDegrees(Math.atan2(-dx, dz));
    }

    private double turnToward(double target) {
        double delta = normalizeDegrees(target - bodyYaw);
        double step = Math.max(-HollowGuardDefinition.MAX_TURN_DEGREES,
                Math.min(HollowGuardDefinition.MAX_TURN_DEGREES, delta));
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

    private void sound(String key, float volume, float pitch) {
        Location origin = rig.origin();
        origin.getWorld().playSound(origin, "minecraft:" + key, volume, pitch);
    }

    private void particles(Particle particle, Location location, int count, double spread) {
        location.getWorld().spawnParticle(particle, location, count, spread, spread, spread, 0);
    }

    private void swingEffect(List<Location> weapon) {
        for (Location point : weapon) {
            particles(Particle.SOUL, point, 2, 0.15);
        }
        if (!weapon.isEmpty()) {
            particles(Particle.SWEEP_ATTACK, weapon.get(weapon.size() - 1), 2, 0.2);
        }
    }

    private void trail() {
        Location origin = rig.origin();
        particles(Particle.SOUL, origin.clone().add(0, 0.1, 0), 4, 0.3);
    }

    @Override
    public void playDefeat() {
        Location origin = rig.origin();
        sound("entity.wither.death", 1.4f, 1.3f);
        particles(Particle.SOUL_FIRE_FLAME, origin.clone().add(0, 1.5, 0), 80, 1.5);
        for (Player player : origin.getWorld().getPlayers()) {
            player.sendTitle("§6討 伐", "§7" + species.displayName() + "を倒した", 5, 30, 10);
        }
    }
}
