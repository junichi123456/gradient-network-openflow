package jp.mcserver.core.worldcouncil;

import java.util.List;

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

    /** 4か国×2名が揃い、開催可能な状態か。 */
    public static boolean ready(List<Entry> entries) {
        return entries.size() == MAX_NATIONS
                && entries.stream().allMatch(e -> e.representatives().size() == REPRESENTATIVES_PER_NATION);
    }
}
