package jp.mcserver.plugin.worldcouncil;

import org.bukkit.GameRule;
import org.bukkit.World;
import org.bukkit.plugin.Plugin;
import org.bukkit.scheduler.BukkitRunnable;

/**
 * 「世界協議」専用ワールドの日照サイクル制御（`world_council_spec.md`）。
 *
 * <p>「すべてのプレイヤーの行動が終わると、2秒のあいだに1日サイクルが進み、
 * ゲーム内時間で12:00（正午。{@code time} 換算で {@value #NOON}）で停止する」という
 * ユーザーの要求を実装する。<b>「行動が終わった」の判定そのもの</b>（BLOCK CONQUEST の
 * 8人の手番が一巡したという合図）はデータパック側の状態を必要とするため、今回の
 * 統合レイヤーの対象外——ここでは効果本体（時間の滑らかな進行と正午での固定）だけを
 * {@link #advanceToNoon} として用意し、{@code /worldcouncil advanceday} から呼べるようにする。
 * 実際の呼び出しタイミングは、盤面本体が実装されたときにそちらから叩く想定。
 */
public final class WorldCouncilDayCycle {

    /** 正午の {@code time}（0=夜明け,6000=正午,12000=夕暮れ,18000=深夜）。 */
    public static final long NOON = 6000L;

    /** 1日の長さ（tick）。 */
    public static final long DAY_LENGTH = 24000L;

    /** アニメーションにかける時間（tick）。2秒＝40tick。 */
    public static final int ANIMATION_TICKS = 40;

    private WorldCouncilDayCycle() {}

    /** 次に迎える正午の絶対時刻（{@link World#getFullTime()} 基準）。必ず現在より先を返す。 */
    public static long nextNoon(long currentFullTime) {
        long sinceMidnight = currentFullTime % DAY_LENGTH;
        long target = currentFullTime - sinceMidnight + NOON;
        if (target <= currentFullTime) {
            target += DAY_LENGTH;
        }
        return target;
    }

    /**
     * 2秒（{@value #ANIMATION_TICKS} tick）かけて次の正午まで滑らかに進め、到達したら
     * 昼夜サイクルの自動進行を止めて固定する。
     */
    public static void advanceToNoon(Plugin plugin, World world) {
        long start = world.getFullTime();
        long target = nextNoon(start);
        long span = target - start;

        new BukkitRunnable() {
            int tick = 0;

            @Override
            public void run() {
                tick++;
                long value = start + span * tick / ANIMATION_TICKS;
                if (tick >= ANIMATION_TICKS) {
                    value = target;
                }
                world.setFullTime(value);
                if (tick >= ANIMATION_TICKS) {
                    world.setGameRule(GameRule.DO_DAYLIGHT_CYCLE, false);
                    cancel();
                }
            }
        }.runTaskTimer(plugin, 1L, 1L);
    }
}
