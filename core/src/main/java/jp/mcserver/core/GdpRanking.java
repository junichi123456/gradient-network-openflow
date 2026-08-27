package jp.mcserver.core;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;

/**
 * 国内総生産のランキング（§7.2）。
 *
 * <p>国内総生産 = 外交準備高 ＋ 国庫。総額のみを公開し、内訳は公開しない
 * （国庫の残高は自国民にのみ公開するという §7 の原則を保つため）。
 */
public final class GdpRanking {

    private GdpRanking() {}

    public record Entry(String nationName, long gdp) {}

    /** 順位つきの行。同額は同順位とし、次の順位はその分だけ飛ぶ。 */
    public record Row(int rank, String nationName, long gdp) {}

    public static List<Row> rank(List<Entry> entries) {
        List<Entry> sorted = new ArrayList<>(entries);
        sorted.sort(Comparator.comparingLong(Entry::gdp).reversed()
                .thenComparing(Entry::nationName));

        List<Row> rows = new ArrayList<>(sorted.size());
        int rank = 0;
        long previous = Long.MIN_VALUE;
        for (int i = 0; i < sorted.size(); i++) {
            Entry e = sorted.get(i);
            if (e.gdp() != previous) {
                rank = i + 1;
                previous = e.gdp();
            }
            rows.add(new Row(rank, e.nationName(), e.gdp()));
        }
        return rows;
    }
}
