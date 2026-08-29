package jp.mcserver.core;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * レイドイベント（§12）。隔週で実施する PvE の共同討伐。
 */
public final class Raid {

    private Raid() {}

    /** 開催周期（日）。 */
    public static final int CYCLE_DAYS = 14;

    /**
     * 参加人数の上限。
     *
     * <p><b>12名。</b>個体は1体のモブではなく、部位ごとの表示エンティティと当たり判定の集まりで
     * あるため（§12.6）、位置の更新が「実体の数 × 20Hz × 観戦者数」で増える。
     * 騎士型の第二形態（表示19・判定17）を1体出した場合、12名で毎秒およそ 10,900 件、
     * 20名では 18,200 件になる。
     *
     * <p>12名に置くのは、<b>描画側を最適化せずに済ませる</b>ための線である。
     * これを超えて増やす場合は、部位をまとめて動かす仕組み（騎乗など）が先に要る。
     */
    public static final int MAX_PARTICIPANTS = 12;

    /** 登録の締切（開始前の分）。 */
    public static final int REGISTRATION_CLOSES_MINUTES = 10;

    /** 開催枠の間隔（時）。同じ開催日に3時間ごとに実施する（§12.1）。 */
    public static final int SLOT_INTERVAL_HOURS = 3;

    /** 最初の枠の開始時刻（時）。 */
    public static final int FIRST_SLOT_HOUR = 6;

    /** 1日の開催枠数。 */
    public static final int SLOTS_PER_DAY = 6;

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

    /**
     * その国家に国家バフを付与できるか（§12.4）。
     *
     * <p><b>同じ開催日では重複しない。</b>その日の最初の討伐で7日間付与し、
     * 以降の枠で討伐しても延長も重複もしない。枠を並べるのは参加できる人数を
     * 増やすためであり、国民の多い国が枠を埋めて効果を重ねる道は塞ぐ。
     *
     * @param buffedToday その開催日にすでにバフを得た国家
     */
    public static boolean nationBuffGranted(Set<String> buffedToday, String nation) {
        return !buffedToday.contains(nation);
    }

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

    // ------------------------------------------------------------ 開催枠

    /**
     * 開催枠の開始時刻の一覧（§12.1）。
     *
     * <p>参加人数の上限が12名であるため（§12.3）、1枠だけでは同時接続の多い時間帯に
     * 参加できない者が出る。同じ開催日に枠を並べ、<b>1人が入れるのは同日1枠だけ</b>とする。
     */
    public static List<Integer> slotHours() {
        List<Integer> hours = new ArrayList<>(SLOTS_PER_DAY);
        for (int i = 0; i < SLOTS_PER_DAY; i++) {
            hours.add(FIRST_SLOT_HOUR + i * SLOT_INTERVAL_HOURS);
        }
        return hours;
    }

    /** 枠番号（1始まり）の開始時刻。 */
    public static int slotHour(int slot) {
        if (slot < 1 || slot > SLOTS_PER_DAY) {
            throw new IllegalArgumentException("枠番号が範囲外である: " + slot);
        }
        return FIRST_SLOT_HOUR + (slot - 1) * SLOT_INTERVAL_HOURS;
    }

    /** その時刻に始まる枠の番号。枠でない時刻なら 0。 */
    public static int slotAt(int hour) {
        int offset = hour - FIRST_SLOT_HOUR;
        if (offset < 0 || offset % SLOT_INTERVAL_HOURS != 0) {
            return 0;
        }
        int slot = offset / SLOT_INTERVAL_HOURS + 1;
        return slot <= SLOTS_PER_DAY ? slot : 0;
    }

    /** 1日に受け入れられる延べ人数。 */
    public static int dailyCapacity() {
        return SLOTS_PER_DAY * MAX_PARTICIPANTS;
    }

    /**
     * 最後の枠が終わる時刻（時・分）。制限時間ぶんを足す。
     * 日をまたがないことを確かめるために用いる。
     */
    public static int lastSlotEndMinuteOfDay() {
        return (slotHour(SLOTS_PER_DAY) * 60) + TIME_LIMIT_MINUTES;
    }

    /**
     * 最初の枠の登録開始が前日にずれ込まないか。
     * 締切は開始の {@value #REGISTRATION_CLOSES_MINUTES} 分前である。
     */
    public static boolean registrationFitsInDay() {
        return FIRST_SLOT_HOUR * 60 - REGISTRATION_CLOSES_MINUTES >= 0
                && lastSlotEndMinuteOfDay() <= 24 * 60;
    }

    /** 次元の再生成（§1.1）と衝突しないか。開催枠の検証に用いる。 */
    public static boolean slotIsClear(int dayOfWeek, int hour, int dayOfMonth) {
        // エンド: 毎週土曜 15:00 / ネザー: 毎月1日 05:00 / 資源: 毎週火・金 03:00
        boolean end = dayOfWeek == 6 && hour == 15;
        boolean nether = dayOfMonth == 1 && hour == 5;
        boolean resource = (dayOfWeek == 2 || dayOfWeek == 5) && hour == 3;
        return !(end || nether || resource);
    }

    /** 登録の結果（§12.1）。 */
    public enum Entry {
        /** 受け付けた。 */
        ACCEPTED,
        /** その時刻に枠がない。 */
        NO_SLOT,
        /** その枠は満員である。 */
        SLOT_FULL,
        /** 同じ開催日にすでに参加している。 */
        ALREADY_TODAY;

        public boolean accepted() {
            return this == ACCEPTED;
        }
    }

    /**
     * 同じ開催日の参加登録（§12.1）。
     *
     * <p><b>1人が参加できるのは同日1枠だけである。</b>枠を並べるのは参加できる人数を
     * 増やすためであり、同じ人が周回して報酬を重ねるためではない。
     */
    public static final class DailyEntry {

        /** 開催日 → 枠番号 → 参加者 */
        private final Map<Integer, Map<Integer, Set<String>>> byDay = new HashMap<>();

        /** 開催日 → 参加者 → 枠番号 */
        private final Map<Integer, Map<String, Integer>> slotOfPlayer = new HashMap<>();

        /** 開催日 → すでに開始した枠。開始後は辞退できない */
        private final Map<Integer, Set<Integer>> started = new HashMap<>();

        /**
         * 登録する。
         *
         * @param day    開催日（サーバー稼働日）
         * @param slot   枠番号（1始まり）
         * @param player 参加者
         */
        public Entry register(int day, int slot, String player) {
            if (slot < 1 || slot > SLOTS_PER_DAY) {
                return Entry.NO_SLOT;
            }
            if (hasParticipated(day, player)) {
                return Entry.ALREADY_TODAY;
            }
            Set<String> members = byDay
                    .computeIfAbsent(day, key -> new HashMap<>())
                    .computeIfAbsent(slot, key -> new LinkedHashSet<>());
            if (members.size() >= MAX_PARTICIPANTS) {
                return Entry.SLOT_FULL;
            }
            members.add(player);
            slotOfPlayer.computeIfAbsent(day, key -> new HashMap<>()).put(player, slot);
            return Entry.ACCEPTED;
        }

        /** その日にすでに参加しているか。 */
        public boolean hasParticipated(int day, String player) {
            return slotOfPlayer.getOrDefault(day, Map.of()).containsKey(player);
        }

        /** その日に入っている枠。入っていなければ 0。 */
        public int slotOf(int day, String player) {
            return slotOfPlayer.getOrDefault(day, Map.of()).getOrDefault(player, 0);
        }

        /** その枠の参加者。登録順に並ぶ。 */
        public List<String> participants(int day, int slot) {
            return List.copyOf(byDay.getOrDefault(day, Map.of())
                    .getOrDefault(slot, Set.of()));
        }

        /** その枠の残り人数。 */
        public int remaining(int day, int slot) {
            return MAX_PARTICIPANTS - participants(day, slot).size();
        }

        /** その枠が満員か。 */
        public boolean isFull(int day, int slot) {
            return remaining(day, slot) <= 0;
        }

        /** その日にまだ空きのある枠。案内に用いる。 */
        public List<Integer> openSlots(int day) {
            List<Integer> open = new ArrayList<>();
            for (int slot = 1; slot <= SLOTS_PER_DAY; slot++) {
                if (!isFull(day, slot)) {
                    open.add(slot);
                }
            }
            return open;
        }

        /** その日の延べ参加者数。 */
        public int participantCount(int day) {
            return slotOfPlayer.getOrDefault(day, Map.of()).size();
        }

        /**
         * 枠を開始する。以降その枠の参加者は辞退できない。
         *
         * <p><b>参加は成否を問わず使い切る</b>（§12.1）。全滅しても時間切れでも、
         * その日は別の枠に入り直せない。開始した時点で枠を消費したものとして扱う。
         */
        public void start(int day, int slot) {
            if (slot < 1 || slot > SLOTS_PER_DAY) {
                throw new IllegalArgumentException("枠番号が範囲外である: " + slot);
            }
            started.computeIfAbsent(day, key -> new LinkedHashSet<>()).add(slot);
        }

        /** その枠がすでに開始しているか。 */
        public boolean started(int day, int slot) {
            return started.getOrDefault(day, Set.of()).contains(slot);
        }

        /** 登録を取り消す。締切前の辞退にのみ用いる。開始した枠からは抜けられない。 */
        public boolean cancel(int day, String player) {
            int slot = slotOf(day, player);
            if (slot == 0 || started(day, slot)) {
                return false;
            }
            Map<Integer, Set<String>> slots = byDay.get(day);
            if (slots != null && slots.get(slot) != null) {
                slots.get(slot).remove(player);
            }
            slotOfPlayer.get(day).remove(player);
            return true;
        }
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
     * <p>体力倍率は <b>1 + 0.9 × (参加人数 − 1)</b>。2人で1.9倍、3人で2.8倍、12人で10.9倍。
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
