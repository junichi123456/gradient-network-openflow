package jp.mcserver.plugin;

import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.World;
import org.bukkit.entity.BlockDisplay;
import org.bukkit.entity.Display;
import org.bukkit.util.Transformation;
import org.joml.Matrix4f;
import org.joml.Quaternionf;
import org.joml.Vector3f;

/**
 * 浮遊剣1本ぶんの表示（{@link BlockDisplay}）。
 *
 * <p>{@link BossRig} の部位ツリーには属さない、独立した実体である
 * （`raid_species.md` §2「実装上の注意」——追従元の部位が本体ではなく空中の一点になるため）。
 * したがって親子の変換合成は要らず、この実体1つが自分の位置と向きだけを持てばよい。
 *
 * <p>素材はバニラのブロックで代用する（例: 銅の剣 → COPPER_BLOCK）。実際の剣の形をした
 * 専用モデルはリソースパック側の作業として別途残る。既存の「剣」部位（{@code IRON_BLOCK}
 * で代用、`HollowGuardDefinition`）と同じ扱いである。
 */
final class BladeDisplay {

    private final BlockDisplay entity;
    private final float width;
    private final float length;

    /**
     * @param at       出現位置
     * @param material 代用するブロック素材
     * @param width    太さ（ブロック）
     * @param length   長さ（ブロック）。局所+Y軸に沿う
     */
    BladeDisplay(Location at, Material material, double width, double length) {
        this.width = (float) width;
        this.length = (float) length;
        World world = at.getWorld();
        this.entity = world.spawn(BossRig.upright(at), BlockDisplay.class, e -> {
            e.setBlock(material.createBlockData());
            e.setInterpolationDuration(1);
            e.setTeleportDuration(1);
            e.setPersistent(false);
            e.setBrightness(new Display.Brightness(15, 15));
            e.setViewRange(2.0f);
        });
    }

    /**
     * 中心位置と、長さの軸（局所+Y）が向く方向を与えて姿勢を更新する。
     * 落下する剣・旋回する剣のように、全長がつねに見えているものに使う。
     */
    void place(Location center, Vector3f direction) {
        entity.teleport(BossRig.upright(center));
        Vector3f dir = new Vector3f(direction).normalize();
        Quaternionf rotation = new Quaternionf().rotationTo(new Vector3f(0, 1, 0), dir);
        Matrix4f placed = new Matrix4f().rotate(rotation)
                .translate(-width / 2f, -length / 2f, -width / 2f);
        Vector3f translation = placed.getTranslation(new Vector3f());
        Quaternionf extracted = placed.getNormalizedRotation(new Quaternionf());
        entity.setTransformation(new Transformation(translation, extracted,
                new Vector3f(width, length, width), new Quaternionf()));
    }

    /**
     * 根本（{@code base}）から鉛直に {@code height} ブロックだけ生えた姿勢にする。
     * 「串刺し」のように、根本を固定して伸び縮みするものに使う。
     */
    void placeRising(Location base, double height) {
        entity.teleport(BossRig.upright(base));
        float h = (float) Math.max(0.02, height);
        Matrix4f placed = new Matrix4f().translate(-width / 2f, 0, -width / 2f);
        Vector3f translation = placed.getTranslation(new Vector3f());
        entity.setTransformation(new Transformation(translation, new Quaternionf(),
                new Vector3f(width, h, width), new Quaternionf()));
    }

    Location location() {
        return entity.getLocation();
    }

    void despawn() {
        entity.remove();
    }
}
