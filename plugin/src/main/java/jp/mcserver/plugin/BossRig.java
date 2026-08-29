package jp.mcserver.plugin;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import jp.mcserver.core.raid.Appearance;
import jp.mcserver.core.raid.Rig;
import jp.mcserver.core.raid.Transform;
import org.bukkit.Color;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.World;
import org.bukkit.entity.BlockDisplay;
import org.bukkit.entity.Display;
import org.bukkit.entity.Interaction;
import org.bukkit.entity.ItemDisplay;
import org.bukkit.inventory.ItemStack;
import org.bukkit.inventory.meta.ItemMeta;
import org.bukkit.util.Transformation;
import org.joml.Matrix4f;
import org.joml.Quaternionf;
import org.joml.Vector3f;

/**
 * 骨格（{@link Rig}）を表示エンティティとして具現化する（§12.6 方式A）。
 *
 * <p>部位ごとに表示エンティティを1体持ち、当たり判定を持つ部位には {@link Interaction} を重ねる。
 * 親子の変換合成はここで行う（コア層は構造の妥当性のみを扱う）。
 *
 * <p>当たり判定は<b>軸に沿った直方体しか取れず、回転もできない</b>。槍のように細長く傾く部位は、
 * 部位の長辺に沿って複数の判定を並べて表す（{@link Rig.Part#hitboxSegments()}）。
 * 判定を1つで済ませると、3.4ブロックの槍が他の部位と同じ大きさの箱になってしまう。
 */
final class BossRig {

    /** 変換の更新間隔（tick）。補間時間と揃える（§12.6）。 */
    static final int UPDATE_INTERVAL = 2;

    /** 当たり判定の最小の辺（ブロック）。細すぎると狙って当てられない。 */
    private static final double MIN_HITBOX = 0.42;

    /** 弱点が露出しているときの発光色。 */
    private static final Color EXPOSED_COLOR = Color.fromRGB(0xFF, 0x30, 0x30);

    private final Rig rig;
    private final Map<String, Display> displays = new LinkedHashMap<>();
    private final Map<String, List<Interaction>> hitboxes = new LinkedHashMap<>();
    private final Map<String, List<Vector3f>> segmentCenters = new LinkedHashMap<>();
    private final Map<String, Vector3f> centers = new LinkedHashMap<>();
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
            Appearance look = part.appearance();

            Display display;
            if (look != null && look.block()) {
                display = world.spawn(origin, BlockDisplay.class,
                        entity -> entity.setBlock(material(look.material()).createBlockData()));
            } else {
                display = world.spawn(origin, ItemDisplay.class,
                        entity -> entity.setItemStack(item(part)));
            }
            display.setInterpolationDuration(UPDATE_INTERVAL);
            display.setTeleportDuration(UPDATE_INTERVAL);
            display.setPersistent(false);
            display.setBrightness(new Display.Brightness(15, 15));
            display.setViewRange(2.0f);
            displays.put(name, display);

            if (part.decoration()) {
                continue;
            }
            Vector3f size = segmentSize(part);
            float width = (float) Math.max(MIN_HITBOX, Math.max(size.x(), size.z()));
            float height = (float) Math.max(MIN_HITBOX, size.y());
            List<Interaction> segments = new ArrayList<>();
            for (int i = 0; i < part.hitboxSegments(); i++) {
                segments.add(world.spawn(origin, Interaction.class, entity -> {
                    entity.setInteractionWidth(width);
                    entity.setInteractionHeight(height);
                    entity.setResponsive(true);
                    entity.setPersistent(false);
                }));
            }
            hitboxes.put(name, segments);
        }
        pose(Transform.IDENTITY, 0);
    }

    /** 生成した実体をすべて除去する。討伐・失敗・停止のいずれでも呼ぶ。 */
    void despawn() {
        displays.values().forEach(Display::remove);
        hitboxes.values().forEach(list -> list.forEach(Interaction::remove));
        displays.clear();
        hitboxes.clear();
        segmentCenters.clear();
        centers.clear();
    }

    /** 部位の中心のワールド座標。演出の発生点に使う。 */
    Location centerOf(String name) {
        Vector3f center = centers.get(name);
        if (center == null) {
            return origin();
        }
        return origin.clone().add(center.x(), center.y(), center.z());
    }

    /** 当たり判定の実体が、どの部位に対応するかを返す。 */
    String partOfHitbox(UUID entityId) {
        for (Map.Entry<String, List<Interaction>> entry : hitboxes.entrySet()) {
            for (Interaction hitbox : entry.getValue()) {
                if (hitbox.getUniqueId().equals(entityId)) {
                    return entry.getKey();
                }
            }
        }
        return null;
    }

    /** 弱点の発光を切り替える。露出していることを見た目で伝える（§12.6）。 */
    void setExposed(boolean exposed) {
        for (Rig.Part part : rig.weakPoints()) {
            Display display = displays.get(part.name());
            if (display == null) {
                continue;
            }
            display.setGlowing(exposed);
            display.setGlowColorOverride(exposed ? EXPOSED_COLOR : null);
        }
    }

    /** 個体を移動させる。表示エンティティは補間で追従する。 */
    void moveTo(Location location) {
        this.origin = location.clone();
        displays.values().forEach(display -> display.teleport(origin));
        // 姿勢の更新は更新間隔ごとだが、当たり判定は毎tick追従させる。
        // ここで一括して原点へ寄せてしまうと、更新の谷にあたるtickで全部位の判定が重なる
        syncHitboxes();
    }

    /** 直前に求めた部位ごとの判定位置を、現在の原点へ追従させる。 */
    private void syncHitboxes() {
        for (Map.Entry<String, List<Interaction>> entry : hitboxes.entrySet()) {
            List<Vector3f> points = segmentCenters.get(entry.getKey());
            if (points == null) {
                continue;
            }
            float height = (float) Math.max(MIN_HITBOX, segmentSize(rig.part(entry.getKey())).y());
            List<Interaction> segments = entry.getValue();
            for (int i = 0; i < segments.size() && i < points.size(); i++) {
                Vector3f point = points.get(i);
                segments.get(i).teleport(origin.clone().add(point.x(),
                        point.y() - height / 2, point.z()));
            }
        }
    }

    /**
     * 姿勢を適用する。
     *
     * @param bodyYawDegrees 胴体の向き（度）
     */
    void pose(Transform rootPose, double bodyYawDegrees) {
        applyMotion(Map.of(rig.root().name(), rootPose), bodyYawDegrees);
    }

    /** 特定の部位の姿勢を差し替える（モーションのサンプリング結果を反映する）。 */
    void applyMotion(Map<String, Transform> sampled, double bodyYawDegrees) {
        Matrix4f base = new Matrix4f().rotateY((float) Math.toRadians(-bodyYawDegrees));
        walk(rig.root().name(), base, sampled);
        syncHitboxes();
    }

    private void walk(String partName, Matrix4f parent, Map<String, Transform> sampled) {
        Rig.Part part = rig.part(partName);
        Transform local = sampled.getOrDefault(partName, Transform.IDENTITY);
        Matrix4f world = new Matrix4f(parent).mul(toMatrix(part.base(), local));

        Display display = displays.get(partName);
        if (display != null) {
            Vector3f size = scaleOf(part);
            Vector3f offset = offsetOf(part);

            Matrix4f model = new Matrix4f(world).translate(offset).scale(size);
            Vector3f translation = model.getTranslation(new Vector3f());
            Quaternionf rotation = model.getNormalizedRotation(new Quaternionf());

            display.setInterpolationDelay(0);
            display.setTransformation(new Transformation(
                    translation, rotation, size, new Quaternionf()));

            centers.put(partName, new Matrix4f(world)
                    .translate(offset.x() + size.x() / 2, offset.y() + size.y() / 2,
                            offset.z() + size.z() / 2)
                    .getTranslation(new Vector3f()));
            segmentCenters.put(partName, segmentPoints(part, world, size, offset));
        }

        for (String name : rig.partNames()) {
            Rig.Part child = rig.part(name);
            if (!child.isRoot() && child.parent().equals(partName)) {
                walk(name, world, sampled);
            }
        }
    }

    /** 部位の長辺に沿って、判定を並べる位置を求める。 */
    private static List<Vector3f> segmentPoints(Rig.Part part, Matrix4f world,
                                                Vector3f size, Vector3f offset) {
        int count = part.hitboxSegments();
        int axis = longestAxis(size);
        List<Vector3f> points = new ArrayList<>(count);
        for (int i = 0; i < count; i++) {
            float ratio = (i + 0.5f) / count;
            float x = offset.x() + size.x() * (axis == 0 ? ratio : 0.5f);
            float y = offset.y() + size.y() * (axis == 1 ? ratio : 0.5f);
            float z = offset.z() + size.z() * (axis == 2 ? ratio : 0.5f);
            points.add(new Matrix4f(world).translate(x, y, z).getTranslation(new Vector3f()));
        }
        return points;
    }

    /** 判定1つぶんの寸法。長辺だけを分割数で割る。 */
    private static Vector3f segmentSize(Rig.Part part) {
        Vector3f size = scaleOf(part);
        int count = part.hitboxSegments();
        if (count <= 1) {
            return size;
        }
        int axis = longestAxis(size);
        return new Vector3f(
                axis == 0 ? size.x() / count : size.x(),
                axis == 1 ? size.y() / count : size.y(),
                axis == 2 ? size.z() / count : size.z());
    }

    private static int longestAxis(Vector3f size) {
        if (size.y() >= size.x() && size.y() >= size.z()) {
            return 1;
        }
        return size.x() >= size.z() ? 0 : 2;
    }

    private static Vector3f scaleOf(Rig.Part part) {
        Appearance look = part.appearance();
        if (look == null) {
            return new Vector3f(1, 1, 1);
        }
        return new Vector3f((float) look.scale().x(), (float) look.scale().y(),
                (float) look.scale().z());
    }

    private static Vector3f offsetOf(Rig.Part part) {
        Appearance look = part.appearance();
        if (look == null) {
            return new Vector3f(0, 0, 0);
        }
        return new Vector3f((float) look.offset().x(), (float) look.offset().y(),
                (float) look.offset().z());
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
        return matrix;
    }

    /** 素材名を解決する。未知の名前でも落ちないよう既定へ倒す。 */
    private static Material material(String name) {
        Material material = Material.matchMaterial(name);
        return material == null ? Material.WHITE_CONCRETE : material;
    }

    /** アイテム表示の中身。見た目の指定が無ければリソースパック用の紙を出す。 */
    private static ItemStack item(Rig.Part part) {
        Appearance look = part.appearance();
        if (look == null) {
            ItemStack stack = new ItemStack(Material.PAPER);
            ItemMeta meta = stack.getItemMeta();
            meta.setCustomModelData(part.modelId());
            stack.setItemMeta(meta);
            return stack;
        }
        return new ItemStack(material(look.material()));
    }
}
