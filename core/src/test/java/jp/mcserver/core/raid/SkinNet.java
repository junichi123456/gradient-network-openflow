package jp.mcserver.core.raid;

import java.util.LinkedHashMap;
import java.util.Map;

/**
 * 箱の6面を1枚の絵に展開した割り付け（§12.6 の見た目づくり）。
 *
 * <p><b>狙いは「Windows のペイントで塗れる」ことである。</b>立体を扱う道具を使わずに
 * 見た目を作れるよう、部位の箱を展開図へ開き、面ごとの枠を画像の座標として決める。
 * モデルの UV はこの枠から機械的に書き出すので、描く側は<b>枠の中を塗るだけ</b>でよい。
 *
 * <p>並べ方は2通りを試し、<b>画素が大きく取れるほうを選ぶ</b>。細長い部位は横並び、
 * 奥行の深い部位（馬胴）は縦積みのほうが画布を使い切れる。
 *
 * <pre>
 *   横並び                          縦積み
 *        ┌────┬────┐               ┌────┬────┐
 *        │ 上 │ 下 │               │ 上 │ 下 │
 *   ┌────┼────┼────┼────┐          ├────┼────┤
 *   │ 右 │ 前 │ 左 │ 後 │          │ 右 │ 左 │
 *   └────┴────┴────┴────┘          ├────┼────┤
 *     D    W    D    W             │ 前 │ 後 │
 *                                  └────┴────┘
 * </pre>
 *
 * <p><b>どちらを選んでも塗る側は迷わない。</b>枠には面の頭文字が入っており
 * （{@link SkinTemplate}）、並びを覚えずに読めるようにしてある。
 *
 * <p>枠の中の向きは Minecraft の既定の UV に従うため、<b>その面を外から見た絵</b>を
 * そのまま描けばよい。前面の枠に顔を描けば前を向く。
 */
public final class SkinNet {

    /** 1枚の大きさ。2の冪でないと Minecraft がミップマップを作れない。 */
    public static final int CANVAS = 128;

    /** 面の名前。Minecraft のモデルが使う綴りに合わせる。 */
    public static final String NORTH = "north";
    public static final String EAST = "east";
    public static final String SOUTH = "south";
    public static final String WEST = "west";
    public static final String UP = "up";
    public static final String DOWN = "down";

    /** 面の名前 → 日本語。案内と塗り絵の目印に使う。 */
    public static String label(String face) {
        return switch (face) {
            case SOUTH -> "前";
            case NORTH -> "後";
            case EAST -> "右";
            case WEST -> "左";
            case UP -> "上";
            case DOWN -> "下";
            default -> face;
        };
    }

    /**
     * 展開図の中の1枠。単位は画素。
     *
     * <p>モデルの UV は画像の解像度に関係なく 0〜16 で書くため、{@link #uv()} で変換する。
     */
    public record Rect(int x, int y, int width, int height) {

        public Rect {
            if (width <= 0 || height <= 0) {
                throw new IllegalArgumentException("枠の大きさが0以下である");
            }
        }

        /** モデルに書く UV（0〜16）。 */
        public double[] uv() {
            return new double[] {uv(x), uv(y), uv(x + width), uv(y + height)};
        }

        /**
         * 枠を縦に等分した1段ぶんの UV。絞りのある部位は箱を積み重ねて近似するため、
         * 側面の絵も段の数だけ切り分けて貼る。
         *
         * @param step  0 が枠の上端（部位の付け根）
         * @param steps 段の数
         */
        public double[] band(int step, int steps) {
            double top = y + (double) height * step / steps;
            double bottom = y + (double) height * (step + 1) / steps;
            return new double[] {uv(x), uv(top), uv(x + width), uv(bottom)};
        }

        private static double uv(double pixels) {
            return pixels * 16.0 / CANVAS;
        }
    }

    private final Map<String, Rect> regions;
    private final String arrangement;

    private SkinNet(Map<String, Rect> regions, String arrangement) {
        this.regions = regions;
        this.arrangement = arrangement;
    }

    /** 並べ方の名前。案内に出す。 */
    public String arrangement() {
        return arrangement;
    }

    /**
     * 寸法から展開図を組む。
     *
     * <p>画素の密度は面をまたいで同じにする。どの面も同じ細かさで塗れるようにするためで、
     * 縦横比の歪みも出ない。そのぶん画像には余白が出るが、余白は<b>どの面にも貼られない</b>。
     *
     * @param size ブロック単位の寸法（幅・高さ・奥行）
     */
    public static SkinNet of(Vec3 size) {
        double width = Math.max(size.x(), 1e-6);
        double height = Math.max(size.y(), 1e-6);
        double depth = Math.max(size.z(), 1e-6);
        // 横並び: 幅 2(W+D)、高さ D+H ／ 縦積み: 幅 2max(W,D)、高さ D+2H
        double wide = Math.min(CANVAS / (2 * (width + depth)), CANVAS / (depth + height));
        double tall = Math.min(CANVAS / (2 * Math.max(width, depth)),
                CANVAS / (depth + 2 * height));
        return tall > wide ? tall(width, height, depth, tall)
                : wide(width, height, depth, wide);
    }

    /** 上段に天面と底面、下段に側面4つ。ふつうの部位はこちらが大きく取れる。 */
    private static SkinNet wide(double width, double height, double depth, double density) {
        int w = Math.max(1, (int) Math.round(width * density));
        int d = Math.max(1, (int) Math.round(depth * density));
        int h = Math.max(1, (int) Math.round(height * density));
        // 丸めで画布からはみ出すことがある。大きい辺から削る
        while (2 * (w + d) > CANVAS) {
            if (w >= d && w > 1) {
                w--;
            } else {
                d--;
            }
        }
        while (d + h > CANVAS && h > 1) {
            h--;
        }

        Map<String, Rect> regions = new LinkedHashMap<>();
        regions.put(UP, new Rect(d, 0, w, d));
        regions.put(DOWN, new Rect(d + w, 0, w, d));
        regions.put(EAST, new Rect(0, d, d, h));
        regions.put(SOUTH, new Rect(d, d, w, h));
        regions.put(WEST, new Rect(d + w, d, d, h));
        regions.put(NORTH, new Rect(2 * d + w, d, w, h));
        return new SkinNet(regions, "横並び");
    }

    /** 3段に積む。奥行が幅より深い部位（馬胴）はこちらが大きく取れる。 */
    private static SkinNet tall(double width, double height, double depth, double density) {
        int w = Math.max(1, (int) Math.round(width * density));
        int d = Math.max(1, (int) Math.round(depth * density));
        int h = Math.max(1, (int) Math.round(height * density));
        while (2 * Math.max(w, d) > CANVAS) {
            if (w >= d && w > 1) {
                w--;
            } else {
                d--;
            }
        }
        while (d + 2 * h > CANVAS && h > 1) {
            h--;
        }

        Map<String, Rect> regions = new LinkedHashMap<>();
        regions.put(UP, new Rect(0, 0, w, d));
        regions.put(DOWN, new Rect(w, 0, w, d));
        regions.put(EAST, new Rect(0, d, d, h));
        regions.put(WEST, new Rect(d, d, d, h));
        regions.put(SOUTH, new Rect(0, d + h, w, h));
        regions.put(NORTH, new Rect(w, d + h, w, h));
        return new SkinNet(regions, "縦積み");
    }

    public Rect region(String face) {
        Rect rect = regions.get(face);
        if (rect == null) {
            throw new IllegalArgumentException("知らない面である: " + face);
        }
        return rect;
    }

    /** 面 → 枠。並びは上・下・右・前・左・後。 */
    public Map<String, Rect> regions() {
        return Map.copyOf(regions);
    }

    /** 使っている画素の右端。案内に出す。 */
    public int usedWidth() {
        return regions.values().stream().mapToInt(r -> r.x() + r.width()).max().orElse(0);
    }

    /** 使っている画素の下端。案内に出す。 */
    public int usedHeight() {
        return regions.values().stream().mapToInt(r -> r.y() + r.height()).max().orElse(0);
    }
}
