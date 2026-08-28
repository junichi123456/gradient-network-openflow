package jp.mcserver.plugin;

import java.util.LinkedHashMap;
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
 * <p>見た目は {@link Appearance} に従う。リソースパックが無い状態でも体型が分かるよう、
 * バニラのブロックとアイテムを寸法どおりに拡大して並べる。
 */
final class BossRig {

    /** 変換の更新間隔（tick）。補間時間と揃える（§12.6）。 */
    static final int UPDATE_INTERVAL = 2;

    /** 弱点が露出しているときの発光色。 */
    private static final Color EXPOSED_COLOR = Color.fromRGB(0xFF, 0x30, 0x30);

    /** 装甲が残り少ないときの発光色。 */
    private static final Color CRACKED_COLOR = Color.fromRGB(0xFF, 0xC0, 0x40);

    private final Rig rig;
    private final Map<String, Display> displays = new LinkedHashMap<>();
    private final Map<String, Interaction> hitboxes = new LinkedHashMap<>();
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
            Vector3f size = scaleOf(part);
            Interaction hitbox = world.spawn(origin, Interaction.class, entity -> {
                entity.setInteractionWidth((float) Math.max(0.5, Math.max(size.x(), size.z())));
                entity.setInteractionHeight((float) Math.max(0.5, size.y()));
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
        centers.clear();
    }

    /** 破壊された部位を消す。子の部位もまとめて消える。 */
    void breakPart(String name) {
        for (String candidate : rig.partNames()) {
            if (rig.chain(candidate).stream().anyMatch(part -> part.name().equals(name))) {
                Display display = displays.remove(candidate);
                if (display != null) {
                    display.remove();
                }
                Interaction hitbox = hitboxes.remove(candidate);
                if (hitbox != null) {
                    hitbox.remove();
                }
            }
        }
    }

    /** その部位がまだ残っているか。 */
    boolean hasPart(String name) {
        return displays.containsKey(name);
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
        for (Map.Entry<String, Interaction> entry : hitboxes.entrySet()) {
            if (entry.getValue().getUniqueId().equals(entityId)) {
                return entry.getKey();
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

    /** 装甲に亀裂が入っていることを発光で伝える。 */
    void setCracked(String part, boolean cracked) {
        Display display = displays.get(part);
        if (display == null) {
            return;
        }
        display.setGlowing(cracked);
        display.setGlowColorOverride(cracked ? CRACKED_COLOR : null);
    }

    /** 個体を移動させる。表示エンティティは補間で追従する。 */
    void moveTo(Location location) {
        this.origin = location.clone();
        displays.values().forEach(display -> display.teleport(origin));
        // 姿勢の更新は更新間隔ごとだが、当たり判定は毎tick追従させる。
        // ここで一括して原点へ寄せてしまうと、更新の谷にあたるtickで全部位の判定が重なる
        syncHitboxes();
    }

    /** 直前に求めた部位の中心を使い、当たり判定だけを現在の原点へ追従させる。 */
    private void syncHitboxes() {
        for (Map.Entry<String, Interaction> entry : hitboxes.entrySet()) {
            Vector3f center = centers.get(entry.getKey());
            if (center == null) {
                continue;
            }
            float height = scaleOf(rig.part(entry.getKey())).y();
            entry.getValue().teleport(origin.clone().add(center.x(),
                    center.y() - height / 2, center.z()));
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
    }

    private void walk(String partName, Matrix4f parent, Map<String, Transform> sampled) {
        Rig.Part part = rig.part(partName);
        Transform local = sampled.getOrDefault(partName, Transform.IDENTITY);
        Matrix4f world = new Matrix4f(parent).mul(toMatrix(part.base(), local));

        Display display = displays.get(partName);
        if (display != null) {
            Vector3f size = scaleOf(part);
            Vector3f offset = offsetOf(part);

            Matrix4f model = new Matrix4f(world)
                    .translate(offset)
                    .scale(size);
            Vector3f translation = model.getTranslation(new Vector3f());
            Quaternionf rotation = model.getNormalizedRotation(new Quaternionf());

            display.setInterpolationDelay(0);
            display.setTransformation(new Transformation(
                    translation, rotation, size, new Quaternionf()));

            Vector3f center = new Matrix4f(world)
                    .translate(offset.x() + size.x() / 2, offset.y() + size.y() / 2,
                            offset.z() + size.z() / 2)
                    .getTranslation(new Vector3f());
            centers.put(partName, center);

            Interaction hitbox = hitboxes.get(partName);
            if (hitbox != null) {
                hitbox.teleport(origin.clone().add(center.x(),
                        center.y() - size.y() / 2, center.z()));
            }
        }

        for (String name : rig.partNames()) {
            Rig.Part child = rig.part(name);
            if (!child.isRoot() && child.parent().equals(partName)) {
                walk(name, world, sampled);
            }
        }
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
