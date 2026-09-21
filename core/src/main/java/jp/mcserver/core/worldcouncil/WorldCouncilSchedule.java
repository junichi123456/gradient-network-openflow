package jp.mcserver.core.worldcouncil;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Optional;

/**
 * 「世界協議」の開催条件（§17 還付の規定）。
 *
 * <p>初回はサーバー開始から{@value #INITIAL_DELAY_DAYS}日後以降、以降は前回開催から
 * {@value #INTERVAL_DAYS}日経過後、参加資格（{@link WorldCouncilEligibility#MIN_RANK}）を
 * 満たしロックアウト中でない国家のうち、直近30日の生産額（§7.2）上位
 * {@value #SELECT_COUNT}か国を自動選抜する。4か国に満たない場合は開催を見送り、
 * 次の間隔（同じ判定を翌日以降も継続）まで待つ。
 *
 * <p>開催した回の上位1〜3位（選抜順ではなく、競技の最終順位）は
 * {@value #LOCKOUT_DAYS}日間、次回以降の選抜対象から外れる。4位は制限を受けない。
 * 28日間隔と33日ロックアウトを組み合わせているのは、間隔をロックアウト期間以上にすると
 * 次回時点で全員が解放され、参加国を絞り込む機構として機能しなくなるためである
 * （§17「参加制限が開催間隔を規定する」）。
 */
public final class WorldCouncilSchedule {

    private WorldCouncilSchedule() {}

    /** サーバー開始から初回開催までの最短日数。 */
    public static final int INITIAL_DELAY_DAYS = 90;

    /** 前回開催から次回開催が可能になるまでの最短日数。 */
    public static final int INTERVAL_DAYS = 28;

    /** 開催した回の上位1〜3位に課すロックアウト日数。 */
    public static final int LOCKOUT_DAYS = 33;

    /** 1回に選抜する国家数（{@link WorldCouncilRoster#MAX_NATIONS} と同じ）。 */
    public static final int SELECT_COUNT = WorldCouncilRoster.MAX_NATIONS;

    /** 開催が可能になる最短日。まだ一度も開催していなければ {@code lastHostDay} は null。 */
    public static long earliestHostDay(long serverStartDay, Long lastHostDay) {
        return lastHostDay == null
                ? serverStartDay + INITIAL_DELAY_DAYS
                : lastHostDay + INTERVAL_DAYS;
    }

    /** 日付だけで見た開催可能性（選抜可能な国家数は問わない）。 */
    public static boolean dateReady(long today, long serverStartDay, Long lastHostDay) {
        return today >= earliestHostDay(serverStartDay, lastHostDay);
    }

    /** 選抜候補の1か国分。{@code nation} は実効国家名（属国は宗主国に統合済み）。 */
    public record Candidate(String nation, int rank, long production30d) {}

    /** ロックアウト中の1件。{@code until} 日未満は参加できない。 */
    public record Lock(String nation, long until) {}

    /** その国家が今日時点でロックアウト中か。 */
    public static boolean isLocked(String nation, long today, List<Lock> locks) {
        return locks.stream().anyMatch(l -> l.nation().equals(nation) && today < l.until());
    }

    /**
     * 参加資格を満たし、ロックアウト中でない国家から、直近30日の生産額が高い順に
     * {@value #SELECT_COUNT}か国を選抜する。満たない場合は {@link Optional#empty()}
     * （開催を見送る）。
     */
    public static Optional<List<String>> selectParticipants(
            long today, List<Candidate> candidates, List<Lock> locks) {
        List<String> eligible = candidates.stream()
                .filter(c -> WorldCouncilEligibility.eligible(c.rank()))
                .filter(c -> !isLocked(c.nation(), today, locks))
                .sorted(Comparator.comparingLong(Candidate::production30d).reversed())
                .map(Candidate::nation)
                .limit(SELECT_COUNT)
                .toList();
        return eligible.size() < SELECT_COUNT ? Optional.empty() : Optional.of(eligible);
    }

    /**
     * 開催終了後のロック一覧を返す。期限切れの記録は捨て、{@code topThree}
     * （競技の最終順位1〜3位。4位は含めない）に新たなロックを加える。
     */
    public static List<Lock> withLocksAfterHosting(List<Lock> existing, long today, List<String> topThree) {
        List<Lock> updated = new ArrayList<>();
        for (Lock l : existing) {
            if (today < l.until()) {
                updated.add(l);
            }
        }
        for (String nation : topThree) {
            updated.add(new Lock(nation, today + LOCKOUT_DAYS));
        }
        return updated;
    }
}
