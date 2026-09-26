package jp.mcserver.core.raid;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
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
 * <p><b>種目ごとに骨格・部位名・寸法が違うため、種目1つぶんの設定を {@link Species} に
 * まとめ、種目を増やすたびにこの1レコードを足すだけで済む形にしてある。</b>
 * 騎士型（{@code knight}）・虚刃の衛士（{@code hollow_guard}）の2種目を生成する。
 *
 * <p>生成するもの（種目ごと）
 * <ul>
 *   <li>部位ごとの箱モデル（{@code assets/minecraft/models/<種目>/...}）</li>
 *   <li>{@code custom_model_data} からの振り分け（{@code assets/minecraft/items/paper.json}、
 *       全種目まとめて1つ）</li>
 *   <li>塗り絵（{@code assets/minecraft/textures/item/<種目>/*.png}）。
 *       <b>すでにある PNG は上書きしない</b></li>
 *   <li>塗り絵の原本（{@code templates/*.png}）。こちらは毎回書き直す</li>
 * </ul>
 *
 * <p>実行: {@code java -cp out jp.mcserver.core.raid.ModelPack [出力先]}
 */
public final class ModelPack {

    private ModelPack() {
    }

    /** 較正用の立方体（`raid_model_spec.md` §7）。種目を問わず共通の1個で足りる。 */
    private static final int CALIBRATION_MODEL_ID = 9000;

    /** 槍の絞りを何段の箱で近似するか。1つのモデルの中で完結するので表示実体は増えない。 */
    private static final int SPEAR_STEPS = 8;

    /** 面の並び。モデルにこの順で書く。 */
    private static final String[] FACES = {
            SkinNet.NORTH, SkinNet.EAST, SkinNet.SOUTH,
            SkinNet.WEST, SkinNet.UP, SkinNet.DOWN};

    /**
     * 種目1つぶんの設定。
     *
     * @param id                モデル・テクスチャの置き場所に使う名前（英数字、日本語をパスに出さない）
     * @param boss              骨格・段階の定義
     * @param names             部位名（日本語） → モデルのファイル名
     * @param skins             部位名（日本語） → 塗り絵の名前（左右・段階で1枚を共有する部位はここで束ねる）
     * @param longPartModelScale モデル座標に収まらない部位を縮める倍率（{@code raid_model_spec.md} §3）
     */
    private record Species(String id, RaidSpecies boss, Map<String, String> names,
                           Map<String, String> skins, double longPartModelScale) {
    }

    public static void main(String[] args) throws IOException {
        Path root = Path.of(args.length > 0 ? args[0] : "../resourcepack");
        Path items = root.resolve("assets/minecraft/items");
        Files.createDirectories(items);

        List<Species> roster = List.of(
                new Species("knight", KnightDefinition.boss(), knightNames(), knightSkins(),
                        KnightDefinition.LONG_PART_MODEL_SCALE),
                new Species("hollow_guard", HollowGuardDefinition.boss(), hollowGuardNames(),
                        hollowGuardSkins(), HollowGuardDefinition.LONG_PART_MODEL_SCALE));

        // threshold の昇順に並べる必要があるため、ID を鍵にした木で全種目ぶん集める
        Map<Integer, String> dispatch = new TreeMap<>();
        dispatch.put(CALIBRATION_MODEL_ID, "knight/calibration");

        for (Species species : roster) {
            generate(root, species, dispatch);
        }

        Files.writeString(items.resolve("paper.json"), dispatch(dispatch),
                StandardCharsets.UTF_8);
        System.out.println();
        System.out.println("振り分け合計 " + dispatch.size() + " 件（"
                + roster.size() + " 種目 + 較正用1件）を書き出した: "
                + items.resolve("paper.json").toAbsolutePath().normalize());
    }

    /** 種目1つぶんを生成する。 */
    private static void generate(Path root, Species species, Map<Integer, String> dispatch)
            throws IOException {
        Path models = root.resolve("assets/minecraft/models/" + species.id());
        Path textures = root.resolve("assets/minecraft/textures/item/" + species.id());
        Path templates = root.resolve("templates");
        String texturePrefix = "item/" + species.id() + "/";

        Map<String, Skin> skins = collectSkins(species);

        // 段階をまたいで骨格が変わらない種目（虚刃の衛士）は、同じ部位IDが全段階に出てくる。
        // そのときは段階ごとのフォルダに分けず、種目直下へまとめて書く（見た目が1つしか無いため）
        boolean multiForm = hasFormChange(species.boss());

        Set<Integer> written = new HashSet<>();
        int writtenCount = 0;
        for (int index = 0; index < species.boss().phases().size(); index++) {
            Rig rig = species.boss().phases().get(index).rig().orElseThrow();
            for (String partName : rig.partNames()) {
                Rig.Part part = rig.part(partName);
                if (part.appearance() == null || !written.add(part.modelId())) {
                    continue;
                }
                Path directory = multiForm ? models.resolve("p" + (index + 1)) : models;
                Files.createDirectories(directory);
                String file = species.names().getOrDefault(partName, "part_" + part.modelId());
                String skinName = skinName(partName, file, species.skins());
                Files.writeString(directory.resolve(file + ".json"),
                        model(partName, part, skins.get(skinName), texturePrefix,
                                species.longPartModelScale()),
                        StandardCharsets.UTF_8);
                String dispatchPath = multiForm
                        ? species.id() + "/p" + (index + 1) + "/" + file
                        : species.id() + "/" + file;
                dispatch.put(part.modelId(), dispatchPath);
                writtenCount++;
            }
        }

        int drawn = writeSkins(skins, textures, templates);

        System.out.println();
        System.out.println("[" + species.id() + "] モデル " + writtenCount + " 件を書き出した: "
                + models.toAbsolutePath().normalize());
        System.out.println("[" + species.id() + "] 塗り絵 " + skins.size() + " 枚 "
                + SkinNet.CANVAS + "×" + SkinNet.CANVAS + "（うち新しく置いたのは "
                + drawn + " 枚。すでにある PNG は触っていない）");
        report(skins);
    }

    /**
     * 段階をまたいで骨格（部位IDの集合）が変わるか。
     *
     * <p>変わらない種目（第一形態の部位IDが後の段階にもそのまま出てくる）は、見た目が
     * 1つしか無いということなので、段階ごとのフォルダ分けをしない。
     */
    private static boolean hasFormChange(RaidSpecies boss) {
        Set<Integer> firstForm = idsOf(boss.phases().get(0).rig().orElseThrow());
        for (int i = 1; i < boss.phases().size(); i++) {
            if (!firstForm.containsAll(idsOf(boss.phases().get(i).rig().orElseThrow()))) {
                return true;
            }
        }
        return false;
    }

    private static Set<Integer> idsOf(Rig rig) {
        Set<Integer> ids = new HashSet<>();
        for (String partName : rig.partNames()) {
            Rig.Part part = rig.part(partName);
            if (part.appearance() != null) {
                ids.add(part.modelId());
            }
        }
        return ids;
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
    private static Map<String, Skin> collectSkins(Species species) {
        Map<String, Skin> skins = new LinkedHashMap<>();
        RaidSpecies boss = species.boss();
        for (int index = 0; index < boss.phases().size(); index++) {
            String phase = "p" + (index + 1);
            Rig rig = boss.phases().get(index).rig().orElseThrow();
            for (String partName : rig.partNames()) {
                Rig.Part part = rig.part(partName);
                Appearance look = part.appearance();
                if (look == null) {
                    continue;
                }
                String file = species.names().getOrDefault(partName, "part_" + part.modelId());
                String name = skinName(partName, file, species.skins());
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

    private static String skinName(String partName, String fallback, Map<String, String> skins) {
        return skins.getOrDefault(partName, fallback);
    }

    /**
     * 下地を置く。
     *
     * <p>{@code templates/} は毎回書き直す原本で、{@code textures/} は描く場所である。
     * <b>描いた PNG を消さない</b>ため、textures 側は無いときだけ置く。
     *
     * <p>枠が小さいと頭文字が入らないため、{@code templates/guide/} に拡大した案内図も置く。
     *
     * <p><b>{@code templates/} は種目をまたいで1つのフォルダを共有する（flat）。</b>
     * 塗り絵の名前が種目間で衝突しないよう、種目ごとの {@code SKINS} で名前を分けてある
     * （例: 虚刃の衛士は {@code guard_} を前置き）。
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
            SkinTemplate.writeGuide(templates.resolve("guide").resolve(entry.getKey() + ".png"),
                    skin.net(), skin.material());
            Path texture = textures.resolve(entry.getKey() + ".png");
            if (!Files.exists(texture)) {
                SkinTemplate.write(texture, skin.net(), skin.material());
                drawn++;
            } else {
                warnIfResized(texture);
            }
        }
        return drawn;
    }

    /**
     * すでにある PNG の大きさが画布と違うときに知らせる。
     *
     * <p>UV は割合で書くため、大きさが違う絵を貼ると<b>黙って位置がずれる</b>。
     * 塗った絵を勝手に捨てないので、消すかどうかは人が決める。
     */
    private static void warnIfResized(Path texture) throws IOException {
        var image = javax.imageio.ImageIO.read(texture.toFile());
        if (image == null) {
            System.out.println("!! 読めない PNG がある: " + texture);
            return;
        }
        if (image.getWidth() != SkinNet.CANVAS || image.getHeight() != SkinNet.CANVAS) {
            System.out.println("!! 大きさが画布（" + SkinNet.CANVAS + "×" + SkinNet.CANVAS
                    + "）と違う: " + texture.getFileName() + " は "
                    + image.getWidth() + "×" + image.getHeight()
                    + "。貼る位置がずれるので、消して置き直すこと");
        }
    }

    /** 塗る前に読む表。どの絵がどの部位に貼られ、どの枠がどれだけの大きさかを出す。 */
    private static void report(Map<String, Skin> skins) {
        System.out.println();
        System.out.printf("%-14s %-6s %-8s %-6s %s%n",
                "塗り絵", "並び", "前面の枠", "頭文字", "貼られる部位");
        skins.forEach((name, skin) -> {
            SkinNet.Rect front = skin.net().region(SkinNet.SOUTH);
            System.out.printf("%-14s %-6s %3d×%-4d %d/6    %s%n", name + ".png",
                    skin.net().arrangement(), front.width(), front.height(),
                    SkinTemplate.markedFaces(skin.net()),
                    String.join("・", skin.parts()));
        });
        System.out.println();
        System.out.println("頭文字が入らない枠は templates/guide/ の拡大図で読むこと"
                + "（実寸の " + SkinTemplate.GUIDE_SCALE + " 倍）");
    }

    // ------------------------------------------------------------------ モデル

    /**
     * 部位1つぶんのモデル。
     *
     * <p>原点はモデル座標 <b>(8, 8, 8)</b>（実機で較正済み。§7）。部位の宣言が持つ
     * ずらしをそのまま単位へ写すので、中心合わせの部位は中心に、
     * 付け根合わせの部位は原点から下へ伸びる形になる。
     */
    private static String model(String partName, Rig.Part part, Skin skin, String texturePrefix,
            double longPartModelScale) {
        Appearance look = part.appearance();
        double unit = 16 / modelDivisor(look, longPartModelScale);
        StringBuilder json = new StringBuilder();
        json.append("{\n");
        json.append("  \"__comment\": \"").append(partName).append(" / ID ")
                .append(part.modelId()).append(" / 自動生成（core の ModelPack）\",\n");
        json.append("  \"textures\": {\n");
        json.append("    \"skin\": \"").append(texture(skin, texturePrefix)).append("\",\n");
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
     * 描画側が {@code longPartModelScale} 倍に戻す。
     */
    private static double modelDivisor(Appearance look, double longPartModelScale) {
        return look.fitsModelSpace() ? 1.0 : longPartModelScale;
    }

    private static String texture(Skin skin, String texturePrefix) {
        return texturePrefix + skin.name();
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

    // ------------------------------------------------------------------ 対応表（騎士型）

    private static Map<String, String> knightNames() {
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

    private static Map<String, String> knightSkins() {
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

    // ------------------------------------------------------------------ 対応表（虚刃の衛士）

    private static Map<String, String> hollowGuardNames() {
        Map<String, String> names = new LinkedHashMap<>();
        names.put("胴", "torso");
        names.put("頭", "head");
        names.put("右腕", "arm_right");
        names.put("左腕", "arm_left");
        names.put("右足", "leg_right");
        names.put("左足", "leg_left");
        names.put("剣", "sword");
        return names;
    }

    /**
     * 塗り絵の名前。<b>{@code guard_} を前置きする</b>——{@code templates/} は種目をまたいで
     * 1つのフォルダを共有する flat 構造であり、騎士型がすでに {@code torso}・{@code head}・
     * {@code arm}・{@code leg} を使っているため、そのままでは衝突する。
     */
    private static Map<String, String> hollowGuardSkins() {
        Map<String, String> skins = new LinkedHashMap<>();
        skins.put("胴", "guard_torso");
        skins.put("頭", "guard_head");
        // 左右の腕・足は1枚を共有する（騎士型と同じ考え方）
        skins.put("右腕", "guard_arm");
        skins.put("左腕", "guard_arm");
        skins.put("右足", "guard_leg");
        skins.put("左足", "guard_leg");
        skins.put("剣", "guard_sword");
        return skins;
    }
}
