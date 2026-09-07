package jp.mcserver.plugin;

import java.util.ArrayList;
import java.util.List;
import java.util.Random;
import jp.mcserver.core.raid.GrandWhirl;
import jp.mcserver.core.raid.GroundSpike;
import jp.mcserver.core.raid.HollowGuardDefinition;
import jp.mcserver.core.raid.SpatialSlash;
import jp.mcserver.core.raid.SwordRain;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.entity.Player;

/**
 * 虚刃の衛士の特殊系統（浮遊する剣）を駆動する、実体系統とは独立した小さな状態機械
 * （`raid_species.md` §2「2系統並行の状態機械」）。
 *
 * <p>第二形態に入ってから {@code HollowGuardBoss#tickSpecial()} を通じて毎tick進む。
 * 実体系統の待機→接近→技→向き直りとはまったく別に、
 * 「待機 → 特殊モーションを1つ選ぶ → 生成間隔ごとに波を出す → 待機」を繰り返す。
 * 生成した浮遊剣（{@link FloatingBlade}）は、この状態機械が次の待機に入ったあとも
 * 生き続け、独立して飛び続ける——「本体が待機している最中に浮遊剣が攻撃していることも、
 * その逆もあり得る」（§2）という設計をそのまま形にしたものである。
 *
 * <p>3種（降り注ぐ刃・空間斬撃・串刺し）は固定の順で巡回する（**選び方は仮**。
 * 実体系統の {@code MotionSelector} のような状況判断は、いまのところ特殊には設けていない）。
 * 全域大旋回だけは段階移行時に1回ずつ発動する専用の技であり、この巡回には含めない
 * （{@link #triggerGrandWhirl()}）。
 */
final class SpecialTrack {

    /** 巡回する3種（全域大旋回を除く）。 */
    private enum Kind {
        RAIN, SLASH, SPIKE
    }

    private static final Kind[] ROTATION = Kind.values();

    private final RaidBossBase boss;
    private final Random random = new Random();
    private final List<FloatingBlade> active = new ArrayList<>();

    private boolean enabled;
    private int idleTicks = HollowGuardDefinition.SPECIAL_IDLE_TICKS;
    private int rotationIndex;

    private Kind spawning;
    private int remaining;
    private int cadenceCounter;

    SpecialTrack(RaidBossBase boss) {
        this.boss = boss;
    }

    /** 第二形態へ入った以降にだけ true にする。以前に生成した浮遊剣は無効化後も飛び続ける。 */
    void setEnabled(boolean enabled) {
        this.enabled = enabled;
    }

    void tick() {
        for (int i = active.size() - 1; i >= 0; i--) {
            if (active.get(i).tick()) {
                active.get(i).despawn();
                active.remove(i);
            }
        }
        if (!enabled) {
            return;
        }
        if (spawning != null) {
            continueWave();
            return;
        }
        if (idleTicks > 0) {
            idleTicks--;
            return;
        }
        startNextWave();
    }

    private void startNextWave() {
        if (boss.playersInStage().isEmpty()) {
            idleTicks = 20;
            return;
        }
        Kind kind = ROTATION[rotationIndex % ROTATION.length];
        rotationIndex++;
        int participants = boss.participants();
        int count = switch (kind) {
            case RAIN -> SwordRain.totalCount(participants);
            case SLASH -> SpatialSlash.totalCount(participants);
            case SPIKE -> GroundSpike.totalCount(participants);
        };
        if (count <= 0) {
            idleTicks = HollowGuardDefinition.SPECIAL_IDLE_TICKS;
            return;
        }
        spawning = kind;
        cadenceCounter = 0;
        remaining = count;
        boss.announceMotion(displayName(kind));
    }

    private void continueWave() {
        if (remaining <= 0) {
            spawning = null;
            idleTicks = HollowGuardDefinition.SPECIAL_IDLE_TICKS;
            return;
        }
        int interval = switch (spawning) {
            case RAIN -> SwordRain.WAVE_INTERVAL_TICKS;
            case SLASH -> SpatialSlash.WAVE_INTERVAL_TICKS;
            case SPIKE -> GroundSpike.WAVE_INTERVAL_TICKS;
        };
        if (cadenceCounter % interval != 0) {
            cadenceCounter++;
            return;
        }
        cadenceCounter++;
        int size = switch (spawning) {
            case RAIN -> SwordRain.WAVE_SIZE;
            case SLASH -> SpatialSlash.WAVE_SIZE;
            case SPIKE -> GroundSpike.WAVE_SIZE;
        };
        for (int i = 0; i < size && remaining > 0; i++) {
            spawnOne(spawning);
            remaining--;
        }
    }

    private void spawnOne(Kind kind) {
        List<Player> players = boss.playersInStage();
        if (players.isEmpty()) {
            return;
        }
        Player target = players.get(random.nextInt(players.size()));
        switch (kind) {
            case RAIN -> {
                Location at = target.getLocation();
                double[] xz = SwordRain.spawnPositions(
                        List.of(new double[] {at.getX(), at.getZ()}), 1, random).get(0);
                Location spawn = boss.origin().clone();
                spawn.setX(xz[0]);
                spawn.setZ(xz[1]);
                active.add(new RainBlade(boss, spawn));
            }
            case SLASH -> {
                double jitter = random.nextDouble() * 2 - 1;
                active.add(new ArcBlade(boss, target.getLocation(), target, SpatialSlash.SPAWN_Y,
                        SpatialSlash.END_Y, SpatialSlash.distanceFor(jitter),
                        SpatialSlash.SPEED_BLOCKS_PER_SECOND, SpatialSlash.START_DELAY_TICKS,
                        SpatialSlash.DAMAGE, SpatialSlash.KNOCKBACK_BLOCKS, Material.COPPER_BLOCK,
                        0.3, SpatialSlash.SWORD_LENGTH, random));
            }
            case SPIKE -> active.add(new SpikeBlade(boss, target.getLocation()));
        }
    }

    /** 全域大旋回。段階移行時に1回だけ呼ぶ（{@code RaidBossBase#onPhaseTransition}）。 */
    void triggerGrandWhirl() {
        List<Player> players = boss.playersInStage();
        int count = GrandWhirl.totalCount(boss.participants());
        if (players.isEmpty() || count <= 0) {
            return;
        }
        boss.announceMotion("全域大旋回");
        for (int i = 0; i < count; i++) {
            Player target = players.get(i % players.size());
            double spawnY = GrandWhirl.SPAWN_Y_MIN
                    + random.nextDouble() * (GrandWhirl.SPAWN_Y_MAX - GrandWhirl.SPAWN_Y_MIN);
            GrandWhirl.Blade blade = GrandWhirl.bladeAt(i);
            Material material = switch (blade) {
                case GOLD -> Material.GOLD_BLOCK;
                case COPPER -> Material.COPPER_BLOCK;
                case IRON -> Material.IRON_BLOCK;
                case DIAMOND -> Material.DIAMOND_BLOCK;
                case NETHERITE -> Material.NETHERITE_BLOCK;
            };
            active.add(new ArcBlade(boss, target.getLocation(), target, spawnY, GrandWhirl.END_Y,
                    GrandWhirl.DISTANCE_BLOCKS, GrandWhirl.SPEED_BLOCKS_PER_SECOND,
                    GrandWhirl.START_DELAY_TICKS, blade.damage(), GrandWhirl.KNOCKBACK_BLOCKS,
                    material, 0.3, GrandWhirl.SWORD_LENGTH, random));
        }
    }

    private static String displayName(Kind kind) {
        return switch (kind) {
            case RAIN -> "降り注ぐ刃";
            case SLASH -> "空間斬撃";
            case SPIKE -> "串刺し";
        };
    }

    /** 生きている浮遊剣の数。状況把握（{@code /raid} コマンドの表示）に使う。 */
    int activeCount() {
        return active.size();
    }

    /** 停止する。討伐・撤去のときに呼ぶ。 */
    void clear() {
        for (FloatingBlade blade : active) {
            blade.despawn();
        }
        active.clear();
    }
}
