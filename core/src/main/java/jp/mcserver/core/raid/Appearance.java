package jp.mcserver.core.raid;

/**
 * 部位の見た目（§12.6）。
 *
 * <p>リソースパックが無い状態でも「紙が並んでいる」以上の見た目になるよう、
 * <b>バニラの素材を組み合わせて体型を作る</b>ための記述を持つ。
 * リソースパックを配布したあとは {@link Rig.Part#modelId()} 側へ差し替える。
 *
 * <p>素材名は描画側（Bukkit の {@code Material}）で解決する。コア層は文字列として持つ。
 *
 * @param material   素材名。ブロックなら {@code WHITE_CONCRETE} のような列挙名
 * @param block      ブロック表示か。false ならアイテム表示
 * @param scale      拡大率。ブロック表示では<b>ブロック単位の寸法</b>と一致する
 * @param offset     原点からのずらし。ブロック表示は最小角が原点なので、中心を合わせるのに使う
 * @param decoration 見た目だけの部位か。true の部位には当たり判定を置かない
 * @param authored   リソースパックで描いたモデルを使うか。
 *                   true のとき、描画側はモデルを<b>等倍で</b>出す。
 *                   {@link #scale()} は寸法の宣言としてのみ残り、当たり判定に使われる
 * @param modelScale 描いたモデルにかける拡大率。<b>等倍のみ</b>で、テクスチャは歪まない。
 *                   モデルの座標は −16〜32（3ブロック）に収める必要があるため、
 *                   それを超える部位は縮めて描き、ここで戻す
 * @param taper      先端の細さ。付け根の断面に対する比率で、1.0 なら均一。
 *                   1.0 未満なら描画側が長辺に沿って輪切りにし、<b>円錐状</b>に描く。
 *                   槍のように手元が太く先が細い部位のために用いる
 */
public record Appearance(String material, boolean block, Vec3 scale, Vec3 offset,
                         boolean decoration, double taper, boolean authored,
                         double modelScale) {

    /**
     * 円錐状に描くときの輪切りの数。増やすほど滑らかだが、そのぶん表示実体が増える。
     *
     * <p>断面は<b>両端を含めて</b>等間隔に取る。6枚なら手元・先端を含む6段になり、
     * 手元12ピクセル・先端2ピクセルなら 12・10・8・6・4・2 と刻まれる。
     */
    public static final int TAPER_SLICES = 6;

    public Appearance {
        if (modelScale <= 0) {
            throw new IllegalArgumentException("モデルの拡大率が0以下である: " + material);
        }
        if (authored && block) {
            throw new IllegalArgumentException(
                    "描いたモデルはアイテム表示で出す。ブロック表示にはできない: " + material);
        }
        if (material == null || material.isBlank()) {
            throw new IllegalArgumentException("素材名が空である");
        }
        if (scale.x() <= 0 || scale.y() <= 0 || scale.z() <= 0) {
            throw new IllegalArgumentException("拡大率が0以下である: " + material);
        }
        if (taper <= 0 || taper > 1.0) {
            throw new IllegalArgumentException("先端の細さが範囲外である: " + material);
        }
    }

    /** 円錐状に描くか。 */
    public boolean tapered() {
        return taper < 1.0;
    }

    /** 描画に使う実体の数。 */
    public int slices() {
        return tapered() ? TAPER_SLICES : 1;
    }

    /**
     * 先端を細くした同じ見た目。付け根の断面はそのままで、先端が指定の比率まで絞られる。
     *
     * @param tipRatio 先端の断面 ÷ 付け根の断面
     */
    public Appearance taperedTo(double tipRatio) {
        return new Appearance(material, block, scale, offset, decoration, tipRatio, authored,
                modelScale);
    }

    /** 中心を部位の原点に合わせたブロック。寸法をそのまま与える。 */
    public static Appearance box(String material, double width, double height, double depth) {
        return new Appearance(material, true, new Vec3(width, height, depth),
                new Vec3(-width / 2, -height / 2, -depth / 2), false, 1.0, false, 1.0);
    }

    /** 下端を部位の原点に合わせたブロック。腕・足・槍のように「付け根から伸びる」部位に使う。 */
    public static Appearance limb(String material, double width, double length, double depth) {
        return new Appearance(material, true, new Vec3(width, length, depth),
                new Vec3(-width / 2, -length, -depth / 2), false, 1.0, false, 1.0);
    }

    /** アイテム表示。原点が中心になる。 */
    public static Appearance item(String material, double scale) {
        return new Appearance(material, false, new Vec3(scale, scale, scale), Vec3.ZERO,
                false, 1.0, false, 1.0);
    }

    /**
     * リソースパックで描いたモデルを使う見た目にする。
     *
     * <p>描画側は<b>拡大率1でモデルをそのまま出す</b>。寸法はモデル側が持ち、
     * ここで宣言した寸法は当たり判定にのみ使われる。バニラの素材を引き伸ばす方式と違い、
     * 非等倍の拡大でテクスチャが歪むことがない。
     *
     * @param baseItem モデルを載せるアイテム（例: {@code PAPER}）
     */
    public Appearance authoredAs(String baseItem, double scaleFactor) {
        return new Appearance(baseItem, false, scale, Vec3.ZERO, decoration, 1.0, true,
                scaleFactor);
    }

    /**
     * 描いたモデルの座標が収まる範囲（ブロック）。
     *
     * <p>Minecraft のモデルは −16〜32 の座標しか取れない。1ブロック16単位なので3ブロックである。
     */
    public static final double MODEL_SPACE_BLOCKS = 3.0;

    /** 等倍で描けるか。長辺がモデルの座標範囲に収まるか。 */
    public boolean fitsModelSpace() {
        return Math.max(scale.x(), Math.max(scale.y(), scale.z())) <= MODEL_SPACE_BLOCKS;
    }

    /** 当たり判定を持たない、見た目だけの部位にする。 */
    public Appearance asDecoration() {
        return new Appearance(material, block, scale, offset, true, taper, authored, modelScale);
    }

    /**
     * 長辺に沿った輪切りの1枚。付け根（添字 0）から先端へ向かって断面が絞られる。
     *
     * @param index 0 から {@link #slices()} − 1 まで
     * @return その1枚のずらしと拡大率
     */
    public Appearance slice(int index) {
        int count = slices();
        if (index < 0 || index >= count) {
            throw new IllegalArgumentException("輪切りの添字が範囲外である: " + index);
        }
        if (count == 1) {
            return this;
        }
        double length = scale.y() / count;
        // 断面は両端を含めて取る。index=0 が手元、index=count-1 が先端の太さになる
        double ratio = 1 + (taper - 1) * ((double) index / (count - 1));
        double width = scale.x() * ratio;
        double depth = scale.z() * ratio;
        // 付け根を y = offset.y + scale.y（＝部位の原点側）とし、そこから先端へ積む
        double top = offset.y() + scale.y() - length * index;
        return new Appearance(material, block, new Vec3(width, length, depth),
                new Vec3(-width / 2, top - length, -depth / 2), decoration, 1.0, false, 1.0);
    }
}
