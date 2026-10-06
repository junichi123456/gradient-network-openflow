package jp.mcserver.plugin.racing;

import java.io.File;
import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.EnumMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.UUID;
import java.util.logging.Logger;
import jp.mcserver.core.racing.AbilityStat;
import jp.mcserver.core.racing.AbilityValue;
import jp.mcserver.core.racing.TrainingMenu;
import jp.mcserver.core.racing.TrainingOutcome;
import jp.mcserver.plugin.nation.NationLedger;

/**
 * 競馬専用ワールドの競走馬の永続化（`minecraft_server_spec.md` §27）。
 *
 * <p><b>実機での最初の一歩として、馬の能力値（§27.3）と1日1回の調教（§27.3）だけを
 * 対象にする。</b>血統・配合（§27.2）、レース展開・実際のレース進行（§27.4）、通常・
 * 上位特性の獲得（§27.7.2）、シーズン・年齢（§27.9）、国別登録（§27.10）は、次段階以降
 * で順次plugin化する（{@code RacingDatabase}自体は`core`の式をそのまま呼ぶだけで、
 * これらを妨げない設計にしてある）。
 *
 * <p>国庫（§7・§27.11）は{@link NationLedger}（{@code nation.db}）へ委譲する——
 * {@code RailDatabase}と同じ形。
 */
public final class RacingDatabase implements AutoCloseable {

    private final Connection connection;
    private final NationLedger ledger;
    private final Logger logger;

    private RacingDatabase(Connection connection, NationLedger ledger, Logger logger) {
        this.connection = connection;
        this.ledger = ledger;
        this.logger = logger;
    }

    public static RacingDatabase open(File dataFolder, NationLedger ledger, Logger logger) throws SQLException {
        if (!dataFolder.exists() && !dataFolder.mkdirs()) {
            throw new SQLException("データフォルダを作成できない: " + dataFolder);
        }
        File file = new File(dataFolder, "racing.db");
        Connection connection = DriverManager.getConnection("jdbc:sqlite:" + file.getAbsolutePath());
        RacingDatabase db = new RacingDatabase(connection, ledger, logger);
        db.createSchema();
        return db;
    }

    private void createSchema() throws SQLException {
        try (Statement st = connection.createStatement()) {
            st.execute("""
                    CREATE TABLE IF NOT EXISTS racing_horses (
                        horse_id INTEGER PRIMARY KEY AUTOINCREMENT,
                        owner_uuid TEXT NOT NULL,
                        nation_id TEXT NOT NULL,
                        name TEXT NOT NULL,
                        speed INTEGER NOT NULL,
                        stamina INTEGER NOT NULL,
                        power INTEGER NOT NULL,
                        guts INTEGER NOT NULL,
                        wisdom INTEGER NOT NULL,
                        birth_speed INTEGER NOT NULL,
                        birth_stamina INTEGER NOT NULL,
                        birth_power INTEGER NOT NULL,
                        birth_guts INTEGER NOT NULL,
                        birth_wisdom INTEGER NOT NULL,
                        fatigue INTEGER NOT NULL DEFAULT 0,
                        trust_level INTEGER NOT NULL DEFAULT 0,
                        last_trained_day INTEGER,
                        entity_uuid TEXT
                    )""");
        }
        addEntityUuidColumnIfMissing();
    }

    /** {@code /horse spawn}以前に作られた racing.db には{@code entity_uuid}列が無いため足す。 */
    private void addEntityUuidColumnIfMissing() throws SQLException {
        boolean present = false;
        try (Statement st = connection.createStatement();
                ResultSet rs = st.executeQuery("PRAGMA table_info(racing_horses)")) {
            while (rs.next()) {
                if ("entity_uuid".equals(rs.getString("name"))) {
                    present = true;
                }
            }
        }
        if (!present) {
            try (Statement st = connection.createStatement()) {
                st.execute("ALTER TABLE racing_horses ADD COLUMN entity_uuid TEXT");
            }
        }
    }

    @Override
    public void close() {
        try {
            connection.close();
        } catch (SQLException e) {
            logger.warning("racing.db を閉じる際にエラー: " + e.getMessage());
        }
    }

    // ------------------------------------------------------------ 国庫（NationLedgerへ委譲）

    public Optional<String> nationOfPlayer(UUID playerId) {
        return ledger.nationOfPlayer(playerId);
    }

    public long treasuryOf(String nationId) {
        return ledger.balances(nationId).treasury();
    }

    // ------------------------------------------------------------ 競走馬

    /**
     * 血統に基づく生成（§27.2）はまだplugin化していないため、出生時の能力値は
     * 呼び出し側が用意する（{@link AbilityValue#birthValueValid}を満たす必要がある）。
     * 現在値は出生時の値から始める。
     */
    public int createHorse(UUID owner, String nationId, String name,
            Map<AbilityStat, Integer> birthValues) {
        for (AbilityStat stat : AbilityStat.values()) {
            Integer value = birthValues.get(stat);
            if (value == null || !AbilityValue.birthValueValid(value)) {
                throw new IllegalArgumentException(stat + "の出生時能力値が不正である: " + value);
            }
        }
        ledger.ensureNation(nationId);
        String sql = """
                INSERT INTO racing_horses(
                    owner_uuid, nation_id, name,
                    speed, stamina, power, guts, wisdom,
                    birth_speed, birth_stamina, birth_power, birth_guts, birth_wisdom,
                    fatigue, trust_level, last_trained_day)
                VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, 0, NULL)""";
        try (PreparedStatement ps = connection.prepareStatement(sql, Statement.RETURN_GENERATED_KEYS)) {
            ps.setString(1, owner.toString());
            ps.setString(2, nationId);
            ps.setString(3, name);
            int speed = birthValues.get(AbilityStat.SPEED);
            int stamina = birthValues.get(AbilityStat.STAMINA);
            int power = birthValues.get(AbilityStat.POWER);
            int guts = birthValues.get(AbilityStat.GUTS);
            int wisdom = birthValues.get(AbilityStat.WISDOM);
            ps.setInt(4, speed);
            ps.setInt(5, stamina);
            ps.setInt(6, power);
            ps.setInt(7, guts);
            ps.setInt(8, wisdom);
            ps.setInt(9, speed);
            ps.setInt(10, stamina);
            ps.setInt(11, power);
            ps.setInt(12, guts);
            ps.setInt(13, wisdom);
            ps.executeUpdate();
            try (ResultSet keys = ps.getGeneratedKeys()) {
                keys.next();
                return keys.getInt(1);
            }
        } catch (SQLException e) {
            throw new RacingDatabaseException(e);
        }
    }

    public Optional<HorseRecord> horse(int horseId) {
        String sql = "SELECT * FROM racing_horses WHERE horse_id = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setInt(1, horseId);
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? Optional.of(readHorse(rs)) : Optional.empty();
            }
        } catch (SQLException e) {
            throw new RacingDatabaseException(e);
        }
    }

    public List<HorseRecord> horsesOwnedBy(UUID owner) {
        List<HorseRecord> result = new ArrayList<>();
        String sql = "SELECT * FROM racing_horses WHERE owner_uuid = ? ORDER BY horse_id";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, owner.toString());
            try (ResultSet rs = ps.executeQuery()) {
                while (rs.next()) {
                    result.add(readHorse(rs));
                }
            }
        } catch (SQLException e) {
            throw new RacingDatabaseException(e);
        }
        return result;
    }

    /** 国が保有する競走馬の頭数（§27.11の維持費の頭数逓増に使う）。 */
    public int horseCountOfNation(String nationId) {
        String sql = "SELECT COUNT(*) FROM racing_horses WHERE nation_id = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, nationId);
            try (ResultSet rs = ps.executeQuery()) {
                rs.next();
                return rs.getInt(1);
            }
        } catch (SQLException e) {
            throw new RacingDatabaseException(e);
        }
    }

    /**
     * 1日1回の調教（§27.3）を適用する。呼び出し側が当日分の調教コスト
     * （{@link TrainingMenu#costExp}）を国庫から支払えることと、まだ本日調教して
     * いないこと（{@link HorseRecord#lastTrainedDay}）を先に確認しておく想定。
     *
     * <p>成長は{@link AbilityValue#MAX_TRAINING_GROWTH}（生涯合計）を超えない範囲でしか
     * 反映されない——上限に達したステータスは、それ以上調教で伸びなくなる。
     *
     * @return 実際に反映された能力値ごとの増分（上限超過分は切り捨てられ、0のこともある）
     */
    public Map<AbilityStat, Integer> train(int horseId, TrainingMenu menu, TrainingOutcome outcome, int trainingDay) {
        HorseRecord horse = horse(horseId)
                .orElseThrow(() -> new IllegalArgumentException("馬が見つからない: " + horseId));
        Map<AbilityStat, Integer> applied = new EnumMap<>(AbilityStat.class);
        Map<AbilityStat, Integer> current = horse.currentValues();
        Map<AbilityStat, Integer> birth = horse.birthValues();
        for (AbilityStat stat : menu.targetStats()) {
            int used = current.get(stat) - birth.get(stat);
            int remaining = Math.max(0, AbilityValue.MAX_TRAINING_GROWTH - used);
            int gain = Math.min(outcome.statGain(), remaining);
            applied.put(stat, gain);
            current.put(stat, current.get(stat) + gain);
        }
        int fatigueAfter = horse.fatigue() + outcome.fatigueGain();
        saveTrainingResult(horseId, current, fatigueAfter, trainingDay);
        return applied;
    }

    /** 休養（§27.3）。能力値は変えず、疲労度を0まで回復する（回復量の詳細は§22で調整）。 */
    public void rest(int horseId, int trainingDay) {
        String sql = "UPDATE racing_horses SET fatigue = 0, last_trained_day = ? WHERE horse_id = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setInt(1, trainingDay);
            ps.setInt(2, horseId);
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new RacingDatabaseException(e);
        }
    }

    /** この記録に対応する、ゲーム内に出している馬（{@code /horse spawn}）を記録する。 */
    public void setEntityUuid(int horseId, UUID entityUuid) {
        String sql = "UPDATE racing_horses SET entity_uuid = ? WHERE horse_id = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setString(1, entityUuid.toString());
            ps.setInt(2, horseId);
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new RacingDatabaseException(e);
        }
    }

    /**
     * 本日の調教済みを解除する（検証用の{@code /horse admin resetday}）。
     *
     * @return 該当する馬がいたか
     */
    public boolean clearLastTrainedDay(int horseId) {
        String sql = "UPDATE racing_horses SET last_trained_day = NULL WHERE horse_id = ?";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setInt(1, horseId);
            return ps.executeUpdate() > 0;
        } catch (SQLException e) {
            throw new RacingDatabaseException(e);
        }
    }

    private void saveTrainingResult(int horseId, Map<AbilityStat, Integer> current, int fatigueAfter,
            int trainingDay) {
        String sql = """
                UPDATE racing_horses
                SET speed = ?, stamina = ?, power = ?, guts = ?, wisdom = ?,
                    fatigue = ?, last_trained_day = ?
                WHERE horse_id = ?""";
        try (PreparedStatement ps = connection.prepareStatement(sql)) {
            ps.setInt(1, current.get(AbilityStat.SPEED));
            ps.setInt(2, current.get(AbilityStat.STAMINA));
            ps.setInt(3, current.get(AbilityStat.POWER));
            ps.setInt(4, current.get(AbilityStat.GUTS));
            ps.setInt(5, current.get(AbilityStat.WISDOM));
            ps.setInt(6, fatigueAfter);
            ps.setInt(7, trainingDay);
            ps.setInt(8, horseId);
            ps.executeUpdate();
        } catch (SQLException e) {
            throw new RacingDatabaseException(e);
        }
    }

    private HorseRecord readHorse(ResultSet rs) throws SQLException {
        Map<AbilityStat, Integer> current = new EnumMap<>(AbilityStat.class);
        current.put(AbilityStat.SPEED, rs.getInt("speed"));
        current.put(AbilityStat.STAMINA, rs.getInt("stamina"));
        current.put(AbilityStat.POWER, rs.getInt("power"));
        current.put(AbilityStat.GUTS, rs.getInt("guts"));
        current.put(AbilityStat.WISDOM, rs.getInt("wisdom"));
        Map<AbilityStat, Integer> birth = new EnumMap<>(AbilityStat.class);
        birth.put(AbilityStat.SPEED, rs.getInt("birth_speed"));
        birth.put(AbilityStat.STAMINA, rs.getInt("birth_stamina"));
        birth.put(AbilityStat.POWER, rs.getInt("birth_power"));
        birth.put(AbilityStat.GUTS, rs.getInt("birth_guts"));
        birth.put(AbilityStat.WISDOM, rs.getInt("birth_wisdom"));
        int lastTrainedDay = rs.getInt("last_trained_day");
        // wasNull() は直前に読んだ列の結果しか反映しない。他の列を読む前に確定させる
        Optional<Integer> lastTrainedDayOpt = rs.wasNull() ? Optional.empty() : Optional.of(lastTrainedDay);
        String entityUuid = rs.getString("entity_uuid");
        return new HorseRecord(
                rs.getInt("horse_id"),
                UUID.fromString(rs.getString("owner_uuid")),
                rs.getString("nation_id"),
                rs.getString("name"),
                current,
                birth,
                rs.getInt("fatigue"),
                rs.getInt("trust_level"),
                lastTrainedDayOpt,
                entityUuid == null ? Optional.empty() : Optional.of(UUID.fromString(entityUuid)));
    }

    /** 競走馬1頭の永続化された状態。 */
    public record HorseRecord(
            int horseId,
            UUID owner,
            String nationId,
            String name,
            Map<AbilityStat, Integer> currentValues,
            Map<AbilityStat, Integer> birthValues,
            int fatigue,
            int trustLevel,
            Optional<Integer> lastTrainedDay,
            Optional<UUID> entityUuid) {

        /** 本日（{@code trainingDay}）すでに調教済みか。 */
        public boolean trainedOn(int trainingDay) {
            return lastTrainedDay.isPresent() && lastTrainedDay.get() == trainingDay;
        }
    }

    public static final class RacingDatabaseException extends RuntimeException {
        RacingDatabaseException(SQLException cause) {
            super("racing.db の操作に失敗した", cause);
        }
    }
}
