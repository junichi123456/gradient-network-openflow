package jp.mcserver.plugin;

import jp.mcserver.core.raid.HollowGuardDefinition;
import jp.mcserver.core.raid.RaidSpecies;
import org.bukkit.Location;
import org.bukkit.Particle;

/**
 * 虚刃の衛士の効果音・パーティクルと初期化（`raid_species.md` §2）。
 * 実体（近接攻撃）の挙動は {@link RaidBossBase} が持つ。
 *
 * <p><b>特殊系統（浮遊する剣）はこのクラスが {@link SpecialTrack} を通じて持つ。</b>
 * 実体系統とは独立して並行に進む「2系統並行の状態機械」であり、{@link RaidBossBase} の
 * 単一の状態機械（ENTER/IDLE/APPROACH/MOTION/RETURN/TURN）とは別に、
 * {@link #tickSpecial()}（毎tick呼ばれる）で {@code SpecialTrack} を回す。
 * 第二形態に入った瞬間（{@link #onPhaseTransition}）に解禁し、全域大旋回を1回発動する。
 */
final class HollowGuardBoss extends RaidBossBase {

    private static final Tuning TUNING = new Tuning(
            HollowGuardDefinition.WEAPON_REACH,
            HollowGuardDefinition.STANDOFF_BLOCKS,
            HollowGuardDefinition.PLAYER_HEIGHT,
            HollowGuardDefinition.ATTACK_RANGE_BLOCKS,
            HollowGuardDefinition.MAX_TURN_DEGREES,
            HollowGuardDefinition.BASE_KNOCKBACK,
            HollowGuardDefinition.IDLE_TRACKING_DELAY_TICKS,
            "虚刃");

    private final SpecialTrack special = new SpecialTrack(this);

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
        super(plugin, HollowGuardDefinition.boss(), TUNING, origin, facingYaw, dropIn);
    }

    @Override
    protected void playSpawnEffect() {
        sound("entity.wither_skeleton.ambient", 1.2f, 0.6f);
        particles(Particle.SOUL_FIRE_FLAME, origin().add(0, 1, 0), 20, 0.8);
    }

    @Override
    protected void playStepEffect() {
        sound("entity.wither_skeleton.step", 0.7f, 0.8f);
    }

    @Override
    protected void playLandEffect() {
        sound("entity.wither_skeleton.hurt", 1.4f, 0.5f);
        particles(Particle.SOUL, origin(), 30, 1.4);
    }

    @Override
    protected void playHitEffect(Location targetLocation) {
        sound("entity.wither_skeleton.attack", 1.2f, 0.8f);
    }

    @Override
    protected void playChargeStartEffect() {
        sound("entity.wither_skeleton.shoot", 1.1f, 0.9f);
    }

    @Override
    protected void playEnrageEffect() {
        sound("entity.wither_skeleton.ambient", 1.6f, 0.5f);
        particles(Particle.SOUL_FIRE_FLAME, origin().add(0, 2.0, 0), 30, 0.8);
    }

    @Override
    protected void playDefeatEffect() {
        sound("entity.wither.death", 1.4f, 1.3f);
        particles(Particle.SOUL_FIRE_FLAME, origin().add(0, 1.5, 0), 80, 1.5);
    }

    @Override
    protected Particle boundaryParticle() {
        return Particle.SOUL;
    }

    @Override
    protected Particle trailParticle() {
        return Particle.SOUL;
    }

    @Override
    protected String motionSound(String motionName) {
        return switch (motionName) {
            case "投げ払い" -> "entity.player.attack.sweep";
            case "アッパー" -> "entity.player.attack.strong";
            case "シールドマッシュ" -> "entity.ravager.attack";
            default -> "entity.wither_skeleton.attack";
        };
    }

    @Override
    protected void tickSpecial() {
        special.tick();
    }

    @Override
    protected void onPhaseTransition(RaidSpecies.Phase from, RaidSpecies.Phase to) {
        if ("第二形態".equals(to.name())) {
            special.setEnabled(true);
            special.triggerGrandWhirl();
        }
    }

    @Override
    public void despawn() {
        special.clear();
        super.despawn();
    }
}
