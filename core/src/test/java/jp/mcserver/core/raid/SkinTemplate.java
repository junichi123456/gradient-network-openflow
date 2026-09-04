package jp.mcserver.core.raid;

import java.awt.image.BufferedImage;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.LinkedHashMap;
import java.util.Map;
import javax.imageio.ImageIO;

/**
 * 塗り絵の下地を書き出す（{@link SkinNet} の割り付けに合わせた PNG）。
 *
 * <p><b>Windows のペイントだけで見た目を作れるようにするための下地である。</b>
 * 面ごとの枠を明度差のある下地色で塗り分け、枠線と面の頭文字を入れてある。
 * 描く側は枠の中を塗り替えるだけでよく、立体を扱う道具は要らない。
 *
 * <p>透明度は使わない（{@code TYPE_INT_RGB}）。古いペイントは α を残せないため、
 * 塗り直したあとも壊れないようにしている。
 */
public final class SkinTemplate {

    private SkinTemplate() {
    }

    /** どの面にも貼られない余白。塗っても見た目に出ない。 */
    private static final int MARGIN = 0x1B2024;

    /** 面ごとの明るさ。上が明るく下が暗い、という当たり前の陰を最初から付けておく。 */
    private static final Map<String, Double> SHADE = shade();

    /** 素材名 → 下地色。バニラの素材の色みに寄せてある。 */
    private static final Map<String, Integer> BASE = base();

    /**
     * 下地を1枚書き出す。
     *
     * @param file     書き出し先
     * @param net      面の割り付け
     * @param material 素材名（下地色を引く鍵）
     */
    public static void write(Path file, SkinNet net, String material) throws IOException {
        BufferedImage image = new BufferedImage(
                SkinNet.CANVAS, SkinNet.CANVAS, BufferedImage.TYPE_INT_RGB);
        for (int y = 0; y < SkinNet.CANVAS; y++) {
            for (int x = 0; x < SkinNet.CANVAS; x++) {
                image.setRGB(x, y, MARGIN);
            }
        }
        int baseColor = BASE.getOrDefault(material, 0xE4E7E7);
        net.regions().forEach((face, rect) -> {
            int fill = mix(baseColor, SHADE.getOrDefault(face, 1.0));
            int edge = mix(fill, 0.62);
            fill(image, rect, fill);
            outline(image, rect, edge);
            mark(image, rect, face, mix(fill, 0.80));
        });
        Files.createDirectories(file.getParent());
        if (!ImageIO.write(image, "png", file.toFile())) {
            throw new IOException("PNG を書き出せなかった: " + file);
        }
    }

    // ------------------------------------------------------------------ 描画

    private static void fill(BufferedImage image, SkinNet.Rect rect, int color) {
        for (int y = rect.y(); y < rect.y() + rect.height(); y++) {
            for (int x = rect.x(); x < rect.x() + rect.width(); x++) {
                image.setRGB(x, y, color);
            }
        }
    }

    /** 枠線。ペイントで枠の境目が見えるようにするためで、そのまま装甲の合わせ目にもなる。 */
    private static void outline(BufferedImage image, SkinNet.Rect rect, int color) {
        int right = rect.x() + rect.width() - 1;
        int bottom = rect.y() + rect.height() - 1;
        for (int x = rect.x(); x <= right; x++) {
            image.setRGB(x, rect.y(), color);
            image.setRGB(x, bottom, color);
        }
        for (int y = rect.y(); y <= bottom; y++) {
            image.setRGB(rect.x(), y, color);
            image.setRGB(right, y, color);
        }
    }

    /**
     * 面の目印。狭い枠には入れない。塗り替えれば消える。
     *
     * <p>文字は<b>前後左右上下</b>の頭文字（F/B/R/L/U/D）である。モデルの面名
     * （north/south…）ではなく体の向きで読めるようにしている。
     */
    private static void mark(BufferedImage image, SkinNet.Rect rect, String face, int color) {
        int[] glyph = GLYPHS.get(LETTERS.get(face));
        if (glyph == null) {
            return;
        }
        int pixel = 2;
        int needWidth = 3 * pixel + 4;
        int needHeight = 5 * pixel + 4;
        if (rect.width() < needWidth || rect.height() < needHeight) {
            return;
        }
        int left = rect.x() + 2;
        int top = rect.y() + 2;
        for (int row = 0; row < 5; row++) {
            for (int column = 0; column < 3; column++) {
                if ((glyph[row] & (1 << (2 - column))) == 0) {
                    continue;
                }
                for (int dy = 0; dy < pixel; dy++) {
                    for (int dx = 0; dx < pixel; dx++) {
                        image.setRGB(left + column * pixel + dx, top + row * pixel + dy, color);
                    }
                }
            }
        }
    }

    private static int mix(int color, double factor) {
        int red = clamp((int) Math.round(((color >> 16) & 0xFF) * factor));
        int green = clamp((int) Math.round(((color >> 8) & 0xFF) * factor));
        int blue = clamp((int) Math.round((color & 0xFF) * factor));
        return (red << 16) | (green << 8) | blue;
    }

    private static int clamp(int value) {
        return Math.max(0, Math.min(255, value));
    }

    // ------------------------------------------------------------------ 対応表

    private static Map<String, Double> shade() {
        Map<String, Double> shades = new LinkedHashMap<>();
        shades.put(SkinNet.UP, 1.10);
        shades.put(SkinNet.SOUTH, 1.00);
        shades.put(SkinNet.EAST, 0.94);
        shades.put(SkinNet.WEST, 0.88);
        shades.put(SkinNet.NORTH, 0.82);
        shades.put(SkinNet.DOWN, 0.72);
        return shades;
    }

    private static Map<String, Integer> base() {
        Map<String, Integer> colors = new LinkedHashMap<>();
        colors.put("WHITE_CONCRETE", 0xE4E7E7);
        colors.put("QUARTZ_BLOCK", 0xECE7E0);
        colors.put("BONE_BLOCK", 0xE1DCC5);
        colors.put("POLISHED_ANDESITE", 0x86888A);
        colors.put("SMOOTH_QUARTZ", 0xEDE9E3);
        colors.put("NETHERITE_SWORD", 0x4B4349);
        colors.put("NETHERITE_AXE", 0x4B4349);
        return colors;
    }

    /** 面 → 目印の文字。前(Front)・後(Back)・右(Right)・左(Left)・上(Up)・下(Down)。 */
    private static final Map<String, Character> LETTERS = letters();

    private static Map<String, Character> letters() {
        Map<String, Character> letters = new LinkedHashMap<>();
        letters.put(SkinNet.SOUTH, 'F');
        letters.put(SkinNet.NORTH, 'B');
        letters.put(SkinNet.EAST, 'R');
        letters.put(SkinNet.WEST, 'L');
        letters.put(SkinNet.UP, 'U');
        letters.put(SkinNet.DOWN, 'D');
        return letters;
    }

    /** 3×5 の文字。字を出すためだけにフォントを持ち込まない。 */
    private static final Map<Character, int[]> GLYPHS = glyphs();

    private static Map<Character, int[]> glyphs() {
        Map<Character, int[]> glyphs = new LinkedHashMap<>();
        glyphs.put('F', new int[] {0b111, 0b100, 0b110, 0b100, 0b100});
        glyphs.put('B', new int[] {0b111, 0b101, 0b110, 0b101, 0b111});
        glyphs.put('R', new int[] {0b111, 0b101, 0b111, 0b110, 0b101});
        glyphs.put('L', new int[] {0b100, 0b100, 0b100, 0b100, 0b111});
        glyphs.put('U', new int[] {0b101, 0b101, 0b101, 0b101, 0b111});
        glyphs.put('D', new int[] {0b110, 0b101, 0b101, 0b101, 0b110});
        return glyphs;
    }
}
