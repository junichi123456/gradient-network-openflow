package jp.mcserver.core.raid;

/**
 * 部位の変換（§12.6）。
 *
 * @param translation 親からの相対位置（ブロック）
 * @param rotationDeg 回転（度、XYZ順）
 * @param scale       拡縮
 */
public record Transform(Vec3 translation, Vec3 rotationDeg, Vec3 scale) {

    public static final Transform IDENTITY = new Transform(Vec3.ZERO, Vec3.ZERO, Vec3.ONE);

    /** 線形補間。回転は角度成分ごとに補間する。 */
    public Transform lerp(Transform to, double t) {
        return new Transform(
                translation.lerp(to.translation, t),
                rotationDeg.lerp(to.rotationDeg, t),
                scale.lerp(to.scale, t));
    }

    /** 変換が同一か（補間の省略判定に用いる）。 */
    public boolean sameAs(Transform other, double epsilon) {
        return near(translation, other.translation, epsilon)
                && near(rotationDeg, other.rotationDeg, epsilon)
                && near(scale, other.scale, epsilon);
    }

    private static boolean near(Vec3 a, Vec3 b, double e) {
        return Math.abs(a.x() - b.x()) <= e && Math.abs(a.y() - b.y()) <= e
                && Math.abs(a.z() - b.z()) <= e;
    }
}
