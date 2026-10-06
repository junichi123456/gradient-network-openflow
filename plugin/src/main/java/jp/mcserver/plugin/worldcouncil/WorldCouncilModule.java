package jp.mcserver.plugin.worldcouncil;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import jp.mcserver.core.worldcouncil.WorldCouncilRoster;
import jp.mcserver.plugin.nation.NationLedger;
import org.bukkit.entity.Player;
import org.bukkit.event.EventHandler;
import org.bukkit.event.Listener;
import org.bukkit.event.player.PlayerJoinEvent;
import org.bukkit.plugin.java.JavaPlugin;

/**
 * 「世界協議」（`world_council_spec.md`）の配線をまとめる。{@code RaidPlugin} からは
 * {@link #enable} / {@link #disable} だけを呼べばよい（{@code RailModule} と同様の形）。
 *
 * <p>国庫データは {@code RailModule} が開く {@link NationLedger} を共有で借りる
 * （このクラスは close しない。所有者は {@code RailModule} のまま）。
 *
 * <p>参加登録（ロースター）は<b>常駐データベースを持たない</b>。開催の都度、管理者が
 * {@code /worldcouncil register}〜{@code finish} で組み立てて使い切る、一回性の状態として
 * 扱う（試合中にサーバーが再起動すると失われる。今回の統合レイヤーの範囲では許容する）。
 *
 * <p>ただし<b>参加者の持ち物は失わない</b>。退避は {@link WorldCouncilArena} がファイルに
 * 書いており、再起動後・開催終了後にログインした参加者は、その時点で国家ワールドへ戻す。
 */
public final class WorldCouncilModule implements Listener {

    private final JavaPlugin plugin;
    private final NationLedger ledger;
    private final WorldCouncilArena arena;

    /** {@code begin} から {@code finish} までのあいだか。 */
    private boolean inSession;

    /** 実効国家名 → 代表者名（最大2名）。登録順を UI 表示に使うため LinkedHashMap。 */
    private final Map<String, List<String>> roster = new LinkedHashMap<>();

    public WorldCouncilModule(JavaPlugin plugin, NationLedger ledger) {
        this.plugin = plugin;
        this.ledger = ledger;
        this.arena = new WorldCouncilArena(plugin);
    }

    public void enable() {
        plugin.getServer().getPluginManager().registerEvents(new WorldCouncilGuard(), plugin);
        plugin.getServer().getPluginManager().registerEvents(this, plugin);

        // 再起動をまたいだ退避は、開催が終わっているので戻す（ロースターは再起動で失われる）
        int pending = arena.loadPending();
        if (pending > 0) {
            plugin.getLogger().warning("世界協議: 復帰していない参加者が " + pending
                    + " 名いる。ログインした時点で国家ワールドへ戻す");
        }
        for (Player player : plugin.getServer().getOnlinePlayers()) {
            arena.exit(player);
        }

        var command = new WorldCouncilCommand(this);
        var registered = plugin.getServer().getPluginCommand("worldcouncil");
        if (registered != null) {
            registered.setExecutor(command);
        } else {
            plugin.getLogger().warning("plugin.yml に worldcouncil コマンドが無い");
        }

        plugin.getLogger().info("世界協議を有効化した");
    }

    public void disable() {
        // ledger は RailModule が閉じるため、ここでは何もしない
    }

    public JavaPlugin plugin() {
        return plugin;
    }

    public NationLedger ledger() {
        return ledger;
    }

    public WorldCouncilArena arena() {
        return arena;
    }

    public Map<String, List<String>> roster() {
        return roster;
    }

    public List<WorldCouncilRoster.Entry> rosterEntries() {
        List<WorldCouncilRoster.Entry> entries = new ArrayList<>();
        roster.forEach((nation, reps) -> entries.add(new WorldCouncilRoster.Entry(nation, reps)));
        return entries;
    }

    public void clearRoster() {
        roster.clear();
    }

    public boolean inSession() {
        return inSession;
    }

    public void setInSession(boolean value) {
        inSession = value;
    }

    /** その名前が、いま開催中の回の代表者か。 */
    public boolean isRepresentative(String playerName) {
        return inSession && roster.values().stream()
                .anyMatch(reps -> reps.contains(playerName));
    }

    /**
     * 退避が残ったままログインした参加者を戻す。開催中の代表者は戻さない
     * （回線落ちから戻ってきた場合で、試合はまだ続いている）。
     */
    @EventHandler
    public void onJoin(PlayerJoinEvent event) {
        Player player = event.getPlayer();
        if (!arena.isInside(player) || isRepresentative(player.getName())) {
            return;
        }
        // 参加処理の最中に持ち物とテレポートを触ると取りこぼすことがあるため、1tick 待つ
        plugin.getServer().getScheduler().runTask(plugin, () -> {
            if (player.isOnline() && arena.exit(player)) {
                player.sendMessage("§a世界協議の会場から国家ワールドへ戻しました（持ち物を元に戻しました）");
            }
        });
    }
}
