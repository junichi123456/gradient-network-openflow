package jp.mcserver.core;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;

/**
 * 国内総生産のランキング（§7.2）。
 *
 * <p>国内総生産 = 外交準備高 ＋ 国庫。総額のみを公開し、内訳は公開しない
 * （国庫の残高は自国民にのみ公開するという §7 の原則を保つため）。
 *
 * <p>蓄積である国内総生産に加え、直近30日の生産額を併記する。
 * 前者は「これまでどれだけ積んだか」、後者は「いま伸びているか」を示す。
 */
public final class GdpRanking {

    private GdpRanking() {}

    /**
     * @param gdp           外交準備高 ＋ 国庫
     * @param production30d 直近30日に外交準備高へ計上された額
     */
    public record Entry(String nationName, long gdp, long production30d) {}

    /** 順位つきの行。同額は同順位とし、次の順位はその分だけ飛ぶ。 */
    public record Row(int rank, String nationName, long gdp, long production30d) {}

    /** 国内総生産の順に並べる。 */
    public static List<Row> rank(List<Entry> entries) {
        return rankBy(entries, Entry::gdp);
    }

    /** 直近30日の生産額の順に並べる。 */
    public static List<Row> rankByProduction(List<Entry> entries) {
        return rankBy(entries, Entry::production30d);
    }

    private static List<Row> rankBy(List<Entry> entries, java.util.function.ToLongFunction<Entry> key) {
        List<Entry> sorted = new ArrayList<>(entries);
        sorted.sort(Comparator.comparingLong(key).reversed()
                .thenComparing(Entry::nationName));

        List<Row> rows = new ArrayList<>(sorted.size());
        int rank = 0;
        long previous = Long.MIN_VALUE;
        for (int i = 0; i < sorted.size(); i++) {
            Entry e = sorted.get(i);
            long value = key.applyAsLong(e);
            if (value != previous) {
                rank = i + 1;
                previous = value;
            }
            rows.add(new Row(rank, e.nationName(), e.gdp(), e.production30d()));
        }
        return rows;
    }
}
