package jp.mcserver.core.racing;

/**
 * 競馬専用ワールドの調教メニューと、1回あたりのコスト（`minecraft_server_spec.md` §27.3・§27.11）。
 *
 * <p>調教は週1回のサイクルで行い（§27.3）、メニューの負荷（低・中・高）に応じてexpを
 * 消費する（§27.11）。「なんとなく数だけ増やして走らせる」ことを割に合わない選択に
 * するため、コストは国庫（§7）から自動徴収する。休養のみ無料。
 */
public enum TrainingMenu {
    POOL("低", 300),
    SLOPE("中", 600),
    WOOD_CHIP("中", 600),
    PAIRED("高", 1_200),
    REST("なし", 0);

    private final String load;
    private final long costExp;

    TrainingMenu(String load, long costExp) {
        this.load = load;
        this.costExp = costExp;
    }

    /** この調教メニューの負荷（§27.3の表記に合わせた「低」「中」「高」「なし」）。 */
    public String load() {
        return load;
    }

    /** この調教メニューを1回行うのに国庫から徴収するexp（§27.11）。 */
    public long costExp() {
        return costExp;
    }
}
