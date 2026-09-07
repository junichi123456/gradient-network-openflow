package jp.mcserver.plugin;

import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import jp.mcserver.core.Raid;
import org.bukkit.Location;
import org.bukkit.entity.Player;

/**
 * 開催中の1枠（§12.1 / §12.5）。
 *
 * <p><b>枠が始まった時点で参加を消費する。</b>全滅しても時間切れでも、その日は別の枠に
 * 入り直せない（§12.1）。辞退できるのは開始前だけである。
 *
 * <p>終わり方は3つある。討伐・時間切れ・全滅である。<b>時間切れと全滅では報酬が一切ない</b>
 * （§12.5）。討伐すれば、生存の有無に関わらず条件を満たした者へ配る。
 *
 * <p>死亡した参加者は<b>復帰できない</b>（§12.5）。死亡はレイド次元でも通常どおりに扱い、
 * アイテムはドロップし『消滅の呪い』も作用する。ここで介入はしない。
 */
final class RaidSession {

    /** 終わり方。 */
    enum Outcome {
        /** まだ続いている */
        RUNNING,
        /** 討伐した。報酬を配る */
        DEFEATED,
        /** 時間切れ。報酬なし */
        TIMEOUT,
        /** 全滅。報酬なし */
        WIPED
    }

    private final int day;
    private final int slot;
    private final Location arena;
    /** 参加者と、開始前に立っていた場所。終わったら戻す */
    private final Map<UUID, Location> came = new LinkedHashMap<>();
    /** まだ戦っている者。死亡すると外れる（§12.5 復帰できない） */
    private final Set<UUID> alive = new LinkedHashSet<>();
    private final long deadlineMillis;

    private KnightBoss boss;
    private Outcome outcome = Outcome.RUNNING;

    RaidSession(int day, int slot, Location arena, long startMillis) {
        this.day = day;
        this.slot = slot;
        this.arena = arena.clone();
        this.deadlineMillis = startMillis + Raid.timeLimitMillis();
    }

    int day() {
        return day;
    }

    int slot() {
        return slot;
    }

    Location arena() {
        return arena.clone();
    }

    Outcome outcome() {
        return outcome;
    }

    boolean running() {
        return outcome == Outcome.RUNNING;
    }

    KnightBoss boss() {
        return boss;
    }

    void boss(KnightBoss spawned) {
        this.boss = spawned;
    }

    /** 参加者を迎え入れる。立っていた場所を覚えておく。 */
    void admit(Player player) {
        came.put(player.getUniqueId(), player.getLocation().clone());
        alive.add(player.getUniqueId());
        player.teleport(arena);
    }

    /** 参加者（生死を問わない）。報酬の配布は生死を問わない（§12.5）。 */
    List<UUID> everyone() {
        return List.copyOf(came.keySet());
    }

    /** まだ戦っている人数。 */
    int aliveCount() {
        return alive.size();
    }

    int participantCount() {
        return came.size();
    }

    /** 死亡を記録する。復帰はできない（§12.5）。 */
    void fell(UUID player) {
        alive.remove(player);
    }

    /** 開始前に立っていた場所。終了時に戻す。 */
    Location cameFrom(UUID player) {
        return came.get(player);
    }

    /** 残り時間（ミリ秒）。 */
    long remainingMillis(long now) {
        return Math.max(0, deadlineMillis - now);
    }

    /** 残り時間の表示。 */
    String remaining(long now) {
        long seconds = remainingMillis(now) / 1000;
        return String.format("%d:%02d", seconds / 60, seconds % 60);
    }

    /**
     * 進み具合を見る。
     *
     * @param now いまの時刻（ミリ秒）
     * @return 終わり方。{@link Outcome#RUNNING} なら続いている
     */
    Outcome advance(long now) {
        if (outcome != Outcome.RUNNING) {
            return outcome;
        }
        if (boss != null && boss.isDead()) {
            outcome = Outcome.DEFEATED;
        } else if (remainingMillis(now) <= 0) {
            outcome = Outcome.TIMEOUT;
        } else if (came.size() > 0 && alive.isEmpty()) {
            outcome = Outcome.WIPED;
        }
        return outcome;
    }

    /** 途中で畳む（サーバーの停止など）。報酬は配らない。 */
    void abandon() {
        if (outcome == Outcome.RUNNING) {
            outcome = Outcome.TIMEOUT;
        }
    }
}
