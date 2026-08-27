package jp.mcserver.core.raid;

import java.util.List;
import java.util.Map;
import java.util.Optional;

/**
 * 騎士型の定義（§12.7 / raid_species.md）。
 *
 * <p>検証とシミュレーションで共有する。実運用ではデータファイルから読み込む。
 */
public final class KnightDefinition {

    private KnightDefinition() {}

    /** 基準体力（参加1名）。 */
    public static final long BASE_HEALTH = 600;

    /** 両形態を持つ騎士型。 */
    public static RaidSpecies boss() {
        return new RaidSpecies("knight", "騎士", BASE_HEALTH, knightRig(),
                List.of(knightPhaseOne(), knightPhaseTwo()));
    }


    public static Rig knightRig() {
        return new Rig(List.of(
                new Rig.Part("胴", null, Transform.IDENTITY, 2001),
                new Rig.Part("頭", "胴", new Transform(new Vec3(0, 1.0, 0), Vec3.ZERO, Vec3.ONE), 2002),
                new Rig.Part("右腕", "胴", new Transform(new Vec3(0.5, 0.8, 0), Vec3.ZERO, Vec3.ONE), 2003),
                new Rig.Part("左腕", "胴", new Transform(new Vec3(-0.5, 0.8, 0), Vec3.ZERO, Vec3.ONE), 2004),
                new Rig.Part("右足", "胴", new Transform(new Vec3(0.25, -0.9, 0), Vec3.ZERO, Vec3.ONE), 2005),
                new Rig.Part("左足", "胴", new Transform(new Vec3(-0.25, -0.9, 0), Vec3.ZERO, Vec3.ONE), 2006),
                new Rig.Part("槍", "右腕", new Transform(new Vec3(0, -0.6, 0), Vec3.ZERO, Vec3.ONE),
                        2007, false)),
                3.5, 1.6);
    }

    public static Rig centaurRig() {
        return new Rig(List.of(
                new Rig.Part("人胴", null, Transform.IDENTITY, 2101),
                new Rig.Part("頭", "人胴", new Transform(new Vec3(0, 1.0, 0), Vec3.ZERO, Vec3.ONE), 2102),
                new Rig.Part("右腕", "人胴", new Transform(new Vec3(0.5, 0.8, 0), Vec3.ZERO, Vec3.ONE), 2103),
                new Rig.Part("左腕", "人胴", new Transform(new Vec3(-0.5, 0.8, 0), Vec3.ZERO, Vec3.ONE), 2104),
                new Rig.Part("槍", "右腕", new Transform(new Vec3(0, -0.6, 0), Vec3.ZERO, Vec3.ONE),
                        2105, false),
                new Rig.Part("馬胴", "人胴", new Transform(new Vec3(0, -1.2, 0), Vec3.ZERO, Vec3.ONE), 2106),
                new Rig.Part("右前足", "馬胴", new Transform(new Vec3(0.5, -0.8, 0.7), Vec3.ZERO, Vec3.ONE), 2107),
                new Rig.Part("左前足", "馬胴", new Transform(new Vec3(-0.5, -0.8, 0.7), Vec3.ZERO, Vec3.ONE), 2108),
                new Rig.Part("右後足", "馬胴", new Transform(new Vec3(0.5, -0.8, -0.7), Vec3.ZERO, Vec3.ONE), 2109),
                new Rig.Part("左後足", "馬胴", new Transform(new Vec3(-0.5, -0.8, -0.7), Vec3.ZERO, Vec3.ONE), 2110)),
                4.6, 2.0);
    }

    static Animation arm(String name, int duration, boolean loop, int... ticks) {
        List<Animation.Keyframe> keys = new java.util.ArrayList<>();
        for (int i = 0; i < ticks.length; i++) {
            keys.add(new Animation.Keyframe(ticks[i],
                    new Transform(Vec3.ZERO, new Vec3(0, 10.0 * i, 0), Vec3.ONE)));
        }
        return new Animation(name, duration, loop, Map.of("右腕", keys));
    }

    static MotionSpec charge(MotionSpec.Damage damage, double back) {
        return new MotionSpec("突進切り上げ", arm("突進切り上げ", 30, false, 0, 10, 30), 40, true,
                List.of(new MotionSpec.DamageWindow("槍", 10, 30, damage)),
                Optional.of(new MotionSpec.Interrupt("槍", 30, 80)),
                Optional.of(new MotionSpec.Charge(10.0, 20)), Optional.empty(),
                Optional.of(new MotionSpec.Knockback(3, back)), Optional.empty(), false);
    }

    static MotionSpec sweep(MotionSpec.Damage damage) {
        return new MotionSpec("なぎ払い", arm("なぎ払い", 18, false, 0, 5, 10, 18), 40, false,
                List.of(new MotionSpec.DamageWindow("槍", 10, 18, damage)),
                Optional.empty(), Optional.empty(), Optional.empty(),
                Optional.empty(), Optional.empty(), false);
    }

    static MotionSpec tripleThrust(double perHit) {
        var damage = MotionSpec.Damage.of(perHit);
        return new MotionSpec("3段突き", arm("3段突き", 70, false, 0, 15, 20, 35, 40, 55, 65, 70), 40, false,
                List.of(new MotionSpec.DamageWindow("槍", 15, 20, damage),
                        new MotionSpec.DamageWindow("槍", 35, 40, damage),
                        new MotionSpec.DamageWindow("槍", 65, 70, damage)),
                Optional.empty(), Optional.empty(), Optional.empty(),
                Optional.empty(), Optional.empty(), true);
    }

    static MotionSpec fourHit(double a, double b, double c, double d) {
        return new MotionSpec("追従4連切り",
                arm("追従4連切り", 65, false, 0, 15, 20, 30, 35, 45, 50, 60, 65), 40, false,
                List.of(new MotionSpec.DamageWindow("槍", 15, 20, MotionSpec.Damage.of(a)),
                        new MotionSpec.DamageWindow("槍", 30, 35, MotionSpec.Damage.of(b)),
                        new MotionSpec.DamageWindow("槍", 45, 50, MotionSpec.Damage.of(c)),
                        new MotionSpec.DamageWindow("槍", 60, 65, MotionSpec.Damage.of(d))),
                Optional.empty(), Optional.empty(), Optional.empty(),
                Optional.empty(), Optional.empty(), true);
    }

    static MotionSpec orbitCharge() {
        return new MotionSpec("回旋突進", arm("回旋突進", 120, false, 0, 100, 120), 40, false,
                List.of(new MotionSpec.DamageWindow("槍", 100, 120, MotionSpec.Damage.of(40))),
                Optional.empty(), Optional.of(new MotionSpec.Charge(14.0, 20)),
                Optional.of(new MotionSpec.Orbit(30, 1.5, 100)),
                Optional.of(new MotionSpec.Knockback(3, 7)), Optional.empty(), false);
    }

    static MotionSpec stomp() {
        Animation lift = new Animation("踏みつけ", 20, false, Map.of(
                "右前足", List.of(new Animation.Keyframe(0, Transform.IDENTITY),
                        new Animation.Keyframe(20, new Transform(new Vec3(0, 1.5, 0), Vec3.ZERO, Vec3.ONE))),
                "左前足", List.of(new Animation.Keyframe(0, Transform.IDENTITY),
                        new Animation.Keyframe(20, new Transform(new Vec3(0, 1.5, 0), Vec3.ZERO, Vec3.ONE)))));
        return new MotionSpec("踏みつけ", lift, 40, true,
                List.of(new MotionSpec.DamageWindow("右前足", 20, 20, MotionSpec.Damage.of(5)),
                        new MotionSpec.DamageWindow("左前足", 20, 20, MotionSpec.Damage.of(5))),
                Optional.of(new MotionSpec.Interrupt("頭", 20, 40)),
                Optional.empty(), Optional.empty(), Optional.empty(),
                Optional.of(new MotionSpec.AreaEffect(10, 0.3, MotionSpec.Damage.of(28))), false);
    }

    public static RaidSpecies.Phase knightPhaseOne() {
        return new RaidSpecies.Phase("第一形態", 100,
                List.of(charge(new MotionSpec.Damage(25, 30), 5),
                        sweep(new MotionSpec.Damage(22, 28)),
                        tripleThrust(20),
                        fourHit(10.0, 14.0, 11.0, 16.0)),
                "槍に攻撃を当てて突進を止める。パリイで大きな隙を作る", null, 6.0, knightRig());
    }

    public static RaidSpecies.Phase knightPhaseTwo() {
        return new RaidSpecies.Phase("第二形態", 50,
                List.of(charge(new MotionSpec.Damage(28, 32), 5),
                        sweep(new MotionSpec.Damage(27, 32)),
                        tripleThrust(24),
                        fourHit(12.0, 16.0, 13.0, 18.0),
                        orbitCharge(),
                        stomp()),
                "半身半獣に変身し全モーションが加速。回旋突進と踏みつけが加わる", null, 7.0, centaurRig());
    }

}
