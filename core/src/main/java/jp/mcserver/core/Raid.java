package jp.mcserver.core;

import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;

/**
 * レイドイベント（§12）。隔週で実施する PvE の共同討伐。
 */
public final class Raid {

    private Raid() {}

    /** 開催周期（日）。 */
    public static final int CYCLE_DAYS = 14;

    /** 参加人数の上限。 */
    public static final int MAX_PARTICIPANTS = 20;

    /** 登録の締切（開始前の分）。 */
    public static final int REGISTRATION_CLOSES_MINUTES = 10;

    /** 制限時間（分）。 */
    public static final int TIME_LIMIT_MINUTES = 40;

    /** 種を追加するために必要な周回数（§12.2）。 */
    public static final int CYCLES_BEFORE_ADDITION = 2;

    /** 初回リリースの種類数。 */
    public static final int INITIAL_SPECIES = 5;

    /** 構想の総数。 */
    public static final int PLANNED_SPECIES = 11;

    /** 国家バフの効果期間（日）。 */
    public static final int NATION_BUFF_DAYS = 7;

    // ------------------------------------------------------------ 開催日

    /**
     * 次の開催日。
     *
     * @param anchorDay 初回の開催日（サーバー稼働日）
     * @param today     現在のサーバー稼働日
     */
    public static int nextSessionDay(int anchorDay, int today) {
        if (today <= anchorDay) {
            return anchorDay;
        }
        int elapsed = today - anchorDay;
        int cycles = (elapsed + CYCLE_DAYS - 1) / CYCLE_DAYS;
        return anchorDay + cycles * CYCLE_DAYS;
    }

    /** 開催回数（初回を1回目とする）。開催日以外を渡した場合は直近の開催回数を返す。 */
    public static int sessionNumber(int anchorDay, int today) {
        if (today < anchorDay) {
            return 0;
        }
        return (today - anchorDay) / CYCLE_DAYS + 1;
    }

    /** 次元の再生成（§1.1）と衝突しないか。開催枠の検証に用いる。 */
    public static boolean slotIsClear(int dayOfWeek, int hour, int dayOfMonth) {
        // エンド: 毎週土曜 15:00 / ネザー: 毎月1日 05:00 / 資源: 毎週火・金 03:00
        boolean end = dayOfWeek == 6 && hour == 15;
        boolean nether = dayOfMonth == 1 && hour == 5;
        boolean resource = (dayOfWeek == 2 || dayOfWeek == 5) && hour == 3;
        return !(end || nether || resource);
    }

    // ------------------------------------------------------------ ローテーション

    /**
     * 出現順のローテーション（§12.2）。
     *
     * @param roster 出現順に並べた種の識別子
     */
    public record Rotation(List<String> roster) {

        public Rotation {
            roster = List.copyOf(roster);
            if (roster.isEmpty()) {
                throw new IllegalArgumentException("ローテーションが空である");
            }
            if (new LinkedHashSet<>(roster).size() != roster.size()) {
                throw new IllegalArgumentException("同じ種が重複している");
            }
        }

        /** 指定回に出現する種。 */
        public String speciesFor(int sessionNumber) {
            if (sessionNumber < 1) {
                throw new IllegalArgumentException("開催回が不正である: " + sessionNumber);
            }
            return roster.get((sessionNumber - 1) % roster.size());
        }

        /** 完了した周回数。 */
        public int completedCycles(int sessionsHeld) {
            return sessionsHeld / roster.size();
        }

        /**
         * 種を追加できるか（§12.2）。
         * 現在の構成でローテーションが2周するまで追加しない。
         */
        public boolean canAddSpecies(int sessionsHeldSinceLastAddition) {
            return completedCycles(sessionsHeldSinceLastAddition) >= CYCLES_BEFORE_ADDITION;
        }

        /** 追加までに残っている開催回数。 */
        public int sessionsUntilAddition(int sessionsHeldSinceLastAddition) {
            int required = roster.size() * CYCLES_BEFORE_ADDITION;
            return Math.max(0, required - sessionsHeldSinceLastAddition);
        }

        /** 種を1つ追加した新しいローテーション。追加分は末尾に置き、次の周から回る。 */
        public Rotation add(String species) {
            if (roster.contains(species)) {
                throw new IllegalArgumentException("既に含まれている: " + species);
            }
            List<String> next = new ArrayList<>(roster);
            next.add(species);
            return new Rotation(next);
        }
    }

    // ------------------------------------------------------------ 難易度

    /** 参加者が1人増えるごとに加算される体力倍率（百分率）。 */
    public static final int HEALTH_PERCENT_PER_EXTRA_PARTICIPANT = 90;

    /**
     * 難易度（§12.3）。参加人数でスケールする。
     *
     * <p>体力倍率は <b>1 + 0.9 × (参加人数 − 1)</b>。2人で1.9倍、3人で2.8倍、20人で18.1倍。
     * 百分率の整数で保持するのは、浮動小数だと設定値どうしの比較や保存の往復で
     * 誤差が出るためである。
     */
    public record Difficulty(int healthPercent, int minions) {

        public double healthMultiplier() {
            return healthPercent / 100.0;
        }
    }

    public static Difficulty difficulty(int participants) {
        if (participants < 1 || participants > MAX_PARTICIPANTS) {
            throw new IllegalArgumentException("参加人数が範囲外である: " + participants);
        }
        int tier = (participants - 1) / 5; // 取り巻きは人数帯で増える
        return new Difficulty(
                100 + HEALTH_PERCENT_PER_EXTRA_PARTICIPANT * (participants - 1),
                2 + 2 * tier);
    }

    // ------------------------------------------------------------ 報酬

    /**
     * 報酬の配分（§12.4）。
     *
     * @param expPerParticipant 参加者1人あたりの exp
     * @param remainder         等分できない端数。世界政府へ償却する
     */
    public record Reward(long expPerParticipant, long remainder, boolean nationBuff) {}

    /**
     * 討伐報酬。討伐 exp は参加者へ等分し、端数は世界政府へ寄せる。
     *
     * @param cleared 討伐に成功したか。失敗時は報酬なし（§12.5）
     */
    public static Reward reward(long totalExp, int participants, boolean cleared) {
        if (participants < 1) {
            throw new IllegalArgumentException("参加者がいない");
        }
        if (!cleared) {
            return new Reward(0, 0, false);
        }
        long per = totalExp / participants;
        return new Reward(per, totalExp - per * participants, true);
    }

    /** 国家バフの終了日。 */
    public static int nationBuffExpiry(int clearedDay) {
        return clearedDay + NATION_BUFF_DAYS;
    }

    /** 初回討伐の称号を与えるか（§12.4）。種ごとに一度だけ。 */
    public static boolean firstClearTitle(Set<String> alreadyCleared, String species) {
        return !alreadyCleared.contains(species);
    }
}
