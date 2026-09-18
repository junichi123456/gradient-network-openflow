package jp.mcserver.plugin.nation;

import java.io.File;
import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.util.Optional;
import java.util.UUID;
import java.util.logging.Logger;
import jp.mcserver.core.NationalAccounts;

/**
 * 本物の国家プラグインがまだ無いあいだの、<b>サーバー全体で共有する国家代用データ</b>
 * （国庫・外交準備高・プレイヤーの所属・宗主国関係・同盟関係）。
 *
 * <p>元は `rail_infra_spec.md` の実装過程で「鉄道専用の最小限の代用品」として
 * {@code RailDatabase} 内に作られたものである（§6）。その後 `world_council_spec.md`
 * の「世界協議」も同じ国庫（{@code 国家予算(国庫)}）を必要としたため、鉄道専用の
 * 枠を外し、両モジュールが共有する台帳としてここへ切り出した。
 *
 * <p>本物の国家プラグインができたら、この1クラス（と {@code nation.db}）だけを
 * 差し替えればよい。呼び出し元は同じシグネチャの実装に挿し替わる想定である。
 *
 * <p>SQLite への単純な同期 JDBC 呼び出し。メインスレッドから呼ぶ前提であり、
 * 大量アクセスが問題になったら非同期化を検討する（今回は着手しない）。
 */
public final class NationLedger implements AutoCloseable {

    private final Connection connection;
    private final Logger logger;

    private NationLedger(Connection connection, Logger logger) {
        this.connection = connection;
        this.logger = logger;
    }

    public static NationLedger open(File dataFolder, Logger logger) throws SQLException {
        if (!dataFolder.exists() && !dataFolder.mkdirs()) {
            throw new SQLException("データフォルダを作成できない: " + dataFolder);
        }
        File file = new File(dataFolder, "nation.db");
        Connection connection = DriverManager.getConnection("jdbc:sqlite:" + file.getAbsolutePath());
        NationLedger ledger = new NationLedger(connection, logger);
        ledger.createSchema();
        return ledger;
    }

    private void createSchema() throws SQLException {
        try (Statement st = connection.createStatement()) {
            st.execute("""
                    CREATE TABLE IF NOT EXISTS rail_nations (
                        nation_id TEXT PRIMARY KEY,
                        treasury INTEGER NOT NULL DEFAULT 0,
                        reserve INTEGER NOT NULL DEFAULT 0,
                        suzerain_nation_id TEXT
                    )""");
            st.execute("""
                    CREATE TABLE IF NOT EXISTS rail_nation_players (
                        player_uuid TEXT PRIMARY KEY,
                        nation_id TEXT NOT NULL
                    )""");
            st.execute("""
                    CREATE TABLE IF NOT EXISTS rail_alliances (
                        nation_a TEXT NOT NULL,
                        nation_b TEXT NOT NULL,
                        PRIMARY KEY (nation_a, nation_b)
                    )""");
        }
    }

    @Override
    public void close() {
        try {
            connection.close();
        } catch (SQLException e) {
            logger.warning("nation.db を閉じる際にエラー: " + e.getMessage());
        }
    }

    // ------------------------------------------------------------ 所属

    /** プレイヤーの所属国家。未登録なら空。 */
    public Optional<String> nationOfPlayer(UUID playerId) {
        String sql = "SELECT nation_id FROM rail_nation_players WHERE player_uuid = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, playerId.toString());
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? Optional.of(rs.getString(1)) : Optional.empty();
            }
        } catch (SQLException e) {
            throw new NationLedgerException(e);
        }
    }

    /** プレイヤーの所属国家を設定する（運営コマンドの代用登録）。国家が無ければ作る。 */
    public void setNationOfPlayer(UUID playerId, String nationId) {
        ensureNation(nationId);
        String sql = "INSERT INTO rail_nation_players(player_uuid, nation_id) VALUES(?, ?) "
                + "ON CONFLICT(player_uuid) DO UPDATE SET nation_id = excluded.nation_id";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, playerId.toString());
            ps.setString(2, nationId);
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new NationLedgerException(e);
        }
    }

    // ------------------------------------------------------------ 国庫・外交準備高

    /** 国家が無ければ空の国庫で作る（べき等）。 */
    public void ensureNation(String nationId) {
        String sql = "INSERT OR IGNORE INTO rail_nations(nation_id, treasury, reserve) VALUES(?, 0, 0)";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, nationId);
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new NationLedgerException(e);
        }
    }

    public NationalAccounts.Balances balances(String nationId) {
        ensureNation(nationId);
        String sql = "SELECT treasury, reserve FROM rail_nations WHERE nation_id = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, nationId);
            try (ResultSet rs = ps.executeQuery()) {
                if (!rs.next()) {
                    return NationalAccounts.Balances.empty();
                }
                return new NationalAccounts.Balances(rs.getLong(1), rs.getLong(2));
            }
        } catch (SQLException e) {
            throw new NationLedgerException(e);
        }
    }

    public void saveBalances(String nationId, NationalAccounts.Balances balances) {
        ensureNation(nationId);
        String sql = "UPDATE rail_nations SET treasury = ?, reserve = ? WHERE nation_id = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setLong(1, balances.treasury());
            ps.setLong(2, balances.reserve());
            ps.setString(3, nationId);
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new NationLedgerException(e);
        }
    }

    /** 国庫への納入（運営コマンドの代用。援助金や世界協議の還付もこの経路を使う）。 */
    public void deposit(String nationId, long amount) {
        saveBalances(nationId, NationalAccounts.donate(balances(nationId), amount));
    }

    // ------------------------------------------------------------ 宗主国・同盟

    public void setSuzerain(String vassalNationId, String suzerainNationId) {
        ensureNation(vassalNationId);
        ensureNation(suzerainNationId);
        String sql = "UPDATE rail_nations SET suzerain_nation_id = ? WHERE nation_id = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, suzerainNationId);
            ps.setString(2, vassalNationId);
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new NationLedgerException(e);
        }
    }

    public Optional<String> suzerainOf(String nationId) {
        String sql = "SELECT suzerain_nation_id FROM rail_nations WHERE nation_id = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, nationId);
            try (ResultSet rs = ps.executeQuery()) {
                if (!rs.next() || rs.getString(1) == null) {
                    return Optional.empty();
                }
                return Optional.of(rs.getString(1));
            }
        } catch (SQLException e) {
            throw new NationLedgerException(e);
        }
    }

    /** 自国が宗主国として持つ属国の数（{@code DiplomacyQuota} の引数）。 */
    public int suzerainOfVassalCount(String nationId) {
        String sql = "SELECT COUNT(*) FROM rail_nations WHERE suzerain_nation_id = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, nationId);
            try (ResultSet rs = ps.executeQuery()) {
                rs.next();
                return rs.getInt(1);
            }
        } catch (SQLException e) {
            throw new NationLedgerException(e);
        }
    }

    public void addAlliance(String nationA, String nationB) {
        ensureNation(nationA);
        ensureNation(nationB);
        String a = nationA.compareTo(nationB) <= 0 ? nationA : nationB;
        String b = nationA.compareTo(nationB) <= 0 ? nationB : nationA;
        String sql = "INSERT OR IGNORE INTO rail_alliances(nation_a, nation_b) VALUES(?, ?)";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, a);
            ps.setString(2, b);
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new NationLedgerException(e);
        }
    }

    public int allianceCount(String nationId) {
        String sql = "SELECT COUNT(*) FROM rail_alliances WHERE nation_a = ? OR nation_b = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, nationId);
            ps.setString(2, nationId);
            try (ResultSet rs = ps.executeQuery()) {
                rs.next();
                return rs.getInt(1);
            }
        } catch (SQLException e) {
            throw new NationLedgerException(e);
        }
    }

    /** JDBC の検査例外を、呼び出し側の Bukkit イベント処理で扱いやすい非検査例外に包む。 */
    public static final class NationLedgerException extends RuntimeException {
        NationLedgerException(SQLException cause) {
            super("nation.db の操作に失敗した", cause);
        }
    }
}
