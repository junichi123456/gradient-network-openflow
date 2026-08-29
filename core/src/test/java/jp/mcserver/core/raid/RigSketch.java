package jp.mcserver.core.raid;

import java.util.ArrayList;
import java.util.List;

/**
 * 骨格を正射投影した略図を SVG で出力する（§12.6）。
 *
 * <p>寸法を目で確かめるための道具である。数値は {@link KnightDefinition} から直に読むため、
 * 定義を変えれば図も変わる。手で描いた図と違い、実装との食い違いが起きない。
 *
 * <p>実行: {@code java -cp out jp.mcserver.core.raid.RigSketch > knight.svg}
 */
public final class RigSketch {

    private RigSketch() {}

    /** 1ブロックあたりの画素数。 */
    private static final double SCALE = 46;

    private static final double PANEL_W = 430;
    private static final double PANEL_H = 330;

    /** 3次元の点。 */
    private record P(double x, double y, double z) {}

    /** 投影した2次元の点。 */
    private record Q(double x, double y) {}

    /** 描く1つの面。奥行き順に並べ替えるため代表深度を持つ。 */
    private record Shape(String part, String material, List<Q> outline, double depth,
                         boolean decoration) {}

    public static void main(String[] args) {
        RaidSpecies boss = KnightDefinition.boss();
        Rig first = boss.rigFor(boss.phases().get(0));
        Rig second = boss.rigFor(boss.phases().get(1));

        StringBuilder svg = new StringBuilder();
        double width = PANEL_W * 4;
        svg.append(String.format(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 %.0f %.0f\" "
                        + "width=\"%.0f\" height=\"%.0f\" font-family=\"sans-serif\">%n",
                width, PANEL_H, width, PANEL_H));
        svg.append("<rect width=\"100%\" height=\"100%\" fill=\"#141518\"/>\n");

        panel(svg, 0, first, false, "第一形態 — 正面", 3.5, 1.6);
        panel(svg, PANEL_W, first, true, "第一形態 — 側面", 3.5, 1.6);
        panel(svg, PANEL_W * 2, second, false, "第二形態 — 正面", 4.6, 2.0);
        panel(svg, PANEL_W * 3, second, true, "第二形態 — 側面", 4.6, 2.0);

        for (int i = 1; i < 4; i++) {
            svg.append(String.format(
                    "<line x1=\"%.1f\" y1=\"14\" x2=\"%.1f\" y2=\"%.1f\" "
                            + "stroke=\"#2c2e34\" stroke-width=\"1\"/>%n",
                    PANEL_W * i, PANEL_W * i, PANEL_H - 14));
        }
        svg.append("</svg>\n");
        System.out.print(svg);
    }

    private static void panel(StringBuilder svg, double left, Rig rig, boolean side,
                              String title, double height, double widthBlocks) {
        List<Shape> shapes = new ArrayList<>();
        for (String name : rig.partNames()) {
            Rig.Part part = rig.part(name);
            Appearance look = part.appearance();
            if (look == null) {
                continue;
            }
            double[] matrix = chainMatrix(rig, name);
            // 円錐は輪切りの積み重ねで描かれる。図でも同じように1枚ずつ描く
            for (int slice = 0; slice < look.slices(); slice++) {
                Appearance piece = look.slice(slice);
                List<P> corners = corners(piece);
                List<Q> projected = new ArrayList<>();
                double depth = 0;
                for (P corner : corners) {
                    P world = apply(matrix, corner);
                    projected.add(side ? new Q(world.z(), world.y())
                            : new Q(world.x(), world.y()));
                    depth += side ? world.x() : world.z();
                }
                shapes.add(new Shape(name, piece.material(), hull(projected),
                        depth / corners.size(), piece.decoration()));
            }
        }

        // 奥から手前へ描く
        shapes.sort((a, b) -> Double.compare(a.depth(), b.depth()));

        double minX = Double.MAX_VALUE;
        double maxX = -Double.MAX_VALUE;
        for (Shape shape : shapes) {
            for (Q point : shape.outline()) {
                minX = Math.min(minX, point.x());
                maxX = Math.max(maxX, point.x());
            }
        }
        double centerX = (minX + maxX) / 2;
        double groundY = PANEL_H - 46;
        double originX = left + PANEL_W / 2 - centerX * SCALE;

        // 地面
        svg.append(String.format(
                "<line x1=\"%.1f\" y1=\"%.1f\" x2=\"%.1f\" y2=\"%.1f\" "
                        + "stroke=\"#6b7180\" stroke-width=\"2\"/>%n",
                left + 20, groundY, left + PANEL_W - 20, groundY));
        svg.append(String.format(
                "<text x=\"%.1f\" y=\"%.1f\" fill=\"#8d919b\" font-size=\"10\">0</text>%n",
                left + 22, groundY - 4));

        // 1ブロックごとの目盛り
        for (int block = 1; block <= Math.ceil(height); block++) {
            double y = groundY - block * SCALE;
            svg.append(String.format(
                    "<line x1=\"%.1f\" y1=\"%.1f\" x2=\"%.1f\" y2=\"%.1f\" stroke=\"#23252b\" "
                            + "stroke-width=\"1\" stroke-dasharray=\"3 5\"/>%n",
                    left + 20, y, left + PANEL_W - 20, y));
            svg.append(String.format(
                    "<text x=\"%.1f\" y=\"%.1f\" fill=\"#5a5e68\" font-size=\"10\">%d</text>%n",
                    left + 22, y - 3, block));
        }

        for (Shape shape : shapes) {
            StringBuilder points = new StringBuilder();
            for (Q point : shape.outline()) {
                points.append(String.format("%.1f,%.1f ",
                        originX + point.x() * SCALE, groundY - point.y() * SCALE));
            }
            svg.append(String.format(
                    "<polygon data-part=\"%s\" points=\"%s\" fill=\"%s\" fill-opacity=\"%s\" "
                            + "stroke=\"%s\" stroke-width=\"1\" stroke-linejoin=\"round\"/>%n",
                    shape.part(), points.toString().trim(), fill(shape.material()),
                    shape.decoration() ? "0.75" : "0.95", stroke(shape.material())));
        }

        svg.append(String.format(
                "<text x=\"%.1f\" y=\"26\" fill=\"#e8e6e1\" font-size=\"15\" "
                        + "font-weight=\"600\">%s</text>%n", left + 20, title));
        svg.append(String.format(
                "<text x=\"%.1f\" y=\"%.1f\" fill=\"#8d919b\" font-size=\"11\">"
                        + "全長 %.1f ／ 幅 %.1f ／ 部位 %d ／ 当たり判定 %d</text>%n",
                left + 20, PANEL_H - 20, height, widthBlocks, rig.partCount(),
                rig.interactiveParts().stream().mapToInt(Rig.Part::hitboxSegments).sum()));
    }

    // ------------------------------------------------------------ 幾何

    /** 根から指定の部位までの変換を合成した 4x4 行列（行優先）。 */
    private static double[] chainMatrix(Rig rig, String name) {
        double[] matrix = identity();
        for (Rig.Part part : rig.chain(name)) {
            matrix = multiply(matrix, translation(part.base().translation()));
            matrix = multiply(matrix, rotation(part.base().rotationDeg()));
        }
        return matrix;
    }

    /**
     * 見た目の直方体の8隅（部位の局所座標）。
     *
     * <p>アイテム表示は原点を中心に描かれるため、指定した拡大率の半分だけ手前に寄せる。
     * ブロック表示は最小角が原点なので、{@link Appearance} が持つずらしをそのまま使う。
     */
    private static List<P> corners(Appearance look) {
        double centering = look.block() ? 0 : -0.5;
        double x0 = look.offset().x() + look.scale().x() * centering;
        double y0 = look.offset().y() + look.scale().y() * centering;
        double z0 = look.offset().z() + look.scale().z() * centering;
        double x1 = x0 + look.scale().x();
        double y1 = y0 + look.scale().y();
        double z1 = z0 + look.scale().z();
        List<P> list = new ArrayList<>(8);
        for (double x : new double[] {x0, x1}) {
            for (double y : new double[] {y0, y1}) {
                for (double z : new double[] {z0, z1}) {
                    list.add(new P(x, y, z));
                }
            }
        }
        return list;
    }

    private static double[] identity() {
        return new double[] {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};
    }

    private static double[] translation(Vec3 value) {
        double[] m = identity();
        m[3] = value.x();
        m[7] = value.y();
        m[11] = value.z();
        return m;
    }

    /** X→Y→Z の順に回す回転行列。描画側の合成順に合わせる。 */
    private static double[] rotation(Vec3 degrees) {
        double x = Math.toRadians(degrees.x());
        double y = Math.toRadians(degrees.y());
        double z = Math.toRadians(degrees.z());
        double[] rx = {1, 0, 0, 0, 0, Math.cos(x), -Math.sin(x), 0,
                0, Math.sin(x), Math.cos(x), 0, 0, 0, 0, 1};
        double[] ry = {Math.cos(y), 0, Math.sin(y), 0, 0, 1, 0, 0,
                -Math.sin(y), 0, Math.cos(y), 0, 0, 0, 0, 1};
        double[] rz = {Math.cos(z), -Math.sin(z), 0, 0, Math.sin(z), Math.cos(z), 0, 0,
                0, 0, 1, 0, 0, 0, 0, 1};
        return multiply(multiply(rx, ry), rz);
    }

    private static double[] multiply(double[] a, double[] b) {
        double[] result = new double[16];
        for (int row = 0; row < 4; row++) {
            for (int column = 0; column < 4; column++) {
                double sum = 0;
                for (int k = 0; k < 4; k++) {
                    sum += a[row * 4 + k] * b[k * 4 + column];
                }
                result[row * 4 + column] = sum;
            }
        }
        return result;
    }

    private static P apply(double[] m, P point) {
        return new P(
                m[0] * point.x() + m[1] * point.y() + m[2] * point.z() + m[3],
                m[4] * point.x() + m[5] * point.y() + m[6] * point.z() + m[7],
                m[8] * point.x() + m[9] * point.y() + m[10] * point.z() + m[11]);
    }

    /** 投影した点の凸包（単調鎖法）。 */
    private static List<Q> hull(List<Q> points) {
        List<Q> sorted = new ArrayList<>(points);
        sorted.sort((a, b) -> a.x() == b.x()
                ? Double.compare(a.y(), b.y()) : Double.compare(a.x(), b.x()));
        List<Q> lower = new ArrayList<>();
        for (Q point : sorted) {
            while (lower.size() >= 2 && cross(lower.get(lower.size() - 2),
                    lower.get(lower.size() - 1), point) <= 0) {
                lower.remove(lower.size() - 1);
            }
            lower.add(point);
        }
        List<Q> upper = new ArrayList<>();
        for (int i = sorted.size() - 1; i >= 0; i--) {
            Q point = sorted.get(i);
            while (upper.size() >= 2 && cross(upper.get(upper.size() - 2),
                    upper.get(upper.size() - 1), point) <= 0) {
                upper.remove(upper.size() - 1);
            }
            upper.add(point);
        }
        lower.remove(lower.size() - 1);
        upper.remove(upper.size() - 1);
        lower.addAll(upper);
        return lower;
    }

    private static double cross(Q o, Q a, Q b) {
        return (a.x() - o.x()) * (b.y() - o.y()) - (a.y() - o.y()) * (b.x() - o.x());
    }

    // ------------------------------------------------------------ 配色

    private static String fill(String material) {
        return switch (material) {
            case "WHITE_CONCRETE" -> "#eceae3";
            case "QUARTZ_BLOCK" -> "#dcd6c8";
            case "BONE_BLOCK" -> "#e4dfd0";
            case "POLISHED_ANDESITE" -> "#7f8189";
            case "END_ROD" -> "#fbf7e6";
            case "NETHERITE_SWORD", "NETHERITE_AXE" -> "#524b57";
            default -> "#b9b7b0";
        };
    }

    private static String stroke(String material) {
        return switch (material) {
            case "POLISHED_ANDESITE" -> "#5c5e65";
            case "NETHERITE_SWORD", "NETHERITE_AXE" -> "#2e2a33";
            default -> "#9a968b";
        };
    }
}
