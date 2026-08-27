package jp.mcserver.plugin;

import java.util.LinkedHashMap;
import java.util.Map;
import jp.mcserver.core.raid.Rig;
import jp.mcserver.core.raid.Transform;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.World;
import org.bukkit.entity.Display;
import org.bukkit.entity.Interaction;
import org.bukkit.entity.ItemDisplay;
import org.bukkit.inventory.ItemStack;
import org.bukkit.inventory.meta.ItemMeta;
import org.bukkit.util.Transformation;
import org.joml.AxisAngle4f;
import org.joml.Matrix4f;
import org.joml.Quaternionf;
import org.joml.Vector3f;

/**
 * 骨格（{@link Rig}）を表示エンティティとして具現化する（§12.6 方式A）。
 *
 * <p>部位ごとに {@link ItemDisplay} を1体持ち、被弾する部位には {@link Interaction} を重ねる。
 * 親子の変換合成はここで行う（コア層は構造の妥当性のみを扱う）。
 */
final class BossRig {

    /** 変換の更新間隔（tick）。補間時間と揃える（§12.6）。 */
    static final int UPDATE_INTERVAL = 2;

    private final Rig rig;
    private final Map<String, ItemDisplay> displays = new LinkedHashMap<>();
    private final Map<String, Interaction> hitboxes = new LinkedHashMap<>();
    private Location origin;

    BossRig(Rig rig, Location origin) {
        this.rig = rig;
        this.origin = origin.clone();
    }

    Rig rig() {
        return rig;
    }

    Location origin() {
        return origin.clone();
    }

    /** 部位ぶんの表示エンティティと当たり判定を生成する。 */
    void spawn() {
        World world = origin.getWorld();
        for (String name : rig.partNames()) {
            Rig.Part part = rig.part(name);
            ItemDisplay display = world.spawn(origin, ItemDisplay.class, entity -> {
                entity.setItemStack(model(part.modelId()));
                entity.setInterpolationDuration(UPDATE_INTERVAL);
                entity.setTeleportDuration(UPDATE_INTERVAL);
                entity.setPersistent(false);
            });
            displays.put(name, display);

            Interaction hitbox = world.spawn(origin, Interaction.class, entity -> {
                entity.setInteractionWidth(0.8f);
                entity.setInteractionHeight(0.8f);
                entity.setResponsive(true);
                entity.setPersistent(false);
            });
            hitboxes.put(name, hitbox);
        }
        pose(Transform.IDENTITY, 0);
    }

    /** 生成した実体をすべて除去する。討伐・失敗・停止のいずれでも呼ぶ。 */
    void despawn() {
        displays.values().forEach(Display::remove);
        hitboxes.values().forEach(Interaction::remove);
        displays.clear();
        hitboxes.clear();
    }

    /** 当たり判定の実体が、どの部位に対応するかを返す。 */
    String partOfHitbox(java.util.UUID entityId) {
        for (Map.Entry<String, Interaction> entry : hitboxes.entrySet()) {
            if (entry.getValue().getUniqueId().equals(entityId)) {
                return entry.getKey();
            }
        }
        return null;
    }

    /** 個体を移動させる。表示エンティティは補間で追従する。 */
    void moveTo(Location location) {
        this.origin = location.clone();
        displays.values().forEach(display -> display.teleport(origin));
        hitboxes.values().forEach(hitbox -> hitbox.teleport(origin));
    }

    /**
     * 姿勢を適用する。
     *
     * @param bodyYawDegrees 胴体の向き（度）
     */
    void pose(Transform rootPose, double bodyYawDegrees) {
        Matrix4f base = new Matrix4f().rotateY((float) Math.toRadians(-bodyYawDegrees));
        applyChain(rig.root().name(), base, rootPose);
    }

    /** 特定の部位の姿勢を差し替える（モーションのサンプリング結果を反映する）。 */
    void applyMotion(Map<String, Transform> sampled, double bodyYawDegrees) {
        Matrix4f base = new Matrix4f().rotateY((float) Math.toRadians(-bodyYawDegrees));
        walk(rig.root().name(), base, sampled);
    }

    private void applyChain(String partName, Matrix4f parent, Transform override) {
        walk(partName, parent, Map.of(partName, override));
    }

    private void walk(String partName, Matrix4f parent, Map<String, Transform> sampled) {
        Rig.Part part = rig.part(partName);
        Transform local = sampled.getOrDefault(partName, part.base());
        Matrix4f world = new Matrix4f(parent).mul(toMatrix(part.base(), local));

        ItemDisplay display = displays.get(partName);
        if (display != null) {
            Vector3f translation = world.getTranslation(new Vector3f());
            Quaternionf rotation = world.getNormalizedRotation(new Quaternionf());
            display.setInterpolationDelay(0);
            display.setTransformation(new Transformation(
                    translation, rotation, new Vector3f(1, 1, 1), new Quaternionf()));
        }
        Interaction hitbox = hitboxes.get(partName);
        if (hitbox != null) {
            Vector3f translation = world.getTranslation(new Vector3f());
            hitbox.teleport(origin.clone().add(translation.x(), translation.y(), translation.z()));
        }

        for (String name : rig.partNames()) {
            Rig.Part child = rig.part(name);
            if (!child.isRoot() && child.parent().equals(partName)) {
                walk(name, world, sampled);
            }
        }
    }

    /** 静止時の変換にモーションの変換を重ねた行列。 */
    private static Matrix4f toMatrix(Transform base, Transform motion) {
        Matrix4f matrix = new Matrix4f();
        matrix.translate((float) (base.translation().x() + motion.translation().x()),
                (float) (base.translation().y() + motion.translation().y()),
                (float) (base.translation().z() + motion.translation().z()));
        matrix.rotateXYZ(
                (float) Math.toRadians(base.rotationDeg().x() + motion.rotationDeg().x()),
                (float) Math.toRadians(base.rotationDeg().y() + motion.rotationDeg().y()),
                (float) Math.toRadians(base.rotationDeg().z() + motion.rotationDeg().z()));
        matrix.scale((float) (base.scale().x() * motion.scale().x()),
                (float) (base.scale().y() * motion.scale().y()),
                (float) (base.scale().z() * motion.scale().z()));
        return matrix;
    }

    /** リソースパック未適用でも位置と動きが見えるよう、既定の見た目で出す。 */
    private static ItemStack model(int modelId) {
        ItemStack stack = new ItemStack(Material.PAPER);
        ItemMeta meta = stack.getItemMeta();
        meta.setCustomModelData(modelId);
        stack.setItemMeta(meta);
        return stack;
    }

    /** 回転を持たない単純な姿勢。 */
    static Transform rotationY(double degrees) {
        return new Transform(jp.mcserver.core.raid.Vec3.ZERO,
                new jp.mcserver.core.raid.Vec3(0, degrees, 0), jp.mcserver.core.raid.Vec3.ONE);
    }

    /** 未使用の警告を避けるための補助。 */
    static AxisAngle4f unusedMarker() {
        return new AxisAngle4f();
    }
}
