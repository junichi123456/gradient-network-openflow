package jp.mcserver.plugin.worldcouncil;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import jp.mcserver.core.worldcouncil.WorldCouncilRoster;
import jp.mcserver.plugin.nation.NationLedger;
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
 */
public final class WorldCouncilModule {

    private final JavaPlugin plugin;
    private final NationLedger ledger;
    private final WorldCouncilArena arena = new WorldCouncilArena();

    /** 実効国家名 → 代表者名（最大2名）。登録順を UI 表示に使うため LinkedHashMap。 */
    private final Map<String, List<String>> roster = new LinkedHashMap<>();

    public WorldCouncilModule(JavaPlugin plugin, NationLedger ledger) {
        this.plugin = plugin;
        this.ledger = ledger;
    }

    public void enable() {
        plugin.getServer().getPluginManager().registerEvents(new WorldCouncilGuard(), plugin);

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
}
