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
 */
public record Appearance(String material, boolean block, Vec3 scale, Vec3 offset,
                         boolean decoration) {

    public Appearance {
        if (material == null || material.isBlank()) {
            throw new IllegalArgumentException("素材名が空である");
        }
        if (scale.x() <= 0 || scale.y() <= 0 || scale.z() <= 0) {
            throw new IllegalArgumentException("拡大率が0以下である: " + material);
        }
    }

    /** 中心を部位の原点に合わせたブロック。寸法をそのまま与える。 */
    public static Appearance box(String material, double width, double height, double depth) {
        return new Appearance(material, true, new Vec3(width, height, depth),
                new Vec3(-width / 2, -height / 2, -depth / 2), false);
    }

    /** 下端を部位の原点に合わせたブロック。腕・足・槍のように「付け根から伸びる」部位に使う。 */
    public static Appearance limb(String material, double width, double length, double depth) {
        return new Appearance(material, true, new Vec3(width, length, depth),
                new Vec3(-width / 2, -length, -depth / 2), false);
    }

    /** アイテム表示。原点が中心になる。 */
    public static Appearance item(String material, double scale) {
        return new Appearance(material, false, new Vec3(scale, scale, scale), Vec3.ZERO, false);
    }

    /** 当たり判定を持たない、見た目だけの部位にする。 */
    public Appearance asDecoration() {
        return new Appearance(material, block, scale, offset, true);
    }
}
