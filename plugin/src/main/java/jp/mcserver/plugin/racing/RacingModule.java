package jp.mcserver.plugin.racing;

import java.sql.SQLException;
import jp.mcserver.plugin.nation.NationLedger;
import org.bukkit.plugin.java.JavaPlugin;

/**
 * 競馬専用ワールド（`minecraft_server_spec.md` §27）の配線をまとめる。{@code RaidPlugin}
 * からは{@link #enable}/{@link #disable}だけを呼べばよい（{@code RailModule}・
 * {@code WorldCouncilModule}と同じ形）。
 *
 * <p>国庫データは{@code RailModule}が開く{@link NationLedger}を共有で借りる
 * （このクラスはcloseしない。所有者は{@code RailModule}のまま）。
 *
 * <p>実機で最初に触れる部分（{@code /horse create}・{@code /horse train}・
 * {@code /horse status}）だけを配線する。レース進行・特性・血統など§27の残りは
 * 次段階以降で{@code racing}パッケージへ追加していく。
 */
public final class RacingModule {

    private final JavaPlugin plugin;
    private final NationLedger ledger;
    private RacingDatabase database;

    public RacingModule(JavaPlugin plugin, NationLedger ledger) {
        this.plugin = plugin;
        this.ledger = ledger;
    }

    public void enable() {
        try {
            database = RacingDatabase.open(plugin.getDataFolder(), ledger, plugin.getLogger());
        } catch (SQLException e) {
            plugin.getLogger().severe("racing.db を開けなかった。競馬専用ワールドの機能は無効のまま: "
                    + e.getMessage());
            return;
        }

        var command = new RacingCommand(this);
        var registered = plugin.getServer().getPluginCommand("horse");
        if (registered != null) {
            registered.setExecutor(command);
        } else {
            plugin.getLogger().warning("plugin.yml に horse コマンドが無い");
        }

        plugin.getLogger().info("競馬専用ワールド（試験実装）を有効化した");
    }

    public void disable() {
        if (database != null) {
            database.close();
        }
        // ledger は RailModule が開いたものを共有するため、ここでは close しない
    }

    public NationLedger ledger() {
        return ledger;
    }

    public RacingDatabase database() {
        return database;
    }
}
