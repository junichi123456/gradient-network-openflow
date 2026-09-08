package jp.mcserver.plugin;

import java.util.ArrayList;
import java.util.List;
import java.util.Random;
import jp.mcserver.core.raid.GoldenAxe;
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
 * 全域大旋回だけは段階移行時に1回発動する専用の技で、この巡回には含めない
 * （{@link #triggerGrandWhirl()}）——発動した瞬間、巡回中の波を打ち切って割り込む。
 *
 * <p><b>第三形態からは、金のオノ（{@link GoldenAxe}）が加わる</b>（{@link #setAxePhase}）。
 * 降り注ぐ刃・空間斬撃・串刺しはそれぞれ発動ごとに1本、全域大旋回だけは並びにオノを
 * 織り込んで本数そのものが増える（{@link GrandWhirl#totalCount}）。
 */
final class SpecialTrack {

    private enum Kind {
        RAIN, SLASH, SPIKE, WHIRL
    }

    /** 巡回する3種（全域大旋回は段階移行でだけ発動するため含めない）。 */
    private static final Kind[] ROTATION = {Kind.RAIN, Kind.SLASH, Kind.SPIKE};

    private final RaidBossBase boss;
    private final Random random = new Random();
    private final List<FloatingBlade> active = new ArrayList<>();

    private boolean enabled;
    private boolean axePhase;
    private int idleTicks = HollowGuardDefinition.SPECIAL_IDLE_TICKS;
    private int rotationIndex;

    private Kind spawning;
    private int remaining;
    private int cadenceCounter;
    private int whirlSpawnIndex;
    private int bonusAxeRemaining;

    SpecialTrack(RaidBossBase boss) {
        this.boss = boss;
    }

    /** 第二形態へ入った以降にだけ true にする。以前に生成した浮遊剣は無効化後も飛び続ける。 */
    void setEnabled(boolean enabled) {
        this.enabled = enabled;
    }

    /** 第三形態へ入った以降にだけ true にする（金のオノが加わる）。 */
    void setAxePhase(boolean axePhase) {
        this.axePhase = axePhase;
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
        int count = countFor(kind);
        if (count <= 0) {
            idleTicks = HollowGuardDefinition.SPECIAL_IDLE_TICKS;
            return;
        }
        beginWave(kind, count);
    }

    private void beginWave(Kind kind, int count) {
        spawning = kind;
        cadenceCounter = 0;
        remaining = count;
        whirlSpawnIndex = 0;
        bonusAxeRemaining = (axePhase && kind != Kind.WHIRL) ? GoldenAxe.COUNT_PER_BURST : 0;
        boss.announceMotion(displayName(kind));
        if (kind == Kind.SLASH || kind == Kind.WHIRL) {
            // 虚刃の衛士自身の頭上に具現化する合図（実機の指摘「効果音のあとに召喚」）
            boss.sound("entity.evoker.cast_spell", 1.2f, 0.8f);
        }
    }

    private int countFor(Kind kind) {
        int participants = boss.participants();
        int bonusAxe = axePhase ? GoldenAxe.COUNT_PER_BURST : 0;
        return switch (kind) {
            case RAIN -> SwordRain.totalCount(participants) + bonusAxe;
            case SLASH -> SpatialSlash.totalCount(participants) + bonusAxe;
            case SPIKE -> GroundSpike.totalCount(participants) + bonusAxe;
            case WHIRL -> GrandWhirl.totalCount(participants, axePhase);
        };
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
            case WHIRL -> GrandWhirl.WAVE_INTERVAL_TICKS;
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
            case WHIRL -> GrandWhirl.WAVE_SIZE;
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
        if (kind != Kind.WHIRL && bonusAxeRemaining > 0) {
            bonusAxeRemaining--;
            spawnAxe(kind, target);
            return;
        }
        switch (kind) {
            case RAIN -> {
                Location at = target.getLocation();
                double[] xz = SwordRain.spawnPositions(
                        List.of(new double[] {at.getX(), at.getZ()}), 1, random).get(0);
                Location spawn = boss.origin().clone();
                spawn.setX(xz[0]);
                spawn.setZ(xz[1]);
                active.add(new RainBlade(boss, spawn, target));
            }
            case SLASH -> active.add(new HomingBlade(boss, target, SpatialSlash.SPAWN_Y_OFFSET,
                    SpatialSlash.blocksPerTick(), SpatialSlash.MAX_DISTANCE_BLOCKS,
                    SpatialSlash.START_DELAY_TICKS, SpatialSlash.TRACKING_DURATION_TICKS,
                    SpatialSlash.RETARGET_INTERVAL_TICKS, SpatialSlash.DAMAGE,
                    SpatialSlash.KNOCKBACK_BLOCKS, Material.WOODEN_SWORD,
                    SpatialSlash.SWORD_LENGTH * 0.9));
            case SPIKE -> active.add(new SpikeBlade(boss, target.getLocation()));
            case WHIRL -> {
                GrandWhirl.Blade blade = GrandWhirl.bladeAt(whirlSpawnIndex, axePhase);
                whirlSpawnIndex++;
                if (blade.isAxe()) {
                    active.add(new HomingBlade(boss, target, GrandWhirl.SPAWN_Y_OFFSET,
                            GoldenAxe.homingBlocksPerTick(), GrandWhirl.MAX_DISTANCE_BLOCKS,
                            GrandWhirl.START_DELAY_TICKS, GrandWhirl.TRACKING_DURATION_TICKS,
                            GrandWhirl.RETARGET_INTERVAL_TICKS, blade.damage(),
                            GrandWhirl.KNOCKBACK_BLOCKS, Material.GOLDEN_AXE,
                            GoldenAxe.LENGTH * 0.9, true));
                    return;
                }
                Material material = switch (blade) {
                    case GOLD -> Material.GOLDEN_SWORD;
                    case COPPER -> Material.WOODEN_SWORD;
                    case IRON -> Material.IRON_SWORD;
                    case DIAMOND -> Material.DIAMOND_SWORD;
                    case NETHERITE -> Material.NETHERITE_SWORD;
                    // AXE はここに来ない（isAxe() で上に早期returnしている）
                    case AXE -> Material.NETHERITE_SWORD;
                };
                active.add(new HomingBlade(boss, target, GrandWhirl.SPAWN_Y_OFFSET,
                        GrandWhirl.blocksPerTick(), GrandWhirl.MAX_DISTANCE_BLOCKS,
                        GrandWhirl.START_DELAY_TICKS, GrandWhirl.TRACKING_DURATION_TICKS,
                        GrandWhirl.RETARGET_INTERVAL_TICKS, blade.damage(),
                        GrandWhirl.KNOCKBACK_BLOCKS, material, GrandWhirl.SWORD_LENGTH * 0.9));
            }
        }
    }

    /** 第三形態のボーナスの金のオノ1本。動き方はその技の通常の武器と同じにする。 */
    private void spawnAxe(Kind kind, Player target) {
        switch (kind) {
            case RAIN -> {
                Location at = target.getLocation();
                double[] xz = SwordRain.spawnPositions(
                        List.of(new double[] {at.getX(), at.getZ()}), 1, random).get(0);
                Location spawn = boss.origin().clone();
                spawn.setX(xz[0]);
                spawn.setZ(xz[1]);
                active.add(new RainBlade(boss, spawn, target, Material.GOLDEN_AXE, GoldenAxe.LENGTH,
                        GoldenAxe.DAMAGE, SwordRain.KNOCKBACK_BLOCKS, true));
            }
            case SLASH -> active.add(new HomingBlade(boss, target, SpatialSlash.SPAWN_Y_OFFSET,
                    GoldenAxe.homingBlocksPerTick(), SpatialSlash.MAX_DISTANCE_BLOCKS,
                    SpatialSlash.START_DELAY_TICKS, SpatialSlash.TRACKING_DURATION_TICKS,
                    SpatialSlash.RETARGET_INTERVAL_TICKS, GoldenAxe.DAMAGE,
                    SpatialSlash.KNOCKBACK_BLOCKS, Material.GOLDEN_AXE, GoldenAxe.LENGTH * 0.9, true));
            case SPIKE -> active.add(new SpikeBlade(boss, target.getLocation(), Material.GOLDEN_AXE,
                    GoldenAxe.LENGTH, GoldenAxe.DAMAGE, GroundSpike.KNOCKBACK_BLOCKS, true));
            default -> { }
        }
    }

    /**
     * 全域大旋回。段階移行時に1回だけ呼ぶ（{@code RaidBossBase#onPhaseTransition}）。
     * いま巡回中の波があれば打ち切って割り込む——移行そのものが1つの技として機能する
     * という設計（§2「段階構成」）を優先する。
     */
    void triggerGrandWhirl() {
        int count = GrandWhirl.totalCount(boss.participants(), axePhase);
        if (boss.playersInStage().isEmpty() || count <= 0) {
            return;
        }
        beginWave(Kind.WHIRL, count);
        idleTicks = 0;
    }

    private static String displayName(Kind kind) {
        return switch (kind) {
            case RAIN -> "降り注ぐ刃";
            case SLASH -> "空間斬撃";
            case SPIKE -> "串刺し";
            case WHIRL -> "全域大旋回";
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
