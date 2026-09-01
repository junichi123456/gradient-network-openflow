package jp.mcserver.plugin;

import java.util.ArrayList;
import java.util.List;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.entity.BlockDisplay;
import org.bukkit.entity.Display;
import org.bukkit.entity.Entity;
import org.bukkit.entity.ItemDisplay;
import org.bukkit.inventory.ItemStack;
import org.bukkit.inventory.meta.ItemMeta;
import org.bukkit.util.Transformation;
import org.joml.Quaternionf;
import org.joml.Vector3f;

/**
 * モデルの原点の較正（`raid_model_spec.md` §7）。
 *
 * <p><b>ItemDisplay がモデルのどこを原点として描くかは、実機で1度だけ確かめる必要がある。</b>
 * 本書の想定は「モデル座標 (8, 8, 8) がエンティティの位置に来る」だが、これは検証していない。
 * ここがずれていると、描いたモデルが全部位まとめてずれる。
 *
 * <p>出すもの
 * <ul>
 *   <li><b>16単位の立方体</b>（面ごとに色が違う。モデル座標 (0,0,0) の角にマゼンタの目印）</li>
 *   <li><b>エンティティの位置を示す小さな赤い立方体</b>（一辺 0.1、中心が位置そのもの）</li>
 * </ul>
 *
 * <p>赤い印が色付き立方体の<b>中心</b>にあれば想定どおりである。
 */
final class Calibration {

    private Calibration() {
    }

    /** 較正用の立方体のモデル識別子。リソースパック側の threshold と揃える。 */
    static final int MODEL_ID = 9000;

    /** 位置を示す印の一辺（ブロック）。 */
    private static final float MARKER = 0.1f;

    /**
     * 較正用の表示を出す。
     *
     * @param at 出す位置。ここが「エンティティの位置」になる
     * @return 出したエンティティ。片付けに使う
     */
    static List<Entity> spawn(Location at) {
        List<Entity> spawned = new ArrayList<>();

        ItemStack paper = new ItemStack(Material.PAPER);
        ItemMeta meta = paper.getItemMeta();
        meta.setCustomModelData(MODEL_ID);
        paper.setItemMeta(meta);

        ItemDisplay cube = at.getWorld().spawn(at, ItemDisplay.class, entity -> {
            entity.setItemStack(paper);
            // 実際の骨格と同じ扱いにする。ここが違うと較正の結果が移らない
            entity.setItemDisplayTransform(ItemDisplay.ItemDisplayTransform.NONE);
            entity.setTransformation(identity());
            entity.setBrightness(new Display.Brightness(15, 15));
            entity.setPersistent(false);
        });
        spawned.add(cube);

        BlockDisplay marker = at.getWorld().spawn(at, BlockDisplay.class, entity -> {
            entity.setBlock(Material.REDSTONE_BLOCK.createBlockData());
            // 中心を位置そのものに合わせる
            entity.setTransformation(new Transformation(
                    new Vector3f(-MARKER / 2, -MARKER / 2, -MARKER / 2), new Quaternionf(),
                    new Vector3f(MARKER, MARKER, MARKER), new Quaternionf()));
            entity.setBrightness(new Display.Brightness(15, 15));
            entity.setPersistent(false);
        });
        spawned.add(marker);

        return spawned;
    }

    private static Transformation identity() {
        return new Transformation(new Vector3f(), new Quaternionf(),
                new Vector3f(1, 1, 1), new Quaternionf());
    }
}
