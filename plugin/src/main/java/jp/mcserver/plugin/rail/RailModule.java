package jp.mcserver.plugin.rail;

import java.sql.SQLException;
import jp.mcserver.plugin.nation.NationLedger;
import org.bukkit.plugin.java.JavaPlugin;

/**
 * 地下鉄インフラ（`rail_infra_spec.md`）の配線をまとめる。{@code RaidPlugin} からは
 * {@link #enable} / {@link #disable} だけを呼べばよい。
 *
 * <p>国家代用の台帳（{@link NationLedger}）はここで開き、{@link #ledger()} 経由で
 * `world_council_spec.md`「世界協議」とも共有する（§「国庫データの置き場所」）。
 */
public final class RailModule {

    private final JavaPlugin plugin;
    private NationLedger ledger;
    private RailDatabase database;
    private RailConfig config;
    private final StationIndex stationIndex = new StationIndex();
    private MonthlyBillingTask billingTask;

    public RailModule(JavaPlugin plugin) {
        this.plugin = plugin;
    }

    public void enable() {
        plugin.saveDefaultConfig();
        try {
            ledger = NationLedger.open(plugin.getDataFolder(), plugin.getLogger());
            database = RailDatabase.open(plugin.getDataFolder(), ledger, plugin.getLogger());
        } catch (SQLException e) {
            plugin.getLogger().severe("rail.db / nation.db を開けなかった。地下鉄インフラは無効のまま: "
                    + e.getMessage());
            return;
        }
        config = RailConfig.load(plugin.getConfig(), plugin.getLogger());
        // 認定駅舎の一覧を起動時に一度だけ読み込み、以後はメモリだけで判定する
        // （Mobスポーンのたびに SQLite を叩かないようにするため。§6「負荷対策」）
        stationIndex.loadAll(database.activeStations());

        var listener = new RailListener(database, stationIndex);
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
                + " 種を解決、認定駅舎 " + stationIndex.size() + " 件を読み込み）");
    }

    public void disable() {
        if (billingTask != null) {
            billingTask.cancel();
        }
        if (database != null) {
            database.close();
        }
        // ledger は world_council_spec.md「世界協議」とも共有するため、RailModule が
        // 開いた以上はここで閉じる（WorldCouncilModule 側では close しない）
        if (ledger != null) {
            ledger.close();
        }
    }

    /** 国家代用の台帳。`world_council_spec.md`「世界協議」もこれを共有する。 */
    public NationLedger ledger() {
        return ledger;
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

    public StationIndex stationIndex() {
        return stationIndex;
    }

    public MonthlyBillingTask billingTask() {
        return billingTask;
    }
}
