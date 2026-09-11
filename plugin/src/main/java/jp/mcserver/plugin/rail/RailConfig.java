package jp.mcserver.plugin.rail;

import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;
import java.util.logging.Logger;
import org.bukkit.Material;
import org.bukkit.configuration.file.FileConfiguration;

/**
 * `config.yml` の {@code station.qualifying-blocks} を読む（F-04、`rail_infra_spec.md`）。
 *
 * <p>名前で持たせる理由は、認定可能17種のうち「硫黄レンガ」「辰砂レンガ」が
 * Minecraft 1.26.2 で追加されたブロックで、対応する {@code Material} 定数名を
 * まだ確認していないため（ユーザーへ確認済み・§6）。定数を直接書くとビルドできる
 * Paper API のバージョンが確定するまでコンパイルが通らないが、名前を文字列で持ち
 * {@link Material#matchMaterial(String)} で解決すれば、<b>コンパイルは常に通り、
 * 未対応の名前は起動時に警告してその1種だけ除外する</b>——config.yml を直すだけで
 * 直せる。
 */
public final class RailConfig {

    /** 要件定義書の17種に対応するデフォルトの {@code Material} 名（英語表記）。 */
    public static final List<String> DEFAULT_QUALIFYING_BLOCK_NAMES = List.of(
            "BRICKS",                         // レンガ
            "STONE_BRICKS",                   // 石レンガ
            "CHISELED_STONE_BRICKS",          // 模様入り石レンガ
            "MOSSY_STONE_BRICKS",             // 苔むした石レンガ
            "END_STONE_BRICKS",               // エンドストーンレンガ
            "QUARTZ_BRICKS",                  // クォーツレンガ
            "NETHER_BRICKS",                  // ネザーレンガ
            "CHISELED_NETHER_BRICKS",         // 模様入りのネザーレンガ
            "RED_NETHER_BRICKS",              // 赤いネザーレンガ
            "PRISMARINE_BRICKS",              // プリズマリンレンガ
            "POLISHED_BLACKSTONE_BRICKS",     // 磨かれたブラックストーンレンガ
            "DEEPSLATE_BRICKS",               // 深層岩レンガ
            "MUD_BRICKS",                     // 泥レンガ
            "TUFF_BRICKS",                    // 凝灰岩レンガ
            "RESIN_BRICKS",                   // 樹脂レンガ
            "SULFUR_BRICKS",                  // 硫黄レンガ（1.26.2。定数名は仮——要確認）
            "CINNABAR_BRICKS");                // 辰砂レンガ（1.26.2。定数名は仮——要確認）

    private final Set<Material> qualifyingBlocks;
    private final List<String> unresolvedNames;

    private RailConfig(Set<Material> qualifyingBlocks, List<String> unresolvedNames) {
        this.qualifyingBlocks = qualifyingBlocks;
        this.unresolvedNames = unresolvedNames;
    }

    public static RailConfig load(FileConfiguration config, Logger logger) {
        List<String> names = config.getStringList("station.qualifying-blocks");
        if (names.isEmpty()) {
            names = DEFAULT_QUALIFYING_BLOCK_NAMES;
        }
        Set<Material> resolved = new LinkedHashSet<>();
        List<String> unresolved = new ArrayList<>();
        for (String name : names) {
            Material material = Material.matchMaterial(name);
            if (material == null) {
                unresolved.add(name);
                continue;
            }
            resolved.add(material);
        }
        if (!unresolved.isEmpty()) {
            logger.warning("station.qualifying-blocks のうち、このサーバーの Material に"
                    + "無い名前を除外した（駅舎判定の対象から外れる）: " + unresolved
                    + "——config.yml を実際の Material 名に直すこと");
        }
        if (resolved.isEmpty()) {
            logger.warning("駅舎の対象ブロックが1つも解決できていない。駅舎はどれも認定されない");
        }
        return new RailConfig(resolved, unresolved);
    }

    public boolean isQualifying(Material material) {
        return qualifyingBlocks.contains(material);
    }

    public Set<Material> qualifyingBlocks() {
        return qualifyingBlocks;
    }

    /** config.yml に書かれていたが、この Paper のビルドでは解決できなかった名前。 */
    public List<String> unresolvedNames() {
        return unresolvedNames;
    }
}
