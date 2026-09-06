package jp.mcserver.plugin;

import java.util.ArrayList;
import java.util.List;
import java.util.function.UnaryOperator;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.entity.BlockDisplay;
import org.bukkit.entity.Display;
import org.bukkit.entity.Entity;
import org.bukkit.entity.ItemDisplay;
import org.bukkit.inventory.ItemStack;
import org.bukkit.inventory.meta.ItemMeta;
import org.bukkit.util.Transformation;
import org.joml.Matrix4f;
import org.joml.Quaternionf;
import org.joml.Vector3f;

/**
 * モデルの原点の較正（`raid_model_spec.md` §7）。
 *
 * <p><b>ItemDisplay がモデルのどこを原点として描くかは、実機で1度だけ確かめる必要がある。</b>
 * 本書の想定は「モデル座標 (8, 8, 8) がエンティティの位置に来る」だが、これは検証していない。
 * ここがずれていると、描いたモデルが全部位まとめてずれる。
 *
 * <p><b>出すもの。</b>西から東へ2ブロックおきに4つ並べる。すべて ItemDisplay で、
 * 本番の部位とまったく同じ扱い（{@code ItemDisplayTransform.NONE}）である。
 *
 * <table>
 *   <tr><th>位置</th><th>掛けた回転</th><th>読み取れること</th></tr>
 *   <tr><td>1つめ</td><td>無し</td>
 *       <td>Minecraft が ItemDisplay のモデルを<b>もともとどう向けて描くか</b></td></tr>
 *   <tr><td>2つめ</td><td>{@link BossRig#yawOnly}（Y軸180度）</td>
 *       <td>180度の打ち消しだけで正しくなるか</td></tr>
 *   <tr><td>3つめ</td><td>{@link BossRig#tiltOnly}（Z軸の傾き）</td>
 *       <td>傾きが単独でどう効くか</td></tr>
 *   <tr><td>4つめ</td><td>{@link BossRig#compensate}（本番と同じ）</td>
 *       <td><b>描いたモデルが実際にどう向くか</b></td></tr>
 * </table>
 *
 * <p>それぞれの位置に、エンティティの位置を示す小さな赤い立方体を置く。
 *
 * <p>立方体は面ごとに色が違い、モデル座標 (0,0,0) の角にマゼンタの目印がある。
 * <b>天面の色と北面の色を読めば、掛かっている回転が一意に決まる。</b>
 *
 * <p>較正の結果（実機で確認済み）
 * <ul>
 *   <li>原点は<b>モデル座標 (8,8,8)</b>。赤い印が立方体の中心に来る</li>
 *   <li>ItemDisplay は<b>Y軸まわりに180度回して描く</b>。補正なしの立方体は
 *       南北・東西が入れ替わって見える</li>
 * </ul>
 */
final class Calibration {

    private Calibration() {
    }

    /** 較正用の立方体のモデル識別子。リソースパック側の threshold と揃える。 */
    static final int MODEL_ID = 9000;

    /** 位置を示す印の一辺（ブロック）。 */
    private static final float MARKER = 0.1f;

    /** 立方体を並べる間隔（ブロック）。 */
    private static final int SPACING = 2;

    /** 並べる順。読み取り表と揃える。 */
    static final List<String> LABELS = List.of(
            "補正なし", "180度だけ", "傾きだけ", "本番と同じ（180度+傾き）");

    /**
     * 較正用の表示を出す。
     *
     * @param at 出す位置。ここが1つめの「エンティティの位置」になる
     * @return 出したエンティティ。片付けに使う
     */
    static List<Entity> spawn(Location at) {
        List<Entity> spawned = new ArrayList<>();

        ItemStack paper = new ItemStack(Material.PAPER);
        ItemMeta meta = paper.getItemMeta();
        meta.setCustomModelData(MODEL_ID);
        paper.setItemMeta(meta);

        // 補正の式は BossRig から借りる。ここで書き直すと較正の結論が本番へ移らない
        List<UnaryOperator<Matrix4f>> rotations = List.of(
                matrix -> matrix,
                BossRig::yawOnly,
                BossRig::tiltOnly,
                BossRig::compensate);

        for (int i = 0; i < rotations.size(); i++) {
            // 向きを落とす。位置に向きが残ると表示だけが余計に回る（BossRig.upright）
            Location place = BossRig.upright(at.clone().add((double) SPACING * i, 0, 0));
            spawned.add(cube(place, paper, rotations.get(i)));
            spawned.add(marker(place));
        }

        return spawned;
    }

    /** エンティティの位置そのものを示す小さな立方体。 */
    private static BlockDisplay marker(Location at) {
        return at.getWorld().spawn(at, BlockDisplay.class, entity -> {
            entity.setBlock(Material.REDSTONE_BLOCK.createBlockData());
            // 中心を位置そのものに合わせる
            entity.setTransformation(new Transformation(
                    new Vector3f(-MARKER / 2, -MARKER / 2, -MARKER / 2), new Quaternionf(),
                    new Vector3f(MARKER, MARKER, MARKER), new Quaternionf()));
            entity.setBrightness(new Display.Brightness(15, 15));
            entity.setPersistent(false);
        });
    }

    private static ItemDisplay cube(Location at, ItemStack paper,
            UnaryOperator<Matrix4f> rotation) {
        // 本番と同じ手順で回転を取り出す。拡大率を掛けない行列から取る
        Quaternionf turned = rotation.apply(new Matrix4f())
                .getNormalizedRotation(new Quaternionf());
        return at.getWorld().spawn(at, ItemDisplay.class, entity -> {
            entity.setItemStack(paper);
            // 実際の骨格と同じ扱いにする。ここが違うと較正の結果が移らない
            entity.setItemDisplayTransform(ItemDisplay.ItemDisplayTransform.NONE);
            entity.setTransformation(new Transformation(new Vector3f(), turned,
                    new Vector3f(1, 1, 1), new Quaternionf()));
            entity.setBrightness(new Display.Brightness(15, 15));
            entity.setPersistent(false);
        });
    }
}
