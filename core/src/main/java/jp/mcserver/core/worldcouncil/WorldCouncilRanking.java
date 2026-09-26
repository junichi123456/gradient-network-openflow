package jp.mcserver.core.worldcouncil;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;

/**
 * 「世界協議」の最終順位。
 *
 * <p>BLOCK CONQUEST（アーティファクト仕様 §7.5）の勝敗判定をそのまま国家順位に用いる。
 * <ol>
 *   <li>実効国家の合計得点が高い順</li>
 *   <li>同点なら、代表者2名の得点差が小さい順（チームワーク優先）</li>
 *   <li>なお同点なら同順位（共同優勝）とし、次の順位はその分だけ飛ぶ</li>
 * </ol>
 */
public final class WorldCouncilRanking {

    private WorldCouncilRanking() {}

    public record Entry(String effectiveNation, long totalScore, long scoreDiff) {}

    public record Row(int rank, String effectiveNation, long totalScore, long scoreDiff) {}

    public static List<Row> rank(List<Entry> entries) {
        List<Entry> sorted = new ArrayList<>(entries);
        sorted.sort(Comparator.comparingLong(Entry::totalScore).reversed()
                .thenComparingLong(Entry::scoreDiff)
                .thenComparing(Entry::effectiveNation));

        List<Row> rows = new ArrayList<>(sorted.size());
        int rank = 0;
        long previousScore = Long.MIN_VALUE;
        long previousDiff = Long.MIN_VALUE;
        boolean first = true;
        for (int i = 0; i < sorted.size(); i++) {
            Entry e = sorted.get(i);
            boolean tie = !first && previousScore == e.totalScore() && previousDiff == e.scoreDiff();
            if (!tie) {
                rank = i + 1;
            }
            previousScore = e.totalScore();
            previousDiff = e.scoreDiff();
            first = false;
            rows.add(new Row(rank, e.effectiveNation(), e.totalScore(), e.scoreDiff()));
        }
        return rows;
    }
}
