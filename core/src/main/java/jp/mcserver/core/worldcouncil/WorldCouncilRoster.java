package jp.mcserver.core.worldcouncil;

import java.util.ArrayList;
import java.util.List;
import java.util.Set;
import java.util.random.RandomGenerator;

/**
 * 「世界協議」の参加登録。
 *
 * <p>最大参加国家数は4、1か国あたりの代表者数は2名に固定する（＝最大参加人数8名）。
 * これはBLOCK CONQUEST（4チーム×2名固定）の盤面・カード枚数・タイマー設計が
 * この人数を前提に組まれているための既定値である。
 *
 * <p>開催は管理者トリガーとし、参加登録できる国家はランク7以上に限る
 * （{@link WorldCouncilEligibility#MIN_RANK}）。属国は宗主国に統合されるため
 * （{@link WorldCouncilEligibility#effectiveNation}）、同一の実効国家を重複登録できない。
 */
public final class WorldCouncilRoster {

    private WorldCouncilRoster() {}

    public static final int MAX_NATIONS = 4;
    public static final int REPRESENTATIVES_PER_NATION = 2;
    public static final int MAX_PARTICIPANTS = MAX_NATIONS * REPRESENTATIVES_PER_NATION;

    public enum Denial {
        NONE, RANK_TOO_LOW, ALREADY_REGISTERED, NATION_LIMIT_REACHED,
        REPRESENTATIVE_LIMIT_REACHED, DUPLICATE_REPRESENTATIVE
    }

    public record Check(boolean allowed, Denial denial, String message) {}

    /** 登録済みの1か国分のエントリ。 */
    public record Entry(String effectiveNation, List<String> representatives) {}

    /** 国家の新規登録が可能かを判定する。 */
    public static Check canRegisterNation(String effectiveNation, int rank, List<Entry> currentEntries) {
        if (!WorldCouncilEligibility.eligible(rank)) {
            return new Check(false, Denial.RANK_TOO_LOW,
                    "rank" + WorldCouncilEligibility.MIN_RANK + " 未満は参加できません");
        }
        if (currentEntries.stream().anyMatch(e -> e.effectiveNation().equals(effectiveNation))) {
            return new Check(false, Denial.ALREADY_REGISTERED,
                    "既に登録されています（属国は宗主国に統合されます）");
        }
        if (currentEntries.size() >= MAX_NATIONS) {
            return new Check(false, Denial.NATION_LIMIT_REACHED,
                    "参加国家数の上限（" + MAX_NATIONS + "か国）に達しています");
        }
        return new Check(true, Denial.NONE, "登録できます");
    }

    /** 代表者の追加が可能かを判定する（1か国2名まで、他国との重複不可）。 */
    public static Check canAddRepresentative(String playerName, Entry entry, List<Entry> allEntries) {
        if (entry.representatives().size() >= REPRESENTATIVES_PER_NATION) {
            return new Check(false, Denial.REPRESENTATIVE_LIMIT_REACHED,
                    "代表者は1か国あたり " + REPRESENTATIVES_PER_NATION + " 名までです");
        }
        boolean duplicate = allEntries.stream()
                .flatMap(e -> e.representatives().stream())
                .anyMatch(p -> p.equals(playerName));
        if (duplicate) {
            return new Check(false, Denial.DUPLICATE_REPRESENTATIVE, "既に他国の代表者として登録されています");
        }
        return new Check(true, Denial.NONE, "追加できます");
    }

    /**
     * 開始時に実際に出場する代表者を決める（参加優先度）。
     *
     * <ol>
     *   <li>登録した代表者のうちオンラインの者（登録順）</li>
     *   <li>足りなければ、その国家に所属するオンラインの者をランダムな順で</li>
     *   <li>それでも足りなければ、その国家の属国に所属するオンラインの者をランダムな順で
     *       （属国は宗主国の枠に含める。{@link WorldCouncilEligibility#effectiveNation}）</li>
     * </ol>
     *
     * <p>役職は見ない（ユーザーへ確認して決定。役職のデータがまだ無い）。
     * {@link #REPRESENTATIVES_PER_NATION} 名に届かなければ、届いた分だけを返す。
     * 揃ったかは呼び出し側が人数で判断する。
     *
     * @param registered      登録した代表者
     * @param online          いまオンラインのプレイヤー名
     * @param nationMembers   その国家に所属するプレイヤー名
     * @param vassalMembers   その国家の属国に所属するプレイヤー名
     * @param unavailable     他国の代表者・他国の代わりに選ばれた者など、選んではならない名前
     * @param random          候補を並べる乱数
     */
    public static List<String> fillRepresentatives(List<String> registered, Set<String> online,
                                                   List<String> nationMembers,
                                                   List<String> vassalMembers,
                                                   Set<String> unavailable,
                                                   RandomGenerator random) {
        List<String> chosen = new ArrayList<>();
        for (String name : registered) {
            take(chosen, name, online, unavailable);
        }
        for (List<String> pool : List.of(nationMembers, vassalMembers)) {
            List<String> shuffled = new ArrayList<>(pool);
            for (int i = shuffled.size() - 1; i > 0; i--) {
                java.util.Collections.swap(shuffled, i, random.nextInt(i + 1));
            }
            for (String name : shuffled) {
                take(chosen, name, online, unavailable);
            }
        }
        return chosen;
    }

    private static void take(List<String> chosen, String name, Set<String> online,
                             Set<String> unavailable) {
        if (chosen.size() >= REPRESENTATIVES_PER_NATION || chosen.contains(name)
                || !online.contains(name) || unavailable.contains(name)) {
            return;
        }
        chosen.add(name);
    }

    /** 4か国×2名が揃い、開催可能な状態か。 */
    public static boolean ready(List<Entry> entries) {
        return entries.size() == MAX_NATIONS
                && entries.stream().allMatch(e -> e.representatives().size() == REPRESENTATIVES_PER_NATION);
    }
}
