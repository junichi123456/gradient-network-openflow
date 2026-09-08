package jp.mcserver.plugin.rail;

import java.sql.SQLException;
import org.bukkit.plugin.java.JavaPlugin;

/**
 * 地下鉄インフラ（`rail_infra_spec.md`）の配線をまとめる。{@code RaidPlugin} からは
 * {@link #enable} / {@link #disable} だけを呼べばよい。
 */
public final class RailModule {

    private final JavaPlugin plugin;
    private RailDatabase database;
    private RailConfig config;
    private MonthlyBillingTask billingTask;

    public RailModule(JavaPlugin plugin) {
        this.plugin = plugin;
    }

    public void enable() {
        plugin.saveDefaultConfig();
        try {
            database = RailDatabase.open(plugin.getDataFolder(), plugin.getLogger());
        } catch (SQLException e) {
            plugin.getLogger().severe("rail.db を開けなかった。地下鉄インフラは無効のまま: "
                    + e.getMessage());
            return;
        }
        config = RailConfig.load(plugin.getConfig(), plugin.getLogger());

        var listener = new RailListener(database, plugin.getLogger());
        plugin.getServer().getPluginManager().registerEvents(listener, plugin);
        plugin.getServer().getPluginManager().registerEvents(new VehicleSpeedListener(), plugin);

        billingTask = MonthlyBillingTask.scheduleDaily(plugin, database, plugin.getLogger());

        var command = new RailCommand(this);
        var registered = plugin.getServer().getPluginCommand("rail");
        if (registered != null) {
            registered.setExecutor(command);
        } else {
            plugin.getLogger().warning("plugin.yml に rail コマンドが無い");
        }

        plugin.getLogger().info("地下鉄インフラを有効化した（対象ブロック "
                + config.qualifyingBlocks().size() + "/" + RailConfig.DEFAULT_QUALIFYING_BLOCK_NAMES.size()
                + " 種を解決）");
    }

    public void disable() {
        if (billingTask != null) {
            billingTask.cancel();
        }
        if (database != null) {
            database.close();
        }
    }

    public void reloadConfig() {
        plugin.reloadConfig();
        config = RailConfig.load(plugin.getConfig(), plugin.getLogger());
    }

    public RailDatabase database() {
        return database;
    }

    public RailConfig config() {
        return config;
    }

    public MonthlyBillingTask billingTask() {
        return billingTask;
    }
}
