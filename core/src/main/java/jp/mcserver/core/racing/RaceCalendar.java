package jp.mcserver.core.racing;

import java.time.DayOfWeek;
import java.util.List;

/**
 * 年間レースカレンダー（`minecraft_server_spec.md` §27.8.3）。
 *
 * <p>1シーズン＝4週間（4スプリット、§27.9）の中に、3冠4種（大地三冠・花冠三冠・
 * 新緑三冠・黄金三冠）と距離別4種（スプリント・マイル・中距離・ステイヤーズ、
 * §27.8.2）、計{@value #TOTAL_RACES}レースを収める。主要レースは土日のいずれかに
 * 開催し、1日の開催数は{@value #MAX_RACES_PER_DAY}が上限。
 */
public final class RaceCalendar {

    private RaceCalendar() {}

    /** 1日に開催できるレース数の上限（§27.8.3）。 */
    public static final int MAX_RACES_PER_DAY = 5;

    /** シーズン内の総レース数（3冠4種・距離別4種の合計、§27.8.3）。 */
    public static final int TOTAL_RACES = 31;

    private static final String DAICHI = "大地三冠";
    private static final String KAKAN = "花冠三冠";
    private static final String SHINRYOKU = "新緑三冠";
    private static final String OGON = "黄金三冠";
    private static final String SPRINT = "スプリントシリーズ";
    private static final String MILE = "マイルシリーズ";
    private static final String MIDDLE = "中距離シリーズ";
    private static final String STAYERS = "ステイヤーズシリーズ";

    private static final long G3_PRIZE = RaceClass.G3.flatPrizeMoneyExp();
    private static final long G2_PRIZE = RaceClass.G2.flatPrizeMoneyExp();

    /** 年間レースカレンダーの全レース（§27.8.3の表そのもの）。1着賞金は§27.5・§27.8.2を参照。 */
    public static final List<ScheduledRace> SEASON_RACES = List.of(
            // スプリット1・土
            new ScheduledRace("流氷記念", Grade.G3, RacingCourse.RYUHYO, 2000,
                    1, DayOfWeek.SATURDAY, MIDDLE, false, G3_PRIZE),
            new ScheduledRace("渓谷マイルS", Grade.G3, RacingCourse.KEIKOKU, 1600,
                    1, DayOfWeek.SATURDAY, MILE, false, G3_PRIZE),
            new ScheduledRace("流氷スプリントS", Grade.G3, RacingCourse.RYUHYO, 1200,
                    1, DayOfWeek.SATURDAY, SPRINT, false, G3_PRIZE),

            // スプリット1・日
            new ScheduledRace("高原記念", Grade.G3, RacingCourse.KOGEN, 2000,
                    1, DayOfWeek.SUNDAY, MIDDLE, false, G3_PRIZE),
            new ScheduledRace("湿地マイルS", Grade.G3, RacingCourse.SHITCHI, 1600,
                    1, DayOfWeek.SUNDAY, MILE, false, G3_PRIZE),
            new ScheduledRace("火山スプリントS", Grade.G3, RacingCourse.KAZAN, 1200,
                    1, DayOfWeek.SUNDAY, SPRINT, false, G3_PRIZE),
            new ScheduledRace("渓谷スプリントS", Grade.G3, RacingCourse.KEIKOKU, 1200,
                    1, DayOfWeek.SUNDAY, SPRINT, false, G3_PRIZE),

            // スプリット2・土
            new ScheduledRace("渓谷大阪杯", Grade.G1, RacingCourse.KEIKOKU, 2000,
                    2, DayOfWeek.SATURDAY, SHINRYOKU, false, 24_000),
            new ScheduledRace("丘陵ステイヤーズS", Grade.G2, RacingCourse.KYURYO, 3600,
                    2, DayOfWeek.SATURDAY, STAYERS, false, G2_PRIZE),
            new ScheduledRace("湿地スプリントS", Grade.G3, RacingCourse.SHITCHI, 1000,
                    2, DayOfWeek.SATURDAY, SPRINT, false, G3_PRIZE),
            new ScheduledRace("樹海マイルS", Grade.G3, RacingCourse.JUKAI, 1600,
                    2, DayOfWeek.SATURDAY, MILE, false, G3_PRIZE),

            // スプリット2・日
            new ScheduledRace("竹林天皇賞・春", Grade.G1, RacingCourse.CHIKURIN, 3200,
                    2, DayOfWeek.SUNDAY, SHINRYOKU, false, 24_000),
            new ScheduledRace("平原ステイヤーズS", Grade.G2, RacingCourse.HEIGEN, 3400,
                    2, DayOfWeek.SUNDAY, STAYERS, false, G2_PRIZE),
            new ScheduledRace("樹海スプリントS", Grade.G3, RacingCourse.JUKAI, 1200,
                    2, DayOfWeek.SUNDAY, SPRINT, false, G3_PRIZE),

            // スプリット3・土
            new ScheduledRace("丘陵皐月賞", Grade.G1, RacingCourse.KYURYO, 2000,
                    3, DayOfWeek.SATURDAY, DAICHI, true, 16_000),
            new ScheduledRace("渓谷桜花賞", Grade.G1, RacingCourse.KEIKOKU, 1600,
                    3, DayOfWeek.SATURDAY, KAKAN, true, 11_500),
            new ScheduledRace("火山記念", Grade.G3, RacingCourse.KAZAN, 2000,
                    3, DayOfWeek.SATURDAY, MIDDLE, false, G3_PRIZE),

            // スプリット3・日
            new ScheduledRace("渓谷宝塚記念", Grade.G1, RacingCourse.KEIKOKU, 2200,
                    3, DayOfWeek.SUNDAY, SHINRYOKU, false, 24_000),
            new ScheduledRace("竹林ステイヤーズS", Grade.G2, RacingCourse.CHIKURIN, 3200,
                    3, DayOfWeek.SUNDAY, STAYERS, false, G2_PRIZE),
            new ScheduledRace("タイガスプリントS", Grade.G2, RacingCourse.TAIGA, 1200,
                    3, DayOfWeek.SUNDAY, SPRINT, false, G2_PRIZE),
            new ScheduledRace("タイガ記念", Grade.G2, RacingCourse.TAIGA, 2000,
                    3, DayOfWeek.SUNDAY, MIDDLE, false, G2_PRIZE),

            // スプリット4・土
            new ScheduledRace("平原優駿", Grade.G1, RacingCourse.HEIGEN, 2400,
                    4, DayOfWeek.SATURDAY, DAICHI, true, 24_000),
            new ScheduledRace("平原優駿牝馬", Grade.G1, RacingCourse.HEIGEN, 2400,
                    4, DayOfWeek.SATURDAY, KAKAN, true, 12_000),
            new ScheduledRace("平原天皇賞・秋", Grade.G1, RacingCourse.HEIGEN, 2000,
                    4, DayOfWeek.SATURDAY, OGON, false, 24_000),
            new ScheduledRace("湿地記念", Grade.G3, RacingCourse.SHITCHI, 2000,
                    4, DayOfWeek.SATURDAY, MIDDLE, false, G3_PRIZE),
            new ScheduledRace("丘陵マイルS", Grade.G3, RacingCourse.KYURYO, 1600,
                    4, DayOfWeek.SATURDAY, MILE, false, G3_PRIZE),

            // スプリット4・日
            new ScheduledRace("竹林菊花賞", Grade.G1, RacingCourse.CHIKURIN, 3000,
                    4, DayOfWeek.SUNDAY, DAICHI, true, 16_000),
            new ScheduledRace("竹林秋華賞", Grade.G1, RacingCourse.CHIKURIN, 2000,
                    4, DayOfWeek.SUNDAY, KAKAN, true, 9_000),
            new ScheduledRace("平原国際杯", Grade.G1, RacingCourse.HEIGEN, 2400,
                    4, DayOfWeek.SUNDAY, OGON, false, 40_000),
            new ScheduledRace("丘陵記念", Grade.G1, RacingCourse.KYURYO, 2500,
                    4, DayOfWeek.SUNDAY, OGON, false, 40_000),
            new ScheduledRace("渓谷ステイヤーズS", Grade.G2, RacingCourse.KEIKOKU, 3000,
                    4, DayOfWeek.SUNDAY, STAYERS, false, G2_PRIZE));

    /** 指定のスプリット・曜日に開催されるレース。 */
    public static List<ScheduledRace> racesOn(int split, DayOfWeek day) {
        return SEASON_RACES.stream()
                .filter(race -> race.split() == split && race.day() == day)
                .toList();
    }

    /** 指定のシリーズ名に属するレース。 */
    public static List<ScheduledRace> racesInSeries(String seriesName) {
        return SEASON_RACES.stream()
                .filter(race -> race.seriesName().equals(seriesName))
                .toList();
    }
}
