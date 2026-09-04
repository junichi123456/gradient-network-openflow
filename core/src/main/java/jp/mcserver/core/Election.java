package jp.mcserver.core;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Map;

/**
 * 共和制の首長選挙（§9.1）。
 *
 * <p>任期60日。議決権は実効国民の1人1票。立候補資格はリーダーまたはチーフの在任者のみ。
 * 立候補者ゼロまたは投票者ゼロで不成立となり、現職が自動続投する。
 */
public final class Election {

    private Election() {}

    /** 任期（日）。 */
    public static final int TERM_DAYS = 60;

    /** 立候補できるか（§9.1）。 */
    public static boolean canRun(Role role) {
        return role == Role.LEADER || role == Role.CHIEF;
    }

    /**
     * 候補者。
     *
     * @param contribution 貢献度（§6.3）。得票が同数の場合の決定に用いる
     */
    public record Candidate(String playerName, Role role, double contribution) {}

    public enum Outcome {
        /** 得票により当選した。 */
        ELECTED,
        /** 立候補者がいないため現職が続投する。 */
        NO_CANDIDATE,
        /** 投票者がいないため現職が続投する。 */
        NO_VOTER
    }

    public record Result(Outcome outcome, String headName, int votes, String reason) {
        public boolean incumbentContinues() {
            return outcome != Outcome.ELECTED;
        }
    }

    /**
     * 開票（§9.1）。
     *
     * @param candidates    立候補者。資格のない者は除外される
     * @param votes         候補者名 → 得票数
     * @param incumbentName 現職の首長
     */
    public static Result tally(List<Candidate> candidates, Map<String, Integer> votes,
                               String incumbentName) {
        List<Candidate> eligible = new ArrayList<>();
        for (Candidate c : candidates) {
            if (canRun(c.role())) {
                eligible.add(c);
            }
        }
        if (eligible.isEmpty()) {
            return new Result(Outcome.NO_CANDIDATE, incumbentName, 0,
                    "立候補者がいないため現職が続投します");
        }
        int total = 0;
        for (Candidate c : eligible) {
            total += votes.getOrDefault(c.playerName(), 0);
        }
        if (total == 0) {
            return new Result(Outcome.NO_VOTER, incumbentName, 0,
                    "投票者がいないため現職が続投します");
        }

        // 得票が多い順、同数なら貢献度が高い順、なお同値なら現職を優先し、最後は名前で決定的に選ぶ
        List<Candidate> sorted = new ArrayList<>(eligible);
        sorted.sort(Comparator
                .comparingInt((Candidate c) -> -votes.getOrDefault(c.playerName(), 0))
                .thenComparing(Comparator.comparingDouble(Candidate::contribution).reversed())
                .thenComparing(c -> c.playerName().equals(incumbentName) ? 0 : 1)
                .thenComparing(Candidate::playerName));

        Candidate winner = sorted.get(0);
        int won = votes.getOrDefault(winner.playerName(), 0);
        return new Result(Outcome.ELECTED, winner.playerName(), won,
                winner.playerName() + " が " + won + " 票で当選しました");
    }

    /** 次の選挙日。 */
    public static int nextElectionDay(int termStartDay) {
        return termStartDay + TERM_DAYS;
    }
}
