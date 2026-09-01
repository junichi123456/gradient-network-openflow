package jp.mcserver.core.raid;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;

/**
 * リソースパックの雛形を骨格データから生成する（`raid_model_spec.md`）。
 *
 * <p><b>寸法表を手で写さない。</b>骨格の宣言から直接モデルを書き出すため、
 * 当たり判定と見た目の寸法が食い違わない。骨格を変えたら再生成すればよい。
 *
 * <p>生成するもの
 * <ul>
 *   <li>部位ごとの箱モデル（{@code assets/minecraft/models/knight/p1|p2/*.json}）</li>
 *   <li>{@code custom_model_data} からの振り分け（{@code assets/minecraft/items/paper.json}）</li>
 * </ul>
 *
 * <p>テクスチャは<b>バニラのブロックを指している</b>。差し替えるときは、モデルの
 * {@code "skin"} の1行を自分のテクスチャのパスへ変えるだけでよい。
 * 生成した JSON は Blockbench でそのまま開けるので、形も UV も視覚的に直せる。
 *
 * <p>実行: {@code java -cp out jp.mcserver.core.raid.ModelPack [出力先]}
 */
public final class ModelPack {

    private ModelPack() {
    }

    /** 部位名 → モデルのファイル名。日本語をパスに出さない。 */
    private static final Map<String, String> NAMES = names();

    /** 素材名 → 代用テクスチャ（バニラ）。自分のテクスチャに差し替える前の見た目。 */
    private static final Map<String, String> TEXTURES = textures();

    /** 槍の絞りを何段の箱で近似するか。1つのモデルの中で完結するので表示実体は増えない。 */
    private static final int SPEAR_STEPS = 8;

    public static void main(String[] args) throws IOException {
        Path root = Path.of(args.length > 0 ? args[0] : "../resourcepack");
        Path models = root.resolve("assets/minecraft/models/knight");
        Path items = root.resolve("assets/minecraft/items");
        Files.createDirectories(items);

        RaidSpecies boss = KnightDefinition.boss();
        // threshold の昇順に並べる必要があるため、ID を鍵にした木で集める
        Map<Integer, String> dispatch = new TreeMap<>();
        dispatch.put(9000, "knight/calibration");

        int written = 0;
        for (int index = 0; index < boss.phases().size(); index++) {
            String phase = "p" + (index + 1);
            Path directory = models.resolve(phase);
            Files.createDirectories(directory);
            Rig rig = boss.phases().get(index).rig().orElseThrow();
            for (String partName : rig.partNames()) {
                Rig.Part part = rig.part(partName);
                if (part.appearance() == null) {
                    continue;
                }
                String file = NAMES.getOrDefault(partName, "part_" + part.modelId());
                Files.writeString(directory.resolve(file + ".json"),
                        model(partName, part), StandardCharsets.UTF_8);
                dispatch.put(part.modelId(), "knight/" + phase + "/" + file);
                written++;
            }
        }

        Files.writeString(items.resolve("paper.json"), dispatch(dispatch),
                StandardCharsets.UTF_8);
        System.out.println("モデル " + written + " 件と振り分け " + dispatch.size()
                + " 件を書き出した: " + root.toAbsolutePath().normalize());
    }

    // ------------------------------------------------------------------ モデル

    /**
     * 部位1つぶんのモデル。
     *
     * <p>原点はモデル座標 <b>(8, 8, 8)</b>（実機で較正済み。§7）。部位の宣言が持つ
     * ずらしをそのまま単位へ写すので、中心合わせの部位は中心に、
     * 付け根合わせの部位は原点から下へ伸びる形になる。
     */
    private static String model(String partName, Rig.Part part) {
        Appearance look = part.appearance();
        double unit = 16 / modelDivisor(look);
        StringBuilder json = new StringBuilder();
        json.append("{\n");
        json.append("  \"__comment\": \"").append(partName).append(" / ID ")
                .append(part.modelId()).append(" / 自動生成（core の ModelPack）\",\n");
        json.append("  \"textures\": {\n");
        json.append("    \"skin\": \"").append(texture(look)).append("\",\n");
        json.append("    \"particle\": \"#skin\"\n");
        json.append("  },\n");
        json.append("  \"elements\": [\n");

        List<String> elements = new ArrayList<>();
        if (look.tapered()) {
            elements.addAll(taperElements(look, unit));
        } else {
            elements.add(box(partName, look.drawOffset(), look.scale(), unit));
        }
        json.append(String.join(",\n", elements)).append("\n");
        json.append("  ]\n");
        json.append("}\n");
        return json.toString();
    }

    /**
     * 絞りのある部位を箱の積み重ねで近似する。
     *
     * <p>Minecraft のモデルは軸に沿った箱しか持てないため、真の角錐は描けない。
     * 段を増やして近づける。<b>1つのモデルの中で完結する</b>ので、
     * バニラ素材のときのように表示実体が増えることはない。
     */
    private static List<String> taperElements(Appearance look, double unit) {
        List<String> elements = new ArrayList<>();
        double length = look.scale().y() / SPEAR_STEPS;
        for (int step = 0; step < SPEAR_STEPS; step++) {
            // 手元（原点側）が太い。step=0 が手元
            double ratio = 1 + (look.taper() - 1) * ((double) step / (SPEAR_STEPS - 1));
            Vec3 size = new Vec3(look.scale().x() * ratio, length, look.scale().z() * ratio);
            // 付け根合わせなので、原点から下へ段を積む
            double top = look.offset().y() + look.scale().y() - length * step;
            Vec3 offset = new Vec3(-size.x() / 2, top - length, -size.z() / 2);
            elements.add(box("絞り " + (step + 1) + "/" + SPEAR_STEPS, offset, size, unit));
        }
        return elements;
    }

    private static String box(String name, Vec3 offset, Vec3 size, double unit) {
        double[] from = {
                8 + offset.x() * unit, 8 + offset.y() * unit, 8 + offset.z() * unit};
        double[] to = {
                from[0] + size.x() * unit, from[1] + size.y() * unit, from[2] + size.z() * unit};
        StringBuilder element = new StringBuilder();
        element.append("    {\n");
        element.append("      \"name\": \"").append(name).append("\",\n");
        element.append("      \"from\": ").append(point(from)).append(",\n");
        element.append("      \"to\": ").append(point(to)).append(",\n");
        element.append("      \"faces\": {\n");
        String[] faces = {"north", "east", "south", "west", "up", "down"};
        List<String> rendered = new ArrayList<>();
        for (String face : faces) {
            rendered.add("        \"" + face
                    + "\": { \"uv\": [0, 0, 16, 16], \"texture\": \"#skin\" }");
        }
        element.append(String.join(",\n", rendered)).append("\n");
        element.append("      }\n");
        element.append("    }");
        return element.toString();
    }

    private static String point(double[] values) {
        return String.format("[%s, %s, %s]",
                trim(values[0]), trim(values[1]), trim(values[2]));
    }

    /** 余計な小数を出さない。Blockbench で開いたときに読みやすい。 */
    private static String trim(double value) {
        double rounded = Math.round(value * 1000) / 1000.0;
        return rounded == Math.rint(rounded)
                ? String.valueOf((long) rounded) : String.valueOf(rounded);
    }

    /**
     * 描く縮尺の分母。
     *
     * <p>モデルの座標は −16〜32（3ブロック）しか取れない。超える部位は縮めて描き、
     * 描画側が {@link KnightDefinition#LONG_PART_MODEL_SCALE} 倍に戻す。
     */
    private static double modelDivisor(Appearance look) {
        return look.fitsModelSpace() ? 1.0 : KnightDefinition.LONG_PART_MODEL_SCALE;
    }

    private static String texture(Appearance look) {
        return TEXTURES.getOrDefault(look.material(), "minecraft:block/white_concrete");
    }

    // ------------------------------------------------------------------ 振り分け

    /** 1.21.4 のアイテム定義。`overrides` は削除されており、これが唯一の書き方である。 */
    private static String dispatch(Map<Integer, String> entries) {
        StringBuilder json = new StringBuilder();
        json.append("{\n");
        json.append("  \"__comment\": \"自動生成（core の ModelPack）。"
                + "手で書き換えず、生成し直すこと\",\n");
        json.append("  \"model\": {\n");
        json.append("    \"type\": \"minecraft:range_dispatch\",\n");
        json.append("    \"property\": \"minecraft:custom_model_data\",\n");
        json.append("    \"index\": 0,\n");
        json.append("    \"fallback\": { \"type\": \"minecraft:model\","
                + " \"model\": \"minecraft:item/paper\" },\n");
        json.append("    \"entries\": [\n");
        List<String> rendered = new ArrayList<>();
        entries.forEach((threshold, model) -> rendered.add(
                "      { \"threshold\": " + threshold
                        + ", \"model\": { \"type\": \"minecraft:model\", \"model\": \""
                        + model + "\" } }"));
        json.append(String.join(",\n", rendered)).append("\n");
        json.append("    ]\n");
        json.append("  }\n");
        json.append("}\n");
        return json.toString();
    }

    // ------------------------------------------------------------------ 対応表

    private static Map<String, String> names() {
        Map<String, String> names = new LinkedHashMap<>();
        names.put("胴", "torso");
        names.put("人胴", "torso");
        names.put("頭", "head");
        names.put("右角", "horn_right");
        names.put("左角", "horn_left");
        names.put("頭飾り", "crest");
        names.put("右肩", "shoulder_right");
        names.put("左肩", "shoulder_left");
        names.put("右腕", "arm_right");
        names.put("左腕", "arm_left");
        names.put("右足", "leg_right");
        names.put("左足", "leg_left");
        names.put("槍", "spear");
        names.put("穂先", "spearhead");
        names.put("馬胴", "horse_body");
        names.put("右前足", "foreleg_right");
        names.put("左前足", "foreleg_left");
        names.put("右後足", "hindleg_right");
        names.put("左後足", "hindleg_left");
        return names;
    }

    private static Map<String, String> textures() {
        Map<String, String> textures = new LinkedHashMap<>();
        textures.put("WHITE_CONCRETE", "minecraft:block/white_concrete");
        textures.put("QUARTZ_BLOCK", "minecraft:block/quartz_block_side");
        textures.put("BONE_BLOCK", "minecraft:block/bone_block_side");
        textures.put("POLISHED_ANDESITE", "minecraft:block/polished_andesite");
        textures.put("SMOOTH_QUARTZ", "minecraft:block/quartz_block_bottom");
        textures.put("NETHERITE_SWORD", "minecraft:item/netherite_sword");
        textures.put("NETHERITE_AXE", "minecraft:item/netherite_axe");
        textures.put("PAPER", "minecraft:item/paper");
        return textures;
    }
}
