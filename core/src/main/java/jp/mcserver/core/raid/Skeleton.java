package jp.mcserver.core.raid;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * 骨格を世界座標へ展開する（§12.6）。
 *
 * <p>プラグインの表示・当たり判定と<b>同じ順序で合成する</b>。親の行列に
 * 「基準の平行移動＋モーションの平行移動」→「基準の回転＋モーションの回転（X→Y→Z）」
 * を右から掛ける。ここがずれると、実機の当たり判定と検証の値が食い違う。
 *
 * <p>原点は個体の足元、+Z が正面である。
 */
public final class Skeleton {

    private Skeleton() {
    }

    /** 部位ごとの当たり判定の点。長辺に沿って分割数ぶん並ぶ。 */
    public static Map<String, List<Vec3>> hitPoints(Rig rig, Map<String, Transform> sampled) {
        Map<String, List<Vec3>> points = new LinkedHashMap<>();
        walk(rig, rig.root().name(), new Mat4(), sampled, points);
        return points;
    }

    /** ある部位の当たり判定の点。持たない部位なら空。 */
    public static List<Vec3> pointsOf(Rig rig, Map<String, Transform> sampled, String part) {
        return hitPoints(rig, sampled).getOrDefault(part, List.of());
    }

    /** モーションをある tick でサンプリングして展開する。 */
    public static Map<String, List<Vec3>> hitPoints(Rig rig, Animation animation, int tick) {
        Map<String, Transform> sampled = new LinkedHashMap<>();
        for (String part : animation.animatedParts()) {
            sampled.put(part, animation.sample(part, tick));
        }
        return hitPoints(rig, sampled);
    }

    /**
     * 立っているプレイヤーに届く最大の水平距離。
     *
     * <p>プラグインの判定と同じ規則で測る。プレイヤーは足元から
     * {@link KnightDefinition#PLAYER_HEIGHT} までの<b>縦の線分</b>であり、
     * 武器の点からその線分までの距離が余裕以内なら当たる。
     * 高すぎて誰にも当たらない点は数えない。
     *
     * @param points       武器の当たり判定の点
     * @param weaponReach  1点あたりの判定距離
     */
    public static double reach(List<Vec3> points, double weaponReach) {
        double best = 0;
        for (Vec3 point : points) {
            // 縦の線分に落とすと、余裕のうち水平に使える量が決まる
            double vertical = Math.max(0,
                    Math.max(-point.y(), point.y() - KnightDefinition.PLAYER_HEIGHT));
            double slack = weaponReach * weaponReach - vertical * vertical;
            if (slack < 0) {
                continue;
            }
            best = Math.max(best, Math.hypot(point.x(), point.z()) + Math.sqrt(slack));
        }
        return best;
    }

    /**
     * 立っているプレイヤーに届く最も近い水平距離。
     *
     * <p>0 なら足元に密着していても当たる。突き技は槍の根元が前へ出るため、
     * ここが大きいと<b>懐に入られたときに空を突く</b>。
     */
    public static double nearReach(List<Vec3> points, double weaponReach) {
        double best = Double.MAX_VALUE;
        for (Vec3 point : points) {
            double vertical = Math.max(0,
                    Math.max(-point.y(), point.y() - KnightDefinition.PLAYER_HEIGHT));
            double slack = weaponReach * weaponReach - vertical * vertical;
            if (slack < 0) {
                continue;
            }
            best = Math.min(best,
                    Math.max(0, Math.hypot(point.x(), point.z()) - Math.sqrt(slack)));
        }
        return best == Double.MAX_VALUE ? Double.MAX_VALUE : best;
    }

    /**
     * 表示エンティティへ送るべき配置（§12.6）。
     *
     * @param translation 表示の位置（個体の原点からの相対）。部位の原点に見た目のずらしを足した点
     * @param rotationDeg 表示の回転（XYZ順のオイラー角、度）
     * @param scale       表示の拡大率。ブロック表示では寸法と一致する
     */
    public record Placement(String part, Vec3 translation, Vec3 rotationDeg, Vec3 scale) {
    }

    /**
     * 部位ごとの配置を求める。<b>プラグインが送っている値と1行ずつ突き合わせるための出力である。</b>
     *
     * <p>画面写真から角度を読み取るのは当てにならない。ここが実機と食い違っていれば、
     * 食い違っている部位と量がそのまま分かる。
     */
    public static List<Placement> placements(Rig rig, Map<String, Transform> sampled) {
        List<Placement> placements = new ArrayList<>();
        collect(rig, rig.root().name(), new Mat4(), sampled, placements);
        return placements;
    }

    /** モーションをある tick でサンプリングして配置を求める。 */
    public static List<Placement> placements(Rig rig, Animation animation, int tick) {
        Map<String, Transform> sampled = new LinkedHashMap<>();
        for (String part : animation.animatedParts()) {
            sampled.put(part, animation.sample(part, tick));
        }
        return placements(rig, sampled);
    }

    private static void collect(Rig rig, String name, Mat4 parent, Map<String, Transform> sampled,
                                List<Placement> out) {
        Rig.Part part = rig.part(name);
        Transform local = sampled.getOrDefault(name, Transform.IDENTITY);
        Mat4 world = new Mat4(parent).mul(local(part.base(), local));
        Appearance look = part.appearance();
        if (look != null) {
            Vec3 offset = look.block() ? look.offset() : Vec3.ZERO;
            out.add(new Placement(name,
                    new Mat4(world).translate(offset.x(), offset.y(), offset.z()).position(),
                    world.eulerXYZDegrees(), look.scale()));
        }
        for (String child : rig.partNames()) {
            Rig.Part candidate = rig.part(child);
            if (!candidate.isRoot() && candidate.parent().equals(name)) {
                collect(rig, child, world, sampled, out);
            }
        }
    }

    private static void walk(Rig rig, String name, Mat4 parent, Map<String, Transform> sampled,
                             Map<String, List<Vec3>> out) {
        Rig.Part part = rig.part(name);
        Transform local = sampled.getOrDefault(name, Transform.IDENTITY);
        Mat4 world = new Mat4(parent).mul(local(part.base(), local));
        Appearance look = part.appearance();
        if (look != null) {
            out.put(name, segmentPoints(part, look, world));
        }
        for (String child : rig.partNames()) {
            Rig.Part candidate = rig.part(child);
            if (!candidate.isRoot() && candidate.parent().equals(name)) {
                walk(rig, child, world, sampled, out);
            }
        }
    }

    /** 長辺に沿って判定を並べる位置。BossRig.segmentPoints と同じ式である。 */
    private static List<Vec3> segmentPoints(Rig.Part part, Appearance look, Mat4 world) {
        double sx = look.scale().x();
        double sy = look.scale().y();
        double sz = look.scale().z();
        int axis = (sy >= sx && sy >= sz) ? 1 : (sx >= sz ? 0 : 2);
        int count = part.hitboxSegments();
        List<Vec3> points = new ArrayList<>(count);
        for (int i = 0; i < count; i++) {
            double ratio = (i + 0.5) / count;
            points.add(new Mat4(world).translate(
                    look.offset().x() + sx * (axis == 0 ? ratio : 0.5),
                    look.offset().y() + sy * (axis == 1 ? ratio : 0.5),
                    look.offset().z() + sz * (axis == 2 ? ratio : 0.5)).position());
        }
        return points;
    }

    private static Mat4 local(Transform base, Transform motion) {
        Mat4 matrix = new Mat4();
        matrix.translate(base.translation().x() + motion.translation().x(),
                base.translation().y() + motion.translation().y(),
                base.translation().z() + motion.translation().z());
        matrix.rotateXYZ(Math.toRadians(base.rotationDeg().x() + motion.rotationDeg().x()),
                Math.toRadians(base.rotationDeg().y() + motion.rotationDeg().y()),
                Math.toRadians(base.rotationDeg().z() + motion.rotationDeg().z()));
        return matrix;
    }

    /** 列ベクトル・右から掛ける 4x4。JOML の Matrix4f と同じ規約。 */
    private static final class Mat4 {

        // m[列 * 4 + 行]
        private final double[] m = {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};

        Mat4() {
        }

        Mat4(Mat4 other) {
            System.arraycopy(other.m, 0, m, 0, 16);
        }

        Mat4 mul(Mat4 right) {
            double[] result = new double[16];
            for (int column = 0; column < 4; column++) {
                for (int row = 0; row < 4; row++) {
                    double sum = 0;
                    for (int k = 0; k < 4; k++) {
                        sum += m[k * 4 + row] * right.m[column * 4 + k];
                    }
                    result[column * 4 + row] = sum;
                }
            }
            System.arraycopy(result, 0, m, 0, 16);
            return this;
        }

        Mat4 translate(double x, double y, double z) {
            Mat4 t = new Mat4();
            t.m[12] = x;
            t.m[13] = y;
            t.m[14] = z;
            return mul(t);
        }

        Mat4 rotateXYZ(double x, double y, double z) {
            return mul(rotationX(x)).mul(rotationY(y)).mul(rotationZ(z));
        }

        private static Mat4 rotationX(double radians) {
            Mat4 r = new Mat4();
            double c = Math.cos(radians);
            double s = Math.sin(radians);
            r.m[5] = c;
            r.m[6] = s;
            r.m[9] = -s;
            r.m[10] = c;
            return r;
        }

        private static Mat4 rotationY(double radians) {
            Mat4 r = new Mat4();
            double c = Math.cos(radians);
            double s = Math.sin(radians);
            r.m[0] = c;
            r.m[2] = -s;
            r.m[8] = s;
            r.m[10] = c;
            return r;
        }

        private static Mat4 rotationZ(double radians) {
            Mat4 r = new Mat4();
            double c = Math.cos(radians);
            double s = Math.sin(radians);
            r.m[0] = c;
            r.m[1] = s;
            r.m[4] = -s;
            r.m[5] = c;
            return r;
        }

        Vec3 position() {
            return new Vec3(m[12], m[13], m[14]);
        }

        /**
         * 回転を XYZ順のオイラー角（度）で取り出す。
         *
         * <p>M = Rx·Ry·Rz を前提とする。JOML の {@code getEulerAnglesXYZ} と同じ規約である。
         */
        Vec3 eulerXYZDegrees() {
            double m00 = m[0];
            double m01 = m[4];
            double m02 = m[8];
            double m12 = m[9];
            double m22 = m[10];
            double y = Math.asin(Math.max(-1, Math.min(1, m02)));
            double x = Math.atan2(-m12, m22);
            double z = Math.atan2(-m01, m00);
            return new Vec3(Math.toDegrees(x), Math.toDegrees(y), Math.toDegrees(z));
        }
    }
}
