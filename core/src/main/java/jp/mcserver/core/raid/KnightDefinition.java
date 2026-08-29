package jp.mcserver.core.raid;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;

/**
 * 騎士型の定義（§12.7 / raid_species.md）。
 *
 * <p>検証とシミュレーションで共有する。実運用ではデータファイルから読み込む。
 *
 * <p>見た目はリソースパックを配布する前提だが、それが無い状態でも体型が分かるよう
 * バニラの素材で組んである（{@link Appearance}）。白〜アイボリーの装甲に
 * 機械関節が覗く配色を、コンクリート・骨ブロック・磨かれた安山岩で表している。
 */
public final class KnightDefinition {

    private KnightDefinition() {}

    /** 基準体力（参加1名）。 */
    public static final long BASE_HEALTH = 600;

    /** 頭の弱点倍率。パリイ・妨害・空振りの直後だけ開く。 */
    public static final double HEAD_VULNERABILITY = 2.5;

    /** 槍の当たり判定の分割数。細長い部位を軸に沿った直方体の連なりで表す。 */
    public static final int SPEAR_SEGMENTS = 5;

    /** 槍の先端の細さ。手元の断面に対する比率で、円錐状に絞る。 */
    public static final double SPEAR_TAPER = 0.30;

    /**
     * パリイの成立に要する累積ダメージ（最大体力に対する割合）。
     *
     * <p>最大体力に比例させるのは、人数が増えれば火力も増えるためである。
     * 参加1名で 9、上限の12名で 98 になる。<b>実測で調整する値である</b>（§22）。
     * 突進中の個体は走り抜けていくため、実際に殴れる時間は区間より短い。
     */
    public static final double PARRY_DAMAGE_FRACTION = 0.015;

    /** 突進の前の後ずさり（ブロック / tick）。 */
    public static final double BACKSTEP_BLOCKS = 0.5;
    public static final int BACKSTEP_TICKS = 10;

    /** 突進の到達速度（20tickあたりのブロック数）と、そこまでの加速時間。 */
    public static final double CHARGE_TOP_SPEED = 18.0;
    public static final int CHARGE_ACCELERATION_TICKS = 30;

    /** 突進が走り切る距離。パリイされない限りここまで止まらない。 */
    public static final double CHARGE_DISTANCE = 20.0;

    /** 前足の太さ。 */
    public static final double FORE_LEG = 0.34;

    /** 後足の太さ。前足の 1.2 倍とし、後脚に重心があることを見た目で示す。 */
    public static final double HIND_LEG = FORE_LEG * 1.2;

    /** 人胴の奥行き。正面はこの半分だけ前に出る。 */
    private static final double TORSO_DEPTH = 0.62;

    /** 馬胴の奥行き。 */
    private static final double HORSE_DEPTH = 2.60;

    /**
     * 馬胴の中心の前後位置（人胴から見た相対）。
     *
     * <p><b>人胴の正面と馬胴の正面を揃える。</b>正面の位置は人胴の奥行きの半分であり、
     * 馬胴の中心はそこから馬胴の奥行きの半分だけ後ろへ下がる。
     */
    private static final double HORSE_BODY_Z = TORSO_DEPTH / 2 - HORSE_DEPTH / 2;

    /** 両形態を持つ騎士型。 */
    public static RaidSpecies boss() {
        return new RaidSpecies("knight", "騎士", BASE_HEALTH, knightRig(),
                List.of(knightPhaseOne(), knightPhaseTwo()));
    }

    // ------------------------------------------------------------ 骨格

    /** 素材。白装甲・骨・機械関節・発光する槍。 */
    private static final String ARMOR = "WHITE_CONCRETE";
    private static final String PLATE = "QUARTZ_BLOCK";
    private static final String BONE = "BONE_BLOCK";
    private static final String JOINT = "POLISHED_ANDESITE";
    private static final String SHAFT = "END_ROD";
    private static final String BLADE = "NETHERITE_SWORD";
    private static final String CREST = "NETHERITE_AXE";

    /**
     * 第一形態の骨格。
     *
     * <p>寸法は足元を 0 とした積み上げで決めてある。
     * 足 1.20（0〜1.20）→ 胴 1.20（1.20〜2.40）→ 頭 0.68（2.40〜3.08）→ 角で 3.50。
     * 幅は胴 0.95 に肩を x=±0.52 で置き、外縁が ±0.80 になって 1.60 に収まる。
     */
    public static Rig knightRig() {
        List<Rig.Part> parts = new ArrayList<>();
        parts.add(part("胴", null, pos(0, 1.80, 0), 2001)
                .looks(Appearance.box(ARMOR, 0.95, 1.20, 0.62)));
        parts.add(part("頭", "胴", pos(0, 0.94, 0), 2002)
                .looks(Appearance.box(BONE, 0.68, 0.68, 0.68))
                .weakPoint(HEAD_VULNERABILITY, Rig.Gate.ON_EXPOSURE));
        parts.add(part("右角", "頭", posRot(0.20, 0.20, 0.20, -130, 0, 14), 2003)
                .looks(Appearance.limb(SHAFT, 0.11, 0.72, 0.11).asDecoration()));
        parts.add(part("左角", "頭", posRot(-0.20, 0.20, 0.20, -130, 0, -14), 2004)
                .looks(Appearance.limb(SHAFT, 0.11, 0.72, 0.11).asDecoration()));
        parts.add(part("頭飾り", "頭", posRot(0, 0.30, -0.34, 0, 0, 90), 2005)
                .looks(Appearance.item(CREST, 0.60).asDecoration()));
        parts.add(part("右肩", "胴", pos(0.52, 0.45, 0), 2006)
                .looks(Appearance.box(PLATE, 0.55, 0.42, 0.60)));
        parts.add(part("左肩", "胴", pos(-0.52, 0.45, 0), 2007)
                .looks(Appearance.box(PLATE, 0.55, 0.42, 0.60)));
        parts.add(part("右腕", "胴", pos(0.52, 0.45, 0), 2008)
                .looks(Appearance.limb(ARMOR, 0.40, 1.10, 0.40)));
        parts.add(part("左腕", "胴", pos(-0.52, 0.45, 0), 2009)
                .looks(Appearance.limb(ARMOR, 0.40, 1.10, 0.40)));
        parts.add(part("右足", "胴", pos(0.26, -0.60, 0), 2010)
                .looks(Appearance.limb(JOINT, 0.44, 1.20, 0.44)));
        parts.add(part("左足", "胴", pos(-0.26, -0.60, 0), 2011)
                .looks(Appearance.limb(JOINT, 0.44, 1.20, 0.44)));
        parts.add(part("槍", "右腕", posRot(0, -1.00, 0, -90, 0, 0), 2012)
                .looks(Appearance.limb(SHAFT, 0.26, 3.40, 0.26).taperedTo(SPEAR_TAPER))
                .segments(SPEAR_SEGMENTS)
                .immune());
        parts.add(part("穂先", "槍", posRot(0, -3.35, 0, 0, 0, 45), 2013)
                .looks(Appearance.item(BLADE, 0.70).asDecoration()));
        return new Rig(parts, 3.5, 1.6);
    }

    /**
     * 第二形態の骨格。
     *
     * <p>足 1.50（0〜1.50）→ 馬胴 0.90（1.50〜2.40）→ 人胴 1.15（2.40〜3.55）
     * → 頭 0.68（3.55〜4.23）→ 角で 4.60。
     * 馬胴は<b>前後に 2.60</b> あり、四足は前後 ±1.00 に置く。
     * 足を人胴より長くとることで、四足獣としての体型になる。
     */
    public static Rig centaurRig() {
        List<Rig.Part> parts = new ArrayList<>();
        parts.add(part("人胴", null, pos(0, 2.975, 0), 2101)
                .looks(Appearance.box(ARMOR, 1.00, 1.15, TORSO_DEPTH)));
        parts.add(part("頭", "人胴", pos(0, 0.915, 0), 2102)
                .looks(Appearance.box(BONE, 0.68, 0.68, 0.68))
                .weakPoint(HEAD_VULNERABILITY, Rig.Gate.ON_EXPOSURE));
        parts.add(part("右角", "頭", posRot(0.20, 0.20, 0.20, -130, 0, 14), 2103)
                .looks(Appearance.limb(SHAFT, 0.12, 0.78, 0.12).asDecoration()));
        parts.add(part("左角", "頭", posRot(-0.20, 0.20, 0.20, -130, 0, -14), 2104)
                .looks(Appearance.limb(SHAFT, 0.12, 0.78, 0.12).asDecoration()));
        parts.add(part("頭飾り", "頭", posRot(0, 0.32, -0.36, 0, 0, 90), 2105)
                .looks(Appearance.item(CREST, 0.68).asDecoration()));
        parts.add(part("右肩", "人胴", pos(0.66, 0.425, 0), 2106)
                .looks(Appearance.box(PLATE, 0.65, 0.48, 0.62)));
        parts.add(part("左肩", "人胴", pos(-0.66, 0.425, 0), 2107)
                .looks(Appearance.box(PLATE, 0.65, 0.48, 0.62)));
        parts.add(part("右腕", "人胴", pos(0.66, 0.425, 0), 2108)
                .looks(Appearance.limb(ARMOR, 0.42, 1.10, 0.42)));
        parts.add(part("左腕", "人胴", pos(-0.66, 0.425, 0), 2109)
                .looks(Appearance.limb(ARMOR, 0.42, 1.10, 0.42)));
        parts.add(part("槍", "右腕", posRot(0, -1.00, 0, -90, 0, 0), 2110)
                .looks(Appearance.limb(SHAFT, 0.28, 3.80, 0.28).taperedTo(SPEAR_TAPER))
                .segments(SPEAR_SEGMENTS)
                .immune());
        parts.add(part("穂先", "槍", posRot(0, -3.75, 0, 0, 0, 45), 2111)
                .looks(Appearance.item(BLADE, 0.75).asDecoration()));
        parts.add(part("馬胴", "人胴", pos(0, -1.025, HORSE_BODY_Z), 2112)
                .looks(Appearance.box(ARMOR, 1.15, 0.90, HORSE_DEPTH))
                .segments(2));
        parts.add(part("右前足", "馬胴", pos(0.38, -0.45, 1.00), 2113)
                .looks(Appearance.limb(JOINT, FORE_LEG, 1.50, FORE_LEG)));
        parts.add(part("左前足", "馬胴", pos(-0.38, -0.45, 1.00), 2114)
                .looks(Appearance.limb(JOINT, FORE_LEG, 1.50, FORE_LEG)));
        parts.add(part("右後足", "馬胴", pos(0.38, -0.45, -1.00), 2115)
                .looks(Appearance.limb(JOINT, HIND_LEG, 1.50, HIND_LEG)));
        parts.add(part("左後足", "馬胴", pos(-0.38, -0.45, -1.00), 2116)
                .looks(Appearance.limb(JOINT, HIND_LEG, 1.50, HIND_LEG)));
        return new Rig(parts, 4.6, 2.0);
    }

    private static Rig.Part part(String name, String parent, Transform base, int modelId) {
        return new Rig.Part(name, parent, base, modelId);
    }

    private static Transform pos(double x, double y, double z) {
        return new Transform(new Vec3(x, y, z), Vec3.ZERO, Vec3.ONE);
    }

    private static Transform posRot(double x, double y, double z,
                                    double rx, double ry, double rz) {
        return new Transform(new Vec3(x, y, z), new Vec3(rx, ry, rz), Vec3.ONE);
    }

    // ------------------------------------------------------------ モーション

    /** 回転だけのキーフレーム。 */
    private static Animation.Keyframe rot(int tick, double x, double y, double z) {
        return new Animation.Keyframe(tick, new Transform(Vec3.ZERO, new Vec3(x, y, z), Vec3.ONE));
    }

    /** 平行移動だけのキーフレーム。 */
    private static Animation.Keyframe move(int tick, double x, double y, double z) {
        return new Animation.Keyframe(tick, new Transform(new Vec3(x, y, z), Vec3.ZERO, Vec3.ONE));
    }

    /** 平行移動と回転を持つキーフレーム。 */
    private static Animation.Keyframe pose(int tick, double tz, double rx) {
        return new Animation.Keyframe(tick,
                new Transform(new Vec3(0, 0, tz), new Vec3(rx, 0, 0), Vec3.ONE));
    }

    private static Animation animation(String name, int duration, boolean loop,
                                       Object... trackPairs) {
        Map<String, List<Animation.Keyframe>> tracks = new LinkedHashMap<>();
        for (int i = 0; i < trackPairs.length; i += 2) {
            @SuppressWarnings("unchecked")
            List<Animation.Keyframe> keys = (List<Animation.Keyframe>) trackPairs[i + 1];
            tracks.put((String) trackPairs[i], keys);
        }
        return new Animation(name, duration, loop, tracks);
    }

    /**
     * 突進切り上げ。後ずさってから加速し、20ブロックを走り切る。
     *
     * <p>走り出したら<b>パリイされない限り止まらない</b>。槍を叩いても中断しないため、
     * 避けるか、踏み込んで打ち返すかの二択になる。避け切られた場合は空振りとして
     * 弱点が露出する（§12.6）。
     */
    static MotionSpec charge(MotionSpec.Damage damage, double back, String body) {
        var run = new MotionSpec.Charge(0, BACKSTEP_BLOCKS, BACKSTEP_TICKS, 0,
                CHARGE_TOP_SPEED, CHARGE_ACCELERATION_TICKS, CHARGE_DISTANCE);
        int end = run.endTick();
        int duration = end + 5;
        Animation animation = animation("突進切り上げ", duration, false,
                body, List.of(rot(0, 0, 0, 0), rot(BACKSTEP_TICKS, -12, 0, 0),
                        rot(BACKSTEP_TICKS + 10, 10, 0, 0), rot(end - 2, 16, 0, 0),
                        rot(duration, -8, 0, 0)),
                "頭", List.of(rot(0, 0, 0, 0), rot(BACKSTEP_TICKS, 6, 0, 0),
                        rot(end, -14, 0, 0)),
                "右腕", List.of(pose(0, 0, 0), pose(BACKSTEP_TICKS, -0.45, -20),
                        pose(BACKSTEP_TICKS + 10, 0.30, -25), pose(end - 2, 0.40, -22),
                        pose(end, 0.20, -60), pose(duration, 0.10, -105)));
        return new MotionSpec("突進切り上げ", animation, 40,
                Optional.of(new MotionSpec.Parry(run.runFromTick(), end,
                        PARRY_DAMAGE_FRACTION)),
                List.of(new MotionSpec.DamageWindow("槍", run.runFromTick() + 2, end, damage)),
                Optional.empty(),
                Optional.of(run), Optional.empty(),
                Optional.of(new MotionSpec.Knockback(3, back)), Optional.empty(), false,
                new MotionSpec.Usage(0, 30.0, 0, 25, 120, 2, false));
    }

    /**
     * なぎ払い。槍を左 210 度へ構え、5 tick 静止してから −30 度まで 240 度振る。
     */
    static MotionSpec sweep(MotionSpec.Damage damage, String body) {
        Animation animation = animation("なぎ払い", 18, false,
                body, List.of(rot(0, 0, 0, 0), rot(5, 0, 28, 0), rot(10, 0, 28, 0),
                        rot(18, 0, -38, 0)),
                "右腕", List.of(rot(0, 0, 0, 0), rot(5, -18, 120, 0), rot(10, -18, 120, 0),
                        rot(18, -12, -120, 0)));
        return new MotionSpec("なぎ払い", animation, 40, Optional.empty(),
                List.of(new MotionSpec.DamageWindow("槍", 10, 18, damage)),
                Optional.empty(), Optional.empty(), Optional.empty(),
                Optional.empty(), Optional.empty(), false,
                MotionSpec.Usage.at(0, 6.5, 30, 60));
    }

    /**
     * 3段突き。3回目だけ 10 tick のディレイが入る。
     *
     * <p>そのディレイは予備動作を見せる時間であり、同時に<b>咎める時間</b>でもある。
     * 3段目が出る前（65 tick まで）に槍を叩けば止められ、弱点が露出する。
     */
    static MotionSpec tripleThrust(double perHit, String body) {
        var damage = MotionSpec.Damage.of(perHit);
        Animation animation = animation("3段突き", 70, false,
                body, List.of(rot(0, 0, 0, 0), rot(20, 6, 0, 0), rot(40, 6, 0, 0),
                        rot(55, 2, 0, 0), rot(70, 12, 0, 0)),
                "右腕", List.of(pose(0, 0, 0), pose(15, -0.35, -88), pose(20, 1.15, -92),
                        pose(25, -0.35, -88), pose(35, -0.35, -88), pose(40, 1.15, -92),
                        pose(45, -0.35, -88), pose(55, -0.55, -84), pose(65, -0.55, -84),
                        pose(70, 1.35, -94)));
        return new MotionSpec("3段突き", animation, 40, Optional.empty(),
                List.of(new MotionSpec.DamageWindow("槍", 15, 20, damage),
                        new MotionSpec.DamageWindow("槍", 35, 40, damage),
                        new MotionSpec.DamageWindow("槍", 65, 70, damage)),
                Optional.of(new MotionSpec.Interrupt("槍", 65, 60)),
                Optional.empty(), Optional.empty(),
                Optional.empty(), Optional.empty(), true,
                MotionSpec.Usage.at(0, 5.5, 20, 90));
    }

    /**
     * 追従4連切り。突き → 振り下ろし → 真上からの突き → 振り下ろし。
     */
    static MotionSpec fourHit(double a, double b, double c, double d, String body) {
        Animation animation = animation("追従4連切り", 65, false,
                "頭", List.of(rot(0, 0, 0, 0), rot(35, 12, 0, 0), rot(65, 10, 0, 0)),
                body, List.of(rot(0, 0, 0, 0), rot(20, 5, 0, 0), rot(45, -8, 0, 0),
                        rot(65, 10, 0, 0)),
                "右腕", List.of(pose(0, 0, 0), pose(15, -0.30, -85), pose(20, 1.00, -90),
                        pose(30, 0, -160), pose(35, 0.30, -10), pose(45, 0, -185),
                        pose(50, 0.90, -95), pose(60, 0, -160), pose(65, 0.30, -5)));
        return new MotionSpec("追従4連切り", animation, 40, Optional.empty(),
                List.of(new MotionSpec.DamageWindow("槍", 15, 20, MotionSpec.Damage.of(a)),
                        new MotionSpec.DamageWindow("槍", 30, 35, MotionSpec.Damage.of(b)),
                        new MotionSpec.DamageWindow("槍", 45, 50, MotionSpec.Damage.of(c)),
                        new MotionSpec.DamageWindow("槍", 60, 65, MotionSpec.Damage.of(d))),
                Optional.empty(), Optional.empty(), Optional.empty(),
                Optional.empty(), Optional.empty(), true,
                MotionSpec.Usage.at(0, 7.5, 20, 110));
    }

    /**
     * 回旋突進。円周を 1.5 周してから最短距離のプレイヤーへ突進する。
     *
     * <p>突進は<b>すでに 15 ブロック毎秒で走っている状態から</b>始まり、
     * 20 tick で 20 ブロック毎秒に達する。回旋の勢いをそのまま乗せる。
     */
    static MotionSpec orbitCharge() {
        Animation animation = animation("回旋突進", 120, false,
                "人胴", List.of(rot(0, 0, 0, 0), rot(20, 0, 0, 18), rot(100, 0, 0, 18),
                        rot(120, 16, 0, 0)),
                "右腕", List.of(rot(0, 0, 0, 0), rot(20, -90, 0, 0), rot(100, -90, 0, 0),
                        rot(120, -98, 0, 0)));
        return new MotionSpec("回旋突進", animation, 40, Optional.empty(),
                List.of(new MotionSpec.DamageWindow("槍", 100, 120, MotionSpec.Damage.of(40))),
                Optional.empty(),
                Optional.of(new MotionSpec.Charge(100, 0, 0, 15.0, 20.0, 20, 17.6)),
                Optional.of(new MotionSpec.Orbit(30, 1.5, 100)),
                Optional.of(new MotionSpec.Knockback(3, 7)), Optional.empty(), false,
                MotionSpec.Usage.at(6.0, 40.0, 25, 320));
    }

    /** 踏みつけ。両前足を持ち上げて叩きつけ、半径10ブロックに衝撃波を出す。 */
    static MotionSpec stomp() {
        Animation animation = animation("踏みつけ", 20, false,
                "人胴", List.of(rot(0, 0, 0, 0), rot(12, -26, 0, 0), rot(20, 6, 0, 0)),
                "右前足", List.of(move(0, 0, 0, 0),
                        new Animation.Keyframe(12,
                                new Transform(new Vec3(0, 1.30, 0), new Vec3(-35, 0, 0), Vec3.ONE)),
                        move(20, 0, 0, 0)),
                "左前足", List.of(move(0, 0, 0, 0),
                        new Animation.Keyframe(12,
                                new Transform(new Vec3(0, 1.30, 0), new Vec3(-35, 0, 0), Vec3.ONE)),
                        move(20, 0, 0, 0)));
        return new MotionSpec("踏みつけ", animation, 40,
                Optional.of(new MotionSpec.Parry(0, 20, PARRY_DAMAGE_FRACTION)),
                List.of(new MotionSpec.DamageWindow("右前足", 20, 20, MotionSpec.Damage.of(5)),
                        new MotionSpec.DamageWindow("左前足", 20, 20, MotionSpec.Damage.of(5))),
                Optional.of(new MotionSpec.Interrupt("頭", 20, 40)),
                Optional.empty(), Optional.empty(), Optional.empty(),
                Optional.of(new MotionSpec.AreaEffect(10, 0.3, MotionSpec.Damage.of(28))), false,
                MotionSpec.Usage.crowd(9.0, 2, 30, 220));
    }

    // ------------------------------------------------------------ 待機と歩行

    /** 二足形態の待機。呼吸と首の振り。 */
    static Animation knightIdle() {
        return animation("待機", 40, true,
                "胴", List.of(move(0, 0, 0, 0), move(20, 0, 0.08, 0), move(40, 0, 0, 0)),
                "頭", List.of(rot(0, 0, 0, 0), rot(10, 0, 10, 0), rot(30, 0, -10, 0),
                        rot(40, 0, 0, 0)),
                "右腕", List.of(rot(0, 0, 0, 0), rot(20, 5, 0, 0), rot(40, 0, 0, 0)),
                "左腕", List.of(rot(0, 0, 0, 0), rot(20, -5, 0, 0), rot(40, 0, 0, 0)));
    }

    /** 二足形態の歩行。左右の足と腕を交互に振る。 */
    static Animation knightWalk() {
        return animation("歩行", 20, true,
                "右足", List.of(rot(0, 0, 0, 0), rot(5, -28, 0, 0), rot(10, 0, 0, 0),
                        rot(15, 24, 0, 0), rot(20, 0, 0, 0)),
                "左足", List.of(rot(0, 0, 0, 0), rot(5, 24, 0, 0), rot(10, 0, 0, 0),
                        rot(15, -28, 0, 0), rot(20, 0, 0, 0)),
                "右腕", List.of(rot(0, 0, 0, 0), rot(10, 14, 0, 0), rot(20, 0, 0, 0)),
                "胴", List.of(move(0, 0, 0, 0), move(5, 0, 0.06, 0), move(10, 0, 0, 0),
                        move(15, 0, 0.06, 0), move(20, 0, 0, 0)));
    }

    /** 四足形態の待機。 */
    static Animation centaurIdle() {
        return animation("待機", 40, true,
                "人胴", List.of(move(0, 0, 0, 0), move(20, 0, 0.07, 0), move(40, 0, 0, 0)),
                "頭", List.of(rot(0, 0, 0, 0), rot(10, 0, 12, 0), rot(30, 0, -12, 0),
                        rot(40, 0, 0, 0)),
                "右前足", List.of(rot(0, 0, 0, 0), rot(20, -6, 0, 0), rot(40, 0, 0, 0)),
                "左前足", List.of(rot(0, 0, 0, 0), rot(20, 6, 0, 0), rot(40, 0, 0, 0)));
    }

    /** 四足形態の歩行。対角の足が同時に出る。 */
    static Animation centaurWalk() {
        return animation("歩行", 20, true,
                "右前足", List.of(rot(0, 0, 0, 0), rot(5, -30, 0, 0), rot(10, 0, 0, 0),
                        rot(15, 26, 0, 0), rot(20, 0, 0, 0)),
                "左後足", List.of(rot(0, 0, 0, 0), rot(5, -30, 0, 0), rot(10, 0, 0, 0),
                        rot(15, 26, 0, 0), rot(20, 0, 0, 0)),
                "左前足", List.of(rot(0, 0, 0, 0), rot(5, 26, 0, 0), rot(10, 0, 0, 0),
                        rot(15, -30, 0, 0), rot(20, 0, 0, 0)),
                "右後足", List.of(rot(0, 0, 0, 0), rot(5, 26, 0, 0), rot(10, 0, 0, 0),
                        rot(15, -30, 0, 0), rot(20, 0, 0, 0)));
    }

    // ------------------------------------------------------------ 段階

    public static RaidSpecies.Phase knightPhaseOne() {
        var behavior = new RaidSpecies.Behavior(MotionSpec.DEFAULT_IDLE_TICKS, 20, 6.0,
                knightIdle(), knightWalk());
        return new RaidSpecies.Phase("第一形態", 100,
                List.of(charge(new MotionSpec.Damage(25, 30), 5, "胴"),
                        sweep(new MotionSpec.Damage(22, 28), "胴"),
                        tripleThrust(20, "胴"),
                        fourHit(10.0, 14.0, 11.0, 16.0, "胴")),
                "槍を叩いて突進を止め、パリイで頭を露出させる。露出中の頭だけが倍率の乗る的である",
                null, behavior, knightRig());
    }

    public static RaidSpecies.Phase knightPhaseTwo() {
        var behavior = new RaidSpecies.Behavior(MotionSpec.DEFAULT_IDLE_TICKS, 20, 7.0,
                centaurIdle(), centaurWalk());
        return new RaidSpecies.Phase("第二形態", 50,
                List.of(charge(new MotionSpec.Damage(28, 32), 5, "人胴"),
                        sweep(new MotionSpec.Damage(27, 32), "人胴"),
                        tripleThrust(24, "人胴"),
                        fourHit(12.0, 16.0, 13.0, 18.0, "人胴"),
                        orbitCharge(),
                        stomp()),
                "半身半獣に変身し全モーションが加速。回旋突進と踏みつけが加わる",
                null, behavior, centaurRig());
    }
}
