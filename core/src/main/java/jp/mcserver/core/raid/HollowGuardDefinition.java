package jp.mcserver.core.raid;

import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;

/**
 * 虚刃の衛士（`raid_species.md` §2）。レイド個体の2種目。
 *
 * <p><b>実体（本体の近接攻撃）は {@link MotionSpec} でこのクラスが持つ。</b>特殊
 * （浮遊する剣、第二形態から）は独立した系統として並行に進む設計であり、単一の
 * {@code MotionSpec} では表せない——浮遊剣の軌道・生成間隔・当たり判定は
 * {@link SwordRain}・{@link SpatialSlash}・{@link GroundSpike}・{@link GrandWhirl} の
 * 4クラスが持ち、駆動は plugin 側（{@code HollowGuardBoss} の {@code tickSpecial()}）が行う。
 *
 * <p><b>第一・第二・第三の3形態</b>を持つ。第二形態は実体3種に加えて特殊系統が解禁され、
 * 第三形態では特殊4種それぞれに金のオノが1本ずつ（全域大旋回だけは別の式で）加わる
 * （{@link GoldenAxe}）。
 */
public final class HollowGuardDefinition {

    private HollowGuardDefinition() {
    }

    // ------------------------------------------------------------ 基準値

    /** 基準体力（参加1名）。**仮の値。** 騎士型（600）と同じ値を暫定で置く。実測で調整する。 */
    public static final long BASE_HEALTH = 600;

    /** 武器のリーチ（ブロック）。武器が3倍サイズであることを踏まえ、騎士型（2.2）より長く取る。**仮の値。** */
    public static final double WEAPON_REACH = 4.5;

    /** 密着せず止まる距離（ブロック）。リーチが長いぶん、騎士型（3.5）より遠くで止まる。 */
    public static final double STANDOFF_BLOCKS = 4.0;

    /**
     * 実体系統の待機（tick）。**約10秒。** 特殊系統（{@link #SPECIAL_IDLE_TICKS}、約6秒）とは別の値を持つ。
     * 2系統が独立して並行に進むため、体感の手数は待ち時間の長さだけでは決まらない。
     */
    public static final int PHYSICAL_IDLE_TICKS = 200;

    /** 移動速度（ブロック / 20tick）。**仮の値。** 騎士型（6.0）と同じ値を暫定で置く。 */
    public static final double MOVE_SPEED = 6.0;

    /** 全長（ブロック）。ネザースケルトン相当（プレイヤーよりやや高い程度）。 */
    public static final double HEIGHT = 2.4;

    /** 幅（ブロック）。太さを倍にした実機調整を反映（§2「体型」）。 */
    public static final double WIDTH = 2.0;

    /** 剣の長さ（ブロック）。通常のプレイヤー用武器のおよそ3倍。 */
    public static final double SWORD_LENGTH = 3.0;

    /** プレイヤーの立ち姿の高さ（ブロック）。当たり判定の縦の線分に使う。 */
    public static final double PLAYER_HEIGHT = 1.8;

    /** 攻撃を受け付ける距離（ブロック）。個体からこれより遠くで放たれた攻撃は通らない。 */
    public static final double ATTACK_RANGE_BLOCKS = 10.0;

    /** 1回の姿勢更新で回れる角度の上限（度）。震えを抑える。 */
    public static final double MAX_TURN_DEGREES = 15.0;

    /** どの攻撃にも乗る最小の押し出し（ブロック）。 */
    public static final double BASE_KNOCKBACK = 0.1;

    /**
     * 待機中に相手を追う向きの遅れ（tick）。騎士型と同じ10tickを暫定で置く
     * （§12.6の共通の規約。個体ごとに変える理由が無ければ揃える）。
     */
    public static final int IDLE_TRACKING_DELAY_TICKS = 10;

    /**
     * 特殊系統の待機（tick）。**約6秒（仮の値）。** 実体系統（{@link #PHYSICAL_IDLE_TICKS}）とは
     * 別に、独立して並行に進む（§2「待ち時間」）。
     */
    public static final int SPECIAL_IDLE_TICKS = 120;

    // ------------------------------------------------------------ 素材

    private static final String BODY = "GRAY_CONCRETE";
    private static final String SWORD = "IRON_BLOCK";

    /**
     * 剣に攻撃を当てたときに通す割合。**実機で確認して決定**——剣の当たり判定が体に比べて
     * 大きく、ほとんどの攻撃が剣へ吸われてしまうため、免疫（0%）ではなく20%軽減とした
     * （§2「実装上の注意」）。
     */
    public static final double SWORD_DAMAGE_MULTIPLIER = 0.8;

    // ------------------------------------------------------------ 見た目の方式（騎士型と同じ仕組み）

    /**
     * リソースパックで描いたモデルを使うか（既定）。{@link KnightDefinition} と同じ仕組み。
     *
     * <p>false のあいだはバニラの素材を寸法どおりに引き伸ばして体型を示す。
     */
    public static final boolean AUTHORED_MODELS_DEFAULT = false;

    /**
     * いま描いたモデルを使うか。<b>実行中に切り替えられる。</b>
     * 検証用プラグインの {@code /raid model authored|vanilla} から切り替える
     * （騎士型と共通の1つのスイッチ。§2 参照）。
     */
    private static boolean authoredModels = AUTHORED_MODELS_DEFAULT;

    /** 描いたモデルを使っているか。 */
    public static boolean authoredModels() {
        return authoredModels;
    }

    /** 描いたモデルを使うかを切り替える。次に組む骨格から効く。 */
    public static void useAuthoredModels(boolean authored) {
        authoredModels = authored;
    }

    /** 描いたモデルを載せるアイテム。 */
    public static final String MODEL_ITEM = "PAPER";

    /**
     * 描いたモデルの縮小率。モデルの座標が −16〜32（3ブロック）に収まらない部位は、
     * この率で縮めて描き、描画側で戻す。剣（長さ3.0）だけが該当する
     * （{@link KnightDefinition#LONG_PART_MODEL_SCALE} と同じ考え方・同じ値）。
     */
    public static final double LONG_PART_MODEL_SCALE = 4.0;

    /** 見た目を、その時点の方式（バニラの素材 / 描いたモデル）に合わせる。 */
    private static Appearance look(Appearance vanilla) {
        if (!authoredModels) {
            return vanilla;
        }
        return vanilla.authoredAs(MODEL_ITEM,
                vanilla.fitsModelSpace() ? 1.0 : LONG_PART_MODEL_SCALE);
    }

    // ------------------------------------------------------------ 骨格

    /**
     * 骨格。積み上げは足元を 0 として、足 0〜1.0・胴 1.0〜1.9（中心1.45）・頭 1.9〜2.4（中心2.15）。
     * 剣は右腕の子で、騎士型の槍と同じ「腕が下がった姿勢で正面へ水平に構える」基準回転を持つ。
     *
     * <p><b>体（胴・頭・両腕・両足）の太さは2倍にしてある</b>（実機で確認して調整）。
     * 縦（Y、身長・手足の長さ）はそのままで、横（X・Z、太さ）だけを倍にした——
     * 「太くする」であって「大きくする／高くする」ではないため、全長（{@link #HEIGHT}）は
     * 変わらない。腕・足の付け根の横方向のずらしも、胴が太くなった分だけ倍にして、
     * 胴に埋もれないようにしてある。剣（武器）は体ではないため対象外。
     *
     * <p><b>剣に攻撃を当てると、通常の{@link #SWORD_DAMAGE_MULTIPLIER}（80%）だけ個体へ通る</b>
     * （{@link Rig.Part#reducedDamage}）。騎士型の槍は免疫（{@link Rig.Part#immune()}）だが、
     * 虚刃の衛士の剣は当たり判定が大きく、免疫のままだとほとんどの攻撃が素通りしてしまうため、
     * 完全な免疫ではなく軽減にしてある。
     */
    public static Rig rig() {
        List<Rig.Part> parts = List.of(
                part("胴", null, pos(0, 1.45, 0), 3001)
                        .looks(look(Appearance.box(BODY, 1.40, 0.90, 0.90))),
                part("頭", "胴", pos(0, 0.70, 0), 3002)
                        .looks(look(Appearance.box(BODY, 0.90, 0.45, 0.90))),
                part("右腕", "胴", pos(-0.90, 0.35, 0), 3003)
                        .looks(look(Appearance.limb(BODY, 0.70, 1.00, 0.70))),
                part("左腕", "胴", pos(0.90, 0.35, 0), 3004)
                        .looks(look(Appearance.limb(BODY, 0.70, 1.00, 0.70))),
                part("右足", "胴", pos(-0.40, -0.45, 0), 3005)
                        .looks(look(Appearance.limb(BODY, 0.80, 1.00, 0.80))),
                part("左足", "胴", pos(0.40, -0.45, 0), 3006)
                        .looks(look(Appearance.limb(BODY, 0.80, 1.00, 0.80))),
                part("剣", "右腕", posRot(0, -1.00, 0, -90, 0, 0), 3007)
                        .looks(look(Appearance.limb(SWORD, 0.30, SWORD_LENGTH, 0.30)))
                        .reducedDamage(SWORD_DAMAGE_MULTIPLIER));
        return new Rig(parts, HEIGHT, WIDTH);
    }

    // ------------------------------------------------------------ 個体

    public static RaidSpecies boss() {
        return new RaidSpecies("hollow_guard", "虚刃の衛士", BASE_HEALTH, rig(),
                List.of(phaseOne(), phaseTwo(), phaseThree()));
    }

    /** 第一形態（体力100〜67%）。**実体の大剣のみ**を使う。特殊系統はまだ解禁されない。 */
    public static RaidSpecies.Phase phaseOne() {
        var behavior = new RaidSpecies.Behavior(PHYSICAL_IDLE_TICKS, 20, MOVE_SPEED,
                idle(), walk());
        return new RaidSpecies.Phase("第一形態", 100,
                List.of(throwSweep(), upper(), shieldMash()),
                "実体の大剣のみ。特殊系統（浮遊する剣）は未解禁",
                null, behavior, rig());
    }

    /**
     * 第二形態（体力66%以下）。実体3種はそのまま、**特殊系統（浮遊する剣）が解禁される**
     * （§2「段階構成」）。特殊系統のモーションそのものは {@code MotionSpec} を使わず、
     * plugin 側の並行した状態機械（{@code tickSpecial()}）が駆動する——ここでの違いは
     * 段階の閾値だけであり、実体3種の内容は第一形態と同じである。
     */
    public static RaidSpecies.Phase phaseTwo() {
        var behavior = new RaidSpecies.Behavior(PHYSICAL_IDLE_TICKS, 20, MOVE_SPEED,
                idle(), walk());
        return new RaidSpecies.Phase("第二形態", 66,
                List.of(throwSweep(), upper(), shieldMash()),
                "実体の大剣 + 浮遊する剣。特殊系統（弾幕）が解禁",
                null, behavior, rig());
    }

    /**
     * 第三形態（体力33%以下）。実体3種・特殊系統は第二形態のまま、**特殊4種それぞれに
     * 金のオノが加わる**（{@link GoldenAxe}）。降り注ぐ刃・空間斬撃・串刺しはそれぞれの技の
     * 発動ごとに1本、全域大旋回だけは並び（金・銅・鉄・ダイヤモンド・ネザライト）に
     * オノが加わって周期が5→6になるぶん本数も増える（{@link GrandWhirl#totalCount}）。
     * オノは通常のオノと同じく、命中すると相手の盾を一時的に使えなくする。
     */
    public static RaidSpecies.Phase phaseThree() {
        var behavior = new RaidSpecies.Behavior(PHYSICAL_IDLE_TICKS, 20, MOVE_SPEED,
                idle(), walk());
        return new RaidSpecies.Phase("第三形態", 33,
                List.of(throwSweep(), upper(), shieldMash()),
                "実体の大剣 + 浮遊する剣・斧。特殊4種すべてに金のオノが加わる",
                null, behavior, rig());
    }

    // ------------------------------------------------------------ モーション

    /**
     * 投げ払い。大剣をほぼ一回転（300度）振り回し、周囲を一掃する薙ぎ払い。
     *
     * <p>Y回転を −150 度から +150 度へ<b>数値のまま</b>動かすことで、0度（正面）を経由する
     * 300度の弧を描く。騎士型のなぎ払い（240度・8tick）より大きく、長い分だけ時間も取る。
     */
    static MotionSpec throwSweep() {
        var damage = new MotionSpec.Damage(24, 30);
        Animation animation = animation("投げ払い", 34, false,
                "胴", List.of(rot(0, 0, 0, 0), rot(10, 0, -15, 0, SET),
                        rot(28, 0, 15, 0, STRIKE), rot(34, 0, 0, 0)),
                "右腕", List.of(rot(0, 0, 0, 0), rot(10, 0, -150, 0, SET),
                        rot(28, 0, 150, 0, STRIKE), rot(34, 0, 0, 0)));
        return new MotionSpec("投げ払い", animation, MotionSpec.Idle.of(PHYSICAL_IDLE_TICKS),
                Optional.empty(),
                List.of(new MotionSpec.DamageWindow("剣", 10, 28, damage)),
                Optional.empty(), Optional.empty(), Optional.empty(),
                Optional.empty(), Optional.empty(), false,
                MotionSpec.Usage.crowd(6.0, 1, 25, 60));
    }

    /**
     * アッパー。2ブロック前進しながら大剣を下から上へ振り上げる。強いノックバック。
     *
     * <p>前進は {@link MotionSpec.Charge} を流用する。加速はせず、8tick（振り上げの区間）で
     * ちょうど2ブロック進む一定速度にしてある。突進のような「止まらない」性質は持たない
     * （このモーションに {@link MotionSpec.Parry} は無い）。
     */
    static MotionSpec upper() {
        var damage = new MotionSpec.Damage(26, 32);
        var advance = MotionSpec.Charge.steady(8, 5.0, 2.0);
        Animation animation = animation("アッパー", 22, false,
                "右腕", List.of(rot(0, 0, 0, 0), rot(8, 50, 0, 0, SET),
                        rot(16, -70, 0, 0, STRIKE), rot(22, -10, 0, 0)));
        return new MotionSpec("アッパー", animation, MotionSpec.Idle.of(PHYSICAL_IDLE_TICKS),
                Optional.empty(),
                List.of(new MotionSpec.DamageWindow("剣", 8, 16, damage)),
                Optional.empty(),
                Optional.of(advance), Optional.empty(),
                Optional.of(new MotionSpec.Knockback(2.5, 4.0)), Optional.empty(), false,
                MotionSpec.Usage.at(0, 5.0, 30, 50));
    }

    /**
     * シールドマッシュ。剣を縦に構えて5ブロック体当たりする。剣は{@link Rig.Part#immune()}
     * であり、殴っても個体にダメージは通らない（騎士型の槍と同じ扱い）。
     *
     * <p><b>ダメージは軽く、主目的はノックバックによる押しのけである。</b>正面へ構えた剣が
     * 盾のように機能し、それ自体が判定源として当たったプレイヤーを押し出す。
     */
    static MotionSpec shieldMash() {
        var damage = new MotionSpec.Damage(10, 14);
        var advance = MotionSpec.Charge.steady(6, 7.0, 5.0);
        int chargeEnd = advance.endTick();
        Animation animation = animation("シールドマッシュ", chargeEnd + 6, false,
                "右腕", List.of(rot(0, 0, 0, 0), rot(6, 0, 0, -80, SET),
                        rot(chargeEnd, 0, 0, -80, HOLD), rot(chargeEnd + 6, 0, 0, 0)));
        return new MotionSpec("シールドマッシュ", animation, MotionSpec.Idle.of(PHYSICAL_IDLE_TICKS),
                Optional.empty(),
                List.of(new MotionSpec.DamageWindow("剣", 6, chargeEnd, damage)),
                Optional.empty(),
                Optional.of(advance), Optional.empty(),
                Optional.of(new MotionSpec.Knockback(0.5, 6.0)), Optional.empty(), false,
                MotionSpec.Usage.at(2.0, 8.0, 20, 70));
    }

    // ------------------------------------------------------------ 待機と歩行

    /**
     * <b>胴・頭だけの直立では機械的に見えるため、腕（剣ごと）と両足にも体重移動を
     * 足した</b>（実機の指摘「待機モーションを生物らしく」を受けて追加）。
     * ピークのtickを胴20・頭20・腕14/28・足10/30とずらし、全身が1つの時計に
     * きっちり同期して動く不自然さを避けている。剣は右腕の子なので、腕の揺れが
     * そのまま剣先のわずかな震えとして伝わる。
     */
    static Animation idle() {
        return animation("待機", 40, true,
                "胴", List.of(rot(0, 0, 0, 0), rot(20, -4, 0, 0), rot(40, 0, 0, 0)),
                "頭", List.of(rot(0, 0, 0, 0), rot(20, 0, 8, 0), rot(40, 0, 0, 0)),
                "右腕", List.of(rot(0, 0, 0, 0), rot(14, 2, 0, 1.5), rot(28, -1, 0, -1),
                        rot(40, 0, 0, 0)),
                "左腕", List.of(rot(0, 0, 0, 0), rot(14, -2, 0, -1.5), rot(28, 1, 0, 1),
                        rot(40, 0, 0, 0)),
                "右足", List.of(rot(0, 0, 0, 0), rot(30, -1.5, 0, 0), rot(40, 0, 0, 0)),
                "左足", List.of(rot(0, 0, 0, 0), rot(10, 1.5, 0, 0), rot(40, 0, 0, 0)));
    }

    static Animation walk() {
        return animation("歩行", 20, true,
                "右足", List.of(rot(0, 0, 0, 0), rot(5, -24, 0, 0), rot(10, 0, 0, 0),
                        rot(15, 24, 0, 0), rot(20, 0, 0, 0)),
                "左足", List.of(rot(0, 0, 0, 0), rot(5, 24, 0, 0), rot(10, 0, 0, 0),
                        rot(15, -24, 0, 0), rot(20, 0, 0, 0)),
                "右腕", List.of(rot(0, 0, 0, 0), rot(5, 0, 0, 10), rot(10, 0, 0, 0),
                        rot(15, 0, 0, -10), rot(20, 0, 0, 0)));
    }

    // ------------------------------------------------------------ 補助（KnightDefinition と同じ形）

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

    private static final Animation.Easing STRIKE = Animation.Easing.EASE_IN;
    private static final Animation.Easing SET = Animation.Easing.EASE_OUT;
    private static final Animation.Easing HOLD = Animation.Easing.LINEAR;

    private static Animation.Keyframe rot(int tick, double x, double y, double z) {
        return new Animation.Keyframe(tick, new Transform(Vec3.ZERO, new Vec3(x, y, z), Vec3.ONE));
    }

    private static Animation.Keyframe rot(int tick, double x, double y, double z,
                                          Animation.Easing easing) {
        return rot(tick, x, y, z).with(easing);
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
}
