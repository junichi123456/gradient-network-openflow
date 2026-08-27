package jp.mcserver.core.raid;

/** 3成分のベクトル。位置・回転（度）・拡縮に用いる。 */
public record Vec3(double x, double y, double z) {

    public static final Vec3 ZERO = new Vec3(0, 0, 0);
    public static final Vec3 ONE = new Vec3(1, 1, 1);

    /** 線形補間。 */
    public Vec3 lerp(Vec3 to, double t) {
        return new Vec3(x + (to.x - x) * t, y + (to.y - y) * t, z + (to.z - z) * t);
    }

    public Vec3 plus(Vec3 other) {
        return new Vec3(x + other.x, y + other.y, z + other.z);
    }
}
