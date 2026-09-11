package jp.mcserver.plugin;

import java.util.ArrayList;
import java.util.List;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.entity.BlockDisplay;
import org.bukkit.entity.Display;
import org.bukkit.entity.Entity;
import org.bukkit.util.Transformation;
import org.joml.Matrix4f;
import org.joml.Quaternionf;
import org.joml.Vector3f;

/**
 * 送った行列がどう描かれるかを示す基準（§12.6 の切り分け）。
 *
 * <p><b>見え方を言葉で言い当てるのをやめるための道具である。</b>「西を向いている」
 * 「15度傾いている」といった報告からは、どの軸がどれだけ回っているか決まらない。
 * ここでは向きの分かっている3本の棒を、<b>部位とまったく同じ手順</b>で出す。
 * 棒がどこを指すかを見れば、描画の側がずれているのかどうかが1目で決まる。
 *
 * <p>出すもの
 * <ul>
 *   <li><b>回転なしの十字</b>— 赤が東（+X）、緑が上（+Y）、青が南（+Z）を指すべきである</li>
 *   <li><b>槍と同じ回転（X軸まわり −90度）を掛けた十字</b>— 3ブロック東へ並べる。
 *       緑が<b>北</b>、青が<b>上</b>、赤が<b>東</b>のままを指すべきである</li>
 * </ul>
 *
 * <p>1つめがずれていれば、描画へ渡す行列の組み立てが間違っている。
 * 1つめが合っていて2つめがずれていれば、回転の掛け方（左回転と右回転の取り違えなど）が
 * 間違っている。どちらも合っていれば、描画の側は正しく、骨格の宣言か姿勢の側を見ることになる。
 */
final class Axes {

    private Axes() {
    }

    /** 棒の長さ（ブロック）。 */
    private static final float LENGTH = 1.5f;

    /** 棒の太さ（ブロック）。 */
    private static final float THICK = 0.08f;

    /** 2つめの十字を並べる距離（ブロック）。 */
    private static final int SPACING = 3;

    /** 槍と同じ傾き。骨格の宣言（`posRot(0, -1.00, 0, -90, 0, 0)`）と揃える。 */
    private static final float SPEAR_PITCH_DEGREES = -90;

    /** 並べる順。案内と揃える。 */
    static final List<String> LABELS = List.of(
            "回転なし（赤=東 緑=上 青=南）",
            "槍と同じ X軸 -90度（赤=東 緑=北 青=上）");

    /**
     * 基準の十字を出す。
     *
     * @param at 出す位置
     * @return 出したエンティティ。片付けに使う
     */
    static List<Entity> spawn(Location at) {
        List<Entity> spawned = new ArrayList<>();
        List<Quaternionf> rotations = List.of(
                new Quaternionf(),
                new Quaternionf().rotateX((float) Math.toRadians(SPEAR_PITCH_DEGREES)));

        for (int i = 0; i < rotations.size(); i++) {
            // 向きを落とす。位置に向きが残ると表示だけが余計に回る（BossRig.upright）
            Location place = BossRig.upright(at.clone().add((double) SPACING * i, 0, 0));
            Quaternionf turn = rotations.get(i);
            spawned.add(bar(place, turn, Material.RED_CONCRETE,
                    new Vector3f(LENGTH, THICK, THICK),
                    new Vector3f(0, -THICK / 2, -THICK / 2)));
            spawned.add(bar(place, turn, Material.LIME_CONCRETE,
                    new Vector3f(THICK, LENGTH, THICK),
                    new Vector3f(-THICK / 2, 0, -THICK / 2)));
            spawned.add(bar(place, turn, Material.BLUE_CONCRETE,
                    new Vector3f(THICK, THICK, LENGTH),
                    new Vector3f(-THICK / 2, -THICK / 2, 0)));
        }
        return spawned;
    }

    /**
     * 棒1本。
     *
     * <p><b>組み立ては {@link BossRig} の部位と同じ手順にする。</b>拡大率を含まない行列から
     * 位置と回転を取り出し、拡大率は {@link Transformation} の scale として別に渡す。
     * ここを簡単に書いてしまうと、本番と違う経路を測ることになる。
     */
    private static BlockDisplay bar(Location at, Quaternionf world, Material material,
            Vector3f size, Vector3f offset) {
        Matrix4f placed = new Matrix4f().rotate(world).translate(offset);
        Vector3f translation = placed.getTranslation(new Vector3f());
        Quaternionf rotation = placed.getNormalizedRotation(new Quaternionf());
        return at.getWorld().spawn(at, BlockDisplay.class, entity -> {
            entity.setBlock(material.createBlockData());
            entity.setTransformation(
                    new Transformation(translation, rotation, size, new Quaternionf()));
            entity.setBrightness(new Display.Brightness(15, 15));
            entity.setPersistent(false);
        });
    }
}
