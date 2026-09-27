package jp.mcserver.core.racing;

import java.util.List;

/**
 * 競馬専用ワールドの調教メニューと、1回あたりのコスト（`minecraft_server_spec.md` §27.3・§27.11）。
 *
 * <p>調教は実時間で1日に1回だけ行え（毎日0:30更新、§27.3）、メニューの負荷（低・中・高）
 * に応じてexpを消費する（§27.11）。「なんとなく数だけ増やして走らせる」ことを割に合わない
 * 選択にするため、コストは国庫（§7）から自動徴収する。休養のみ無料。
 */
public enum TrainingMenu {
    POOL("低", 300, List.of(AbilityStat.STAMINA)),
    SLOPE("中", 600, List.of(AbilityStat.POWER)),
    WOOD_CHIP("中", 600, List.of(AbilityStat.SPEED)),
    PAIRED("高", 1_200, List.of(AbilityStat.GUTS, AbilityStat.WISDOM)),
    REST("なし", 0, List.of());

    private final String load;
    private final long costExp;
    private final List<AbilityStat> targetStats;

    TrainingMenu(String load, long costExp, List<AbilityStat> targetStats) {
        this.load = load;
        this.costExp = costExp;
        this.targetStats = targetStats;
    }

    /** この調教メニューの負荷（§27.3の表記に合わせた「低」「中」「高」「なし」）。 */
    public String load() {
        return load;
    }

    /** この調教メニューを1回行うのに国庫から徴収するexp（§27.11）。 */
    public long costExp() {
        return costExp;
    }

    /**
     * このメニューで成長する能力値（§27.3）。併せ馬調教は根性・賢さの両方が同じ結果
     * （{@link TrainingOutcome}）で成長する。休養は空リスト。
     */
    public List<AbilityStat> targetStats() {
        return targetStats;
    }
}
