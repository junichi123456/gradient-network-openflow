package jp.mcserver.plugin.rail;

import java.io.File;
import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import java.util.UUID;
import java.util.logging.Logger;
import jp.mcserver.core.NationalAccounts;
import jp.mcserver.core.rail.RailType;

/**
 * 地下鉄インフラの永続化（`rail_infra_spec.md` §4）と、本物の国家プラグインが
 * まだ無いあいだの<b>鉄道専用の最小限の代用品</b>（ユーザーへ確認して決定。§6）。
 *
 * <p>プレイヤー→国家、国家→国庫残高・同盟数・属国関係、チャンク→所属国、といった
 * 「本来は国家プラグインが持つはずのデータ」を、この1ファイルの SQLite に間借りして持つ。
 * 本物の国家プラグインができたら、この代用テーブル（{@code rail_nations} /
 * {@code rail_nation_players} / {@code rail_alliances} / {@code rail_claims}）だけを
 * 差し替えればよいよう、`rail_data` / `nation_monthly_data` / `station_data`（要件定義書
 * どおりの本来のテーブル）とは分けてある。
 *
 * <p>SQLite への単純な同期 JDBC 呼び出しである。呼び出し元（{@link RailListener} 等）は
 * メインスレッドから呼ぶため、大量のレールを一度に処理する運用になったら非同期化を検討する
 * ——実機の負荷を見てから判断する（今回は着手しない）。
 */
public final class RailDatabase implements AutoCloseable {

    private final Connection connection;
    private final Logger logger;

    private RailDatabase(Connection connection, Logger logger) {
        this.connection = connection;
        this.logger = logger;
    }

    public static RailDatabase open(File dataFolder, Logger logger) throws SQLException {
        if (!dataFolder.exists() && !dataFolder.mkdirs()) {
            throw new SQLException("データフォルダを作成できない: " + dataFolder);
        }
        File file = new File(dataFolder, "rail.db");
        Connection connection = DriverManager.getConnection("jdbc:sqlite:" + file.getAbsolutePath());
        RailDatabase db = new RailDatabase(connection, logger);
        db.createSchema();
        return db;
    }

    private void createSchema() throws SQLException {
        try (Statement st = connection.createStatement()) {
            // §4 の本来のテーブル
            st.execute("""
                    CREATE TABLE IF NOT EXISTS rail_data (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        world TEXT NOT NULL,
                        x INTEGER NOT NULL,
                        y INTEGER NOT NULL,
                        z INTEGER NOT NULL,
                        rail_type TEXT NOT NULL,
                        owner_nation TEXT NOT NULL,
                        is_outside_territory INTEGER NOT NULL,
                        created_at INTEGER NOT NULL
                    )""");
            st.execute("""
                    CREATE TABLE IF NOT EXISTS nation_monthly_data (
                        nation_id TEXT PRIMARY KEY,
                        placed_count_current_month INTEGER NOT NULL DEFAULT 0,
                        last_reset_date TEXT
                    )""");
            st.execute("""
                    CREATE TABLE IF NOT EXISTS station_data (
                        station_id INTEGER PRIMARY KEY AUTOINCREMENT,
                        owner_nation TEXT NOT NULL,
                        world TEXT NOT NULL,
                        min_x INTEGER NOT NULL, min_y INTEGER NOT NULL, min_z INTEGER NOT NULL,
                        max_x INTEGER NOT NULL, max_y INTEGER NOT NULL, max_z INTEGER NOT NULL,
                        is_active INTEGER NOT NULL DEFAULT 1
                    )""");
            // 鉄道専用の国家代用（本物の国家プラグインができたら置き換える）
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
            // 「自国領土」の代用。チャンク単位で国家を割り当てる（未登録＝領土外として扱う）
            st.execute("""
                    CREATE TABLE IF NOT EXISTS rail_claims (
                        world TEXT NOT NULL,
                        chunk_x INTEGER NOT NULL,
                        chunk_z INTEGER NOT NULL,
                        nation_id TEXT NOT NULL,
                        PRIMARY KEY (world, chunk_x, chunk_z)
                    )""");
        }
    }

    @Override
    public void close() {
        try {
            connection.close();
        } catch (SQLException e) {
            logger.warning("rail.db を閉じる際にエラー: " + e.getMessage());
        }
    }

    // ------------------------------------------------------------ 国家代用

    /** プレイヤーの所属国家。未登録なら空。 */
    public Optional<String> nationOfPlayer(UUID playerId) {
        String sql = "SELECT nation_id FROM rail_nation_players WHERE player_uuid = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, playerId.toString());
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? Optional.of(rs.getString(1)) : Optional.empty();
            }
        } catch (SQLException e) {
            throw new RailDatabaseException(e);
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
            throw new RailDatabaseException(e);
        }
    }

    /** 国家が無ければ空の国庫で作る（べき等）。 */
    public void ensureNation(String nationId) {
        String sql = "INSERT OR IGNORE INTO rail_nations(nation_id, treasury, reserve) VALUES(?, 0, 0)";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, nationId);
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new RailDatabaseException(e);
        }
        String sql2 = "INSERT OR IGNORE INTO nation_monthly_data(nation_id, placed_count_current_month) "
                + "VALUES(?, 0)";
        try (PreparedStatement ps = connection.prepareStatement(sql2)) {
            ps.setString(1, nationId);
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new RailDatabaseException(e);
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
            throw new RailDatabaseException(e);
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
            throw new RailDatabaseException(e);
        }
    }

    /** 納入（運営コマンド `/rail admin deposit` の代用。本来は国家プラグイン側の機能）。 */
    public void deposit(String nationId, long amount) {
        saveBalances(nationId, NationalAccounts.donate(balances(nationId), amount));
    }

    public void setSuzerain(String vassalNationId, String suzerainNationId) {
        ensureNation(vassalNationId);
        ensureNation(suzerainNationId);
        String sql = "UPDATE rail_nations SET suzerain_nation_id = ? WHERE nation_id = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, suzerainNationId);
            ps.setString(2, vassalNationId);
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new RailDatabaseException(e);
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
            throw new RailDatabaseException(e);
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
            throw new RailDatabaseException(e);
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
            throw new RailDatabaseException(e);
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
            throw new RailDatabaseException(e);
        }
    }

    // ------------------------------------------------------------ 領土の代用（チャンク単位）

    /** チャンクをある国家の領土として登録する（未登録＝領土外として扱う）。 */
    public void claimChunk(String world, int chunkX, int chunkZ, String nationId) {
        ensureNation(nationId);
        String sql = "INSERT INTO rail_claims(world, chunk_x, chunk_z, nation_id) VALUES(?, ?, ?, ?) "
                + "ON CONFLICT(world, chunk_x, chunk_z) DO UPDATE SET nation_id = excluded.nation_id";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, world);
            ps.setInt(2, chunkX);
            ps.setInt(3, chunkZ);
            ps.setString(4, nationId);
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new RailDatabaseException(e);
        }
    }

    public void unclaimChunk(String world, int chunkX, int chunkZ) {
        String sql = "DELETE FROM rail_claims WHERE world = ? AND chunk_x = ? AND chunk_z = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, world);
            ps.setInt(2, chunkX);
            ps.setInt(3, chunkZ);
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new RailDatabaseException(e);
        }
    }

    /** そのチャンクが自国（{@code nationId}）の領土<b>ではない</b>か（F-01/F-04 の領土外倍率）。 */
    public boolean isOutsideTerritory(String world, int chunkX, int chunkZ, String nationId) {
        String sql = "SELECT nation_id FROM rail_claims WHERE world = ? AND chunk_x = ? AND chunk_z = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, world);
            ps.setInt(2, chunkX);
            ps.setInt(3, chunkZ);
            try (ResultSet rs = ps.executeQuery()) {
                if (!rs.next()) {
                    // 未登録のチャンクは「誰の領土でもない」ので領土外として扱う
                    return true;
                }
                return !nationId.equals(rs.getString(1));
            }
        } catch (SQLException e) {
            throw new RailDatabaseException(e);
        }
    }

    // ------------------------------------------------------------ 当月の設置カウント（F-01/F-02）

    public int placedThisMonth(String nationId) {
        ensureNation(nationId);
        String sql = "SELECT placed_count_current_month FROM nation_monthly_data WHERE nation_id = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, nationId);
            try (ResultSet rs = ps.executeQuery()) {
                rs.next();
                return rs.getInt(1);
            }
        } catch (SQLException e) {
            throw new RailDatabaseException(e);
        }
    }

    public void incrementPlacedThisMonth(String nationId) {
        ensureNation(nationId);
        String sql = "UPDATE nation_monthly_data SET placed_count_current_month = "
                + "placed_count_current_month + 1 WHERE nation_id = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, nationId);
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new RailDatabaseException(e);
        }
    }

    /** {@code /rail admin reset <nation>} 用の1国だけのリセット。 */
    public void resetMonthlyCount(String nationId) {
        ensureNation(nationId);
        String sql = "UPDATE nation_monthly_data SET placed_count_current_month = 0 "
                + "WHERE nation_id = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, nationId);
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new RailDatabaseException(e);
        }
    }

    /** F-03「請求完了後、すべての国家の当月設置カウントを0にリセット」。 */
    public void resetAllMonthlyCounts(String isoDate) {
        String sql = "UPDATE nation_monthly_data SET placed_count_current_month = 0, last_reset_date = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, isoDate);
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new RailDatabaseException(e);
        }
    }

    // ------------------------------------------------------------ rail_data（F-01/F-03）

    public record RailRecord(long id, String world, int x, int y, int z, RailType type,
                             String ownerNation, boolean outsideTerritory) {
    }

    public void insertRail(String world, int x, int y, int z, RailType type, String ownerNation,
            boolean outsideTerritory) {
        String sql = "INSERT INTO rail_data(world, x, y, z, rail_type, owner_nation, "
                + "is_outside_territory, created_at) VALUES(?, ?, ?, ?, ?, ?, ?, ?)";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, world);
            ps.setInt(2, x);
            ps.setInt(3, y);
            ps.setInt(4, z);
            ps.setString(5, type.name());
            ps.setString(6, ownerNation);
            ps.setInt(7, outsideTerritory ? 1 : 0);
            ps.setLong(8, System.currentTimeMillis());
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new RailDatabaseException(e);
        }
    }

    public void deleteRail(String world, int x, int y, int z) {
        String sql = "DELETE FROM rail_data WHERE world = ? AND x = ? AND y = ? AND z = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, world);
            ps.setInt(2, x);
            ps.setInt(3, y);
            ps.setInt(4, z);
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new RailDatabaseException(e);
        }
    }

    /** 国家が設置しているレールすべて（F-03 の月額維持費の合計に使う）。 */
    public List<RailRecord> railsByNation(String nationId) {
        String sql = "SELECT id, world, x, y, z, rail_type, owner_nation, is_outside_territory "
                + "FROM rail_data WHERE owner_nation = ?";
        List<RailRecord> records = new ArrayList<>();
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, nationId);
            try (ResultSet rs = ps.executeQuery()) {
                while (rs.next()) {
                    records.add(new RailRecord(rs.getLong(1), rs.getString(2), rs.getInt(3),
                            rs.getInt(4), rs.getInt(5), RailType.valueOf(rs.getString(6)),
                            rs.getString(7), rs.getInt(8) != 0));
                }
            }
        } catch (SQLException e) {
            throw new RailDatabaseException(e);
        }
        return records;
    }

    /** 月末バッチ（F-03）が回すべき、レールを1本以上持つ国家のID一覧。 */
    public List<String> nationsWithRails() {
        String sql = "SELECT DISTINCT owner_nation FROM rail_data";
        List<String> nations = new ArrayList<>();
        try (Statement st = connection.createStatement(); ResultSet rs = st.executeQuery(sql)) {
            while (rs.next()) {
                nations.add(rs.getString(1));
            }
        } catch (SQLException e) {
            throw new RailDatabaseException(e);
        }
        return nations;
    }

    // ------------------------------------------------------------ station_data（F-04）

    public record StationRecord(long id, String ownerNation, String world,
                                int minX, int minY, int minZ, int maxX, int maxY, int maxZ) {

        public double distanceTo(String world, int x, int y, int z) {
            if (!this.world.equals(world)) {
                return Double.POSITIVE_INFINITY;
            }
            double cx = (minX + maxX) / 2.0;
            double cy = (minY + maxY) / 2.0;
            double cz = (minZ + maxZ) / 2.0;
            double dx = cx - x;
            double dy = cy - y;
            double dz = cz - z;
            return Math.sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    public void insertStation(String ownerNation, String world, int minX, int minY, int minZ,
            int maxX, int maxY, int maxZ) {
        String sql = "INSERT INTO station_data(owner_nation, world, min_x, min_y, min_z, "
                + "max_x, max_y, max_z, is_active) VALUES(?, ?, ?, ?, ?, ?, ?, ?, 1)";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, ownerNation);
            ps.setString(2, world);
            ps.setInt(3, minX);
            ps.setInt(4, minY);
            ps.setInt(5, minZ);
            ps.setInt(6, maxX);
            ps.setInt(7, maxY);
            ps.setInt(8, maxZ);
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new RailDatabaseException(e);
        }
    }

    /** そのブロック座標が、有効な認定駅舎のいずれかの範囲内にあるか（F-04 の Mob 湧き潰し）。 */
    public boolean isInsideAnyActiveStation(String world, int x, int y, int z) {
        String sql = "SELECT COUNT(*) FROM station_data WHERE is_active = 1 AND world = ? "
                + "AND ? BETWEEN min_x AND max_x AND ? BETWEEN min_y AND max_y "
                + "AND ? BETWEEN min_z AND max_z";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, world);
            ps.setInt(2, x);
            ps.setInt(3, y);
            ps.setInt(4, z);
            try (ResultSet rs = ps.executeQuery()) {
                rs.next();
                return rs.getInt(1) > 0;
            }
        } catch (SQLException e) {
            throw new RailDatabaseException(e);
        }
    }

    /** 最寄りの既存駅舎までの距離（F-04 の150ブロック配置制限）。既存駅舎が無ければ無限大。 */
    public double nearestActiveStationDistance(String world, int x, int y, int z) {
        String sql = "SELECT owner_nation, world, min_x, min_y, min_z, max_x, max_y, max_z "
                + "FROM station_data WHERE is_active = 1";
        double nearest = Double.POSITIVE_INFINITY;
        try (Statement st = connection.createStatement(); ResultSet rs = st.executeQuery(sql)) {
            while (rs.next()) {
                StationRecord record = new StationRecord(0, rs.getString(1), rs.getString(2),
                        rs.getInt(3), rs.getInt(4), rs.getInt(5), rs.getInt(6), rs.getInt(7),
                        rs.getInt(8));
                nearest = Math.min(nearest, record.distanceTo(world, x, y, z));
            }
        } catch (SQLException e) {
            throw new RailDatabaseException(e);
        }
        return nearest;
    }

    /** JDBC の検査例外を、呼び出し側の Bukkit イベント処理で扱いやすい非検査例外に包む。 */
    public static final class RailDatabaseException extends RuntimeException {
        RailDatabaseException(SQLException cause) {
            super("rail.db の操作に失敗した", cause);
        }
    }
}
