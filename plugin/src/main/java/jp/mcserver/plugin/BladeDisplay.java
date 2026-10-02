package jp.mcserver.plugin;

import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.World;
import org.bukkit.entity.Display;
import org.bukkit.entity.ItemDisplay;
import org.bukkit.inventory.ItemStack;
import org.bukkit.util.Transformation;
import org.joml.Quaternionf;
import org.joml.Vector3f;

/**
 * 浮遊剣1本ぶんの表示（{@link ItemDisplay}）。
 *
 * <p>{@link BossRig} の部位ツリーには属さない、独立した実体である
 * （`raid_species.md` §2「実装上の注意」——追従元の部位が本体ではなく空中の一点になるため）。
 * したがって親子の変換合成は要らず、この実体1つが自分の位置と向きだけを持てばよい。
 *
 * <p><b>実機で確認した結果、色付きブロックでは「剣」と分からなかったため、実際の剣アイテム
 * （鉄・金・ダイヤモンド・ネザライトの剣）を表示するよう直した。</b>銅の剣はバニラに無いため
 * 木の剣で代用する（他の4種と見分けが付くようにするため）。
 *
 * <p><b>向きは「実機で較正していない」。</b>アイテム表示の姿勢（{@code ItemDisplayTransform})
 * を経ない生の姿勢は、モデルの向きがバニラのアイテムごとに決まっていて事前に分からない
 * （`BossRig` が実機較正で確かめた180度の補正は、リソースパックで描いた独自モデルにだけ
 * 効くもので、バニラの剣アイテムには適用できない）。飛んでいる剣は自転させて誤魔化し、
 * どの角度から見ても「回っている剣」に見えるようにしてある。串刺しは自転させず直立させて
 * いるが、傾きが正しいかは未確認——ずれていれば {@link #UPRIGHT_PITCH_DEGREES} を調整すること。
 */
final class BladeDisplay {

    /** 自転の1tickあたりの角度（度）。 */
    private static final float SPIN_STEP_DEGREES = 24f;

    /** 飛んでいる剣を、進行方向へ軽く傾けて構える角度（度、仮）。 */
    private static final float FLYING_PITCH_DEGREES = 60f;

    /** 串刺しの剣を直立させる傾き（度、仮・実機未確認）。 */
    private static final float UPRIGHT_PITCH_DEGREES = 0f;

    private final ItemDisplay entity;
    private final float scale;
    private float spinDegrees;

    /**
     * @param at             出現位置
     * @param material       剣のアイテム素材
     * @param visualScale    表示の拡大率。素手の剣より明らかに大きく見えるよう、長さに応じて渡す
     */
    BladeDisplay(Location at, Material material, double visualScale) {
        this.scale = (float) visualScale;
        World world = at.getWorld();
        this.entity = world.spawn(BossRig.upright(at), ItemDisplay.class, e -> {
            e.setItemStack(new ItemStack(material));
            // バニラの素材はここを NONE にする（描いたモデルだけが180度の補正を要る。BossRig参照）
            e.setItemDisplayTransform(ItemDisplay.ItemDisplayTransform.NONE);
            e.setInterpolationDuration(1);
            e.setTeleportDuration(1);
            e.setPersistent(false);
            e.setBrightness(new Display.Brightness(15, 15));
            e.setViewRange(2.0f);
        });
    }

    /** 飛んでいるあいだの姿勢。位置を更新し、その場で自転させる。 */
    void placeFlying(Location center) {
        entity.teleport(BossRig.upright(center));
        spinDegrees = (spinDegrees + SPIN_STEP_DEGREES) % 360f;
        Quaternionf rotation = new Quaternionf()
                .rotateY((float) Math.toRadians(spinDegrees))
                .rotateX((float) Math.toRadians(FLYING_PITCH_DEGREES));
        apply(rotation);
    }

    /**
     * 「串刺し」のように、地面から生え上がる姿勢。根本（{@code base}）を基準に、
     * 生えた高さぶんだけ持ち上げる。柄の分だけ低めに置き、地面より下は地形に隠れて見えない。
     *
     * @param base       根本（地表面）の位置
     * @param height     生えた高さ（0〜全長）
     * @param fullLength 全長（生え切ったときの高さ）
     */
    void placeRising(Location base, double height, double fullLength) {
        Location center = base.clone().add(0, height - fullLength / 2.0, 0);
        entity.teleport(BossRig.upright(center));
        Quaternionf rotation = new Quaternionf().rotateX((float) Math.toRadians(UPRIGHT_PITCH_DEGREES));
        apply(rotation);
    }

    private void apply(Quaternionf rotation) {
        entity.setTransformation(new Transformation(new Vector3f(0, 0, 0), rotation,
                new Vector3f(scale, scale, scale), new Quaternionf()));
    }

    Location location() {
        return entity.getLocation();
    }

    void despawn() {
        entity.remove();
    }
}
