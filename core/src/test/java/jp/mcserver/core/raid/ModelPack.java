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
 * <p><b>見た目づくりを「PNG を塗るだけ」に寄せる。</b>立体を扱う道具を使わずに済むよう、
 * 箱の6面を1枚の絵へ展開した割り付け（{@link SkinNet}）を作り、その枠を UV として
 * モデルへ書き込む。あわせて面ごとに塗り分けた下地（{@link SkinTemplate}）を置くので、
 * Windows のペイントで枠の中を塗り替えれば見た目が変わる。
 *
 * <p>生成するもの
 * <ul>
 *   <li>部位ごとの箱モデル（{@code assets/minecraft/models/knight/p1|p2/*.json}）</li>
 *   <li>{@code custom_model_data} からの振り分け（{@code assets/minecraft/items/paper.json}）</li>
 *   <li>塗り絵（{@code assets/minecraft/textures/knight/*.png}）。
 *       <b>すでにある PNG は上書きしない</b></li>
 *   <li>塗り絵の原本（{@code templates/*.png}）。こちらは毎回書き直す</li>
 * </ul>
 *
 * <p>実行: {@code java -cp out jp.mcserver.core.raid.ModelPack [出力先]}
 */
public final class ModelPack {

    private ModelPack() {
    }

    /** 部位名 → モデルのファイル名。日本語をパスに出さない。 */
    private static final Map<String, String> NAMES = names();

    /**
     * 部位名 → 塗り絵の名前。
     *
     * <p><b>左右と形態で1枚を共有する。</b>左右の腕・角・肩は同じ絵で足り、
     * 形態のあいだの寸法差（腕の 0.40 と 0.42 など）は UV の伸びに吸収させる。
     * 塗る枚数を 29 から 11 まで落とすためで、描き分けたくなった部位だけ
     * ここを分ければよい。
     */
    private static final Map<String, String> SKINS = skins();

    /** 槍の絞りを何段の箱で近似するか。1つのモデルの中で完結するので表示実体は増えない。 */
    private static final int SPEAR_STEPS = 8;

    /** 面の並び。モデルにこの順で書く。 */
    private static final String[] FACES = {
            SkinNet.NORTH, SkinNet.EAST, SkinNet.SOUTH,
            SkinNet.WEST, SkinNet.UP, SkinNet.DOWN};

    public static void main(String[] args) throws IOException {
        Path root = Path.of(args.length > 0 ? args[0] : "../resourcepack");
        Path models = root.resolve("assets/minecraft/models/knight");
        Path textures = root.resolve("assets/minecraft/textures/knight");
        Path templates = root.resolve("templates");
        Path items = root.resolve("assets/minecraft/items");
        Files.createDirectories(items);

        RaidSpecies boss = KnightDefinition.boss();
        Map<String, Skin> skins = collectSkins(boss);

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
                String skin = skinName(partName, file);
                Files.writeString(directory.resolve(file + ".json"),
                        model(partName, part, skins.get(skin)), StandardCharsets.UTF_8);
                dispatch.put(part.modelId(), "knight/" + phase + "/" + file);
                written++;
            }
        }

        Files.writeString(items.resolve("paper.json"), dispatch(dispatch),
                StandardCharsets.UTF_8);
        int drawn = writeSkins(skins, textures, templates);

        System.out.println("モデル " + written + " 件と振り分け " + dispatch.size()
                + " 件を書き出した: " + root.toAbsolutePath().normalize());
        System.out.println("塗り絵 " + skins.size() + " 枚（うち新しく置いたのは "
                + drawn + " 枚。すでにある PNG は触っていない）");
        report(skins);
    }

    // ------------------------------------------------------------------ 塗り絵

    /**
     * 塗り絵1枚ぶん。
     *
     * @param name     塗り絵の名前（PNG のファイル名）
     * @param net      面の割り付け
     * @param material 下地色を引く素材名
     * @param size     割り付けの基にした寸法
     * @param parts    この1枚を使う部位（案内に出す）
     */
    private record Skin(String name, SkinNet net, String material, Vec3 size,
                        List<String> parts) {
    }

    /**
     * 塗り絵の名前 → 中身を集める。
     *
     * <p>割り付けは<b>その名前を使う部位のうち最も大きいもの</b>から作る。
     * 大きい部位ほど画面で目につくため、そちらの縦横比に合わせるのが得である。
     */
    private static Map<String, Skin> collectSkins(RaidSpecies boss) {
        Map<String, Skin> skins = new LinkedHashMap<>();
        for (int index = 0; index < boss.phases().size(); index++) {
            String phase = "p" + (index + 1);
            Rig rig = boss.phases().get(index).rig().orElseThrow();
            for (String partName : rig.partNames()) {
                Rig.Part part = rig.part(partName);
                Appearance look = part.appearance();
                if (look == null) {
                    continue;
                }
                String file = NAMES.getOrDefault(partName, "part_" + part.modelId());
                String name = skinName(partName, file);
                Skin existing = skins.get(name);
                if (existing == null) {
                    List<String> parts = new ArrayList<>();
                    parts.add(phase + " " + partName);
                    skins.put(name, new Skin(name, SkinNet.of(look.scale()),
                            look.material(), look.scale(), parts));
                    continue;
                }
                existing.parts().add(phase + " " + partName);
                if (volume(look.scale()) > volume(existing.size())) {
                    skins.put(name, new Skin(name, SkinNet.of(look.scale()),
                            look.material(), look.scale(), existing.parts()));
                }
            }
        }
        return skins;
    }

    private static double volume(Vec3 size) {
        return size.x() * size.y() * size.z();
    }

    private static String skinName(String partName, String fallback) {
        return SKINS.getOrDefault(partName, fallback);
    }

    /**
     * 下地を置く。
     *
     * <p>{@code templates/} は毎回書き直す原本で、{@code textures/} は描く場所である。
     * <b>描いた PNG を消さない</b>ため、textures 側は無いときだけ置く。
     *
     * @return 新しく置いた枚数
     */
    private static int writeSkins(Map<String, Skin> skins, Path textures, Path templates)
            throws IOException {
        int drawn = 0;
        for (Map.Entry<String, Skin> entry : skins.entrySet()) {
            Skin skin = entry.getValue();
            SkinTemplate.write(templates.resolve(entry.getKey() + ".png"),
                    skin.net(), skin.material());
            Path texture = textures.resolve(entry.getKey() + ".png");
            if (!Files.exists(texture)) {
                SkinTemplate.write(texture, skin.net(), skin.material());
                drawn++;
            }
        }
        return drawn;
    }

    /** 塗る前に読む表。どの絵がどの部位に貼られ、どの枠がどれだけの大きさかを出す。 */
    private static void report(Map<String, Skin> skins) {
        System.out.println();
        System.out.printf("%-14s %-6s %-8s %s%n", "塗り絵", "並び", "前面の枠", "貼られる部位");
        skins.forEach((name, skin) -> {
            SkinNet.Rect front = skin.net().region(SkinNet.SOUTH);
            System.out.printf("%-14s %-6s %3d×%-4d %s%n", name + ".png",
                    skin.net().arrangement(), front.width(), front.height(),
                    String.join("・", skin.parts()));
        });
    }

    // ------------------------------------------------------------------ モデル

    /**
     * 部位1つぶんのモデル。
     *
     * <p>原点はモデル座標 <b>(8, 8, 8)</b>（実機で較正済み。§7）。部位の宣言が持つ
     * ずらしをそのまま単位へ写すので、中心合わせの部位は中心に、
     * 付け根合わせの部位は原点から下へ伸びる形になる。
     */
    private static String model(String partName, Rig.Part part, Skin skin) {
        Appearance look = part.appearance();
        double unit = 16 / modelDivisor(look);
        StringBuilder json = new StringBuilder();
        json.append("{\n");
        json.append("  \"__comment\": \"").append(partName).append(" / ID ")
                .append(part.modelId()).append(" / 自動生成（core の ModelPack）\",\n");
        json.append("  \"textures\": {\n");
        json.append("    \"skin\": \"").append(texture(skin)).append("\",\n");
        json.append("    \"particle\": \"#skin\"\n");
        json.append("  },\n");
        json.append("  \"elements\": [\n");

        List<String> elements = new ArrayList<>();
        if (look.tapered()) {
            elements.addAll(taperElements(look, unit, skin));
        } else {
            elements.add(box(partName, look.drawOffset(), look.scale(), unit,
                    faces(skin, 0, 1)));
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
     *
     * <p>側面の絵も段の数だけ横に切り分けて貼るため、<b>柄に沿って描いた絵がつながる</b>。
     */
    private static List<String> taperElements(Appearance look, double unit, Skin skin) {
        List<String> elements = new ArrayList<>();
        double length = look.scale().y() / SPEAR_STEPS;
        for (int step = 0; step < SPEAR_STEPS; step++) {
            // 手元（原点側）が太い。step=0 が手元
            double ratio = 1 + (look.taper() - 1) * ((double) step / (SPEAR_STEPS - 1));
            Vec3 size = new Vec3(look.scale().x() * ratio, length, look.scale().z() * ratio);
            // 付け根合わせなので、原点から下へ段を積む
            double top = look.offset().y() + look.scale().y() - length * step;
            Vec3 offset = new Vec3(-size.x() / 2, top - length, -size.z() / 2);
            elements.add(box("絞り " + (step + 1) + "/" + SPEAR_STEPS, offset, size, unit,
                    faces(skin, step, SPEAR_STEPS)));
        }
        return elements;
    }

    /**
     * 面ごとの UV。展開図の枠をそのまま写す。
     *
     * @param step  絞りの段。側面はこの段ぶんの帯を使う
     * @param steps 段の数。1 なら枠まるごと
     */
    private static Map<String, double[]> faces(Skin skin, int step, int steps) {
        Map<String, double[]> uvs = new LinkedHashMap<>();
        for (String face : FACES) {
            SkinNet.Rect rect = skin.net().region(face);
            boolean side = !face.equals(SkinNet.UP) && !face.equals(SkinNet.DOWN);
            uvs.put(face, side && steps > 1 ? rect.band(step, steps) : rect.uv());
        }
        return uvs;
    }

    private static String box(String name, Vec3 offset, Vec3 size, double unit,
            Map<String, double[]> uvs) {
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
        List<String> rendered = new ArrayList<>();
        for (String face : FACES) {
            double[] uv = uvs.get(face);
            rendered.add("        \"" + face + "\": { \"uv\": [" + trim(uv[0]) + ", "
                    + trim(uv[1]) + ", " + trim(uv[2]) + ", " + trim(uv[3])
                    + "], \"texture\": \"#skin\" }");
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

    private static String texture(Skin skin) {
        return "knight/" + skin.name();
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

    private static Map<String, String> skins() {
        Map<String, String> skins = new LinkedHashMap<>();
        skins.put("胴", "torso");
        skins.put("人胴", "torso");
        skins.put("頭", "head");
        skins.put("右角", "horn");
        skins.put("左角", "horn");
        skins.put("頭飾り", "crest");
        skins.put("右肩", "shoulder");
        skins.put("左肩", "shoulder");
        skins.put("右腕", "arm");
        skins.put("左腕", "arm");
        skins.put("右足", "leg");
        skins.put("左足", "leg");
        skins.put("槍", "spear");
        skins.put("穂先", "spearhead");
        skins.put("馬胴", "horse_body");
        // 馬の脚は騎士の脚と別に描けるようにしておく。前後は1枚で足りる
        skins.put("右前足", "horse_leg");
        skins.put("左前足", "horse_leg");
        skins.put("右後足", "horse_leg");
        skins.put("左後足", "horse_leg");
        return skins;
    }
}
