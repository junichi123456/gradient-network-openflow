package jp.mcserver.plugin;

import jp.mcserver.core.raid.KnightDefinition;
import org.bukkit.Location;
import org.bukkit.Particle;

/**
 * 騎士型の効果音・パーティクルと初期化（§12.7）。挙動そのものは {@link RaidBossBase} が持つ。
 *
 * <p>パリイは盾では成立しない。<b>その区間に個体へ与えた累積ダメージ</b>で判定する（§12.6、
 * 共通の処理は基底クラス）。突進は走り出したらパリイされない限り止まらず、決めた距離を
 * 走り切る。
 */
final class KnightBoss extends RaidBossBase {

    private static final Tuning TUNING = new Tuning(
            KnightDefinition.WEAPON_REACH,
            KnightDefinition.STANDOFF_BLOCKS,
            KnightDefinition.PLAYER_HEIGHT,
            KnightDefinition.ATTACK_RANGE_BLOCKS,
            KnightDefinition.MAX_TURN_DEGREES,
            KnightDefinition.BASE_KNOCKBACK,
            KnightDefinition.IDLE_TRACKING_DELAY_TICKS,
            "騎士");

    KnightBoss(RaidPlugin plugin, Location origin) {
        this(plugin, origin, 0, false);
    }

    /**
     * 向きと登場の仕方を指定して出す。
     *
     * @param facingYaw 体の向き（度）。0 が南、180 が北
     * @param dropIn    true なら渡された高さから<b>自由落下して地表面に到達する</b>。
     *                  false なら最初から接地させる
     */
    KnightBoss(RaidPlugin plugin, Location origin, double facingYaw, boolean dropIn) {
        super(plugin, KnightDefinition.boss(), TUNING, origin, facingYaw, dropIn);
    }

    @Override
    protected void playSpawnEffect() {
        sound("entity.ravager.roar", 1.4f, 0.7f);
        particles(Particle.EXPLOSION_EMITTER, origin().add(0, 1, 0), 2, 0.8);
    }

    @Override
    protected void playStepEffect() {
        sound("entity.iron_golem.step", 0.7f, 0.8f);
    }

    @Override
    protected void playLandEffect() {
        sound("entity.iron_golem.damage", 1.6f, 0.6f);
        particles(Particle.EXPLOSION, origin(), 6, 1.0);
        particles(Particle.LARGE_SMOKE, origin(), 30, 1.6);
    }

    @Override
    protected void playHitEffect(Location targetLocation) {
        sound("entity.iron_golem.attack", 1.2f, 0.9f);
    }

    @Override
    protected void playChargeStartEffect() {
        sound("entity.ravager.attack", 1.3f, 1.1f);
    }

    @Override
    protected void playEnrageEffect() {
        sound("entity.ravager.roar", 1.6f, 0.6f);
        particles(Particle.ANGRY_VILLAGER, origin().add(0, 2.5, 0), 30, 0.8);
        particles(Particle.FLAME, origin().add(0, 1.5, 0), 40, 1.0);
    }

    @Override
    protected void playDefeatEffect() {
        sound("entity.ender_dragon.death", 1.6f, 1.2f);
        particles(Particle.EXPLOSION_EMITTER, origin().add(0, 1.5, 0), 6, 1.2);
        particles(Particle.SOUL_FIRE_FLAME, origin().add(0, 1.5, 0), 80, 1.5);
    }

    @Override
    protected Particle boundaryParticle() {
        return Particle.END_ROD;
    }

    @Override
    protected Particle trailParticle() {
        return Particle.LARGE_SMOKE;
    }

    @Override
    protected String motionSound(String motionName) {
        return switch (motionName) {
            case "突進切り上げ" -> "item.trident.throw";
            case "なぎ払い" -> "entity.player.attack.sweep";
            case "3段突き" -> "item.trident.riptide_1";
            case "追従4連切り" -> "entity.player.attack.strong";
            case "回旋突進" -> "entity.horse.gallop";
            case "踏みつけ" -> "entity.ravager.step";
            default -> "entity.iron_golem.attack";
        };
    }
}
