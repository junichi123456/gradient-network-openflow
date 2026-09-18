package jp.mcserver.core.raid;

import java.util.List;

/**
 * 表示へ送るべき配置を書き出す（§12.6）。
 *
 * <p><b>実機の {@code /raid dump} と1行ずつ突き合わせるための出力である。</b>
 * 画面写真から角度を読み取るのは当てにならないため、数値で比べる。
 *
 * <p>実行: {@code java -cp out jp.mcserver.core.raid.RigDump [形態 1|2] [tick] [モーション名]}
 * モーション名を省くと待機モーションを見る。
 */
public final class RigDump {

    private RigDump() {
    }

    public static void main(String[] args) {
        int phaseNumber = args.length > 0 ? Integer.parseInt(args[0]) : 1;
        int tick = args.length > 1 ? Integer.parseInt(args[1]) : 0;
        String motionName = args.length > 2 ? args[2] : null;

        var phase = KnightDefinition.boss().phases().get(phaseNumber - 1);
        Rig rig = phase.rig().orElseThrow();
        Animation animation = motionName == null
                ? phase.behavior().idleAnimation().orElseThrow()
                : phase.motions().stream()
                        .filter(m -> m.name().equals(motionName)).findFirst().orElseThrow()
                        .animation();

        System.out.println("第" + phaseNumber + "形態 / " + animation.name() + " / tick " + tick);
        System.out.println("位置は個体の原点からの相対。回転は XYZ順のオイラー角（度）");
        System.out.println();
        var hits = Skeleton.hitPoints(rig, animation, tick);
        for (Skeleton.Placement placement : Skeleton.placements(rig, animation, tick)) {
            StringBuilder line = new StringBuilder(String.format(
                    "%-5s 位置(%6.2f,%6.2f,%6.2f) 回転(%7.1f,%7.1f,%7.1f) 寸法(%.2f,%.2f,%.2f)",
                    placement.part(),
                    placement.translation().x(), placement.translation().y(),
                    placement.translation().z(),
                    placement.rotationDeg().x(), placement.rotationDeg().y(),
                    placement.rotationDeg().z(),
                    placement.scale().x(), placement.scale().y(), placement.scale().z()));
            List<Vec3> points = hits.get(placement.part());
            if (points != null && !points.isEmpty()) {
                Vec3 first = points.get(0);
                Vec3 last = points.get(points.size() - 1);
                line.append(String.format(" 判定 先端(%6.2f,%6.2f,%6.2f) 付根(%6.2f,%6.2f,%6.2f)",
                        first.x(), first.y(), first.z(), last.x(), last.y(), last.z()));
            }
            System.out.println(line);
        }
    }
}
