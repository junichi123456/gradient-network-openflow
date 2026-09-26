package jp.mcserver.core.racing;

import java.util.List;

/**
 * 三冠・距離別シリーズの制覇ボーナス（`minecraft_server_spec.md` §27.8.5）。
 *
 * <p>三冠（大地・花冠・新緑・黄金）は3レース全勝で一律{@value #TRIPLE_CROWN_BONUS_EXP} exp、
 * 距離別シリーズ（スプリント・マイル・中距離・ステイヤーズ）はシリーズ内の合計得点
 * （{@link #pointsForFinish}）最上位が、そのシリーズの最高額レースと同額のボーナス
 * （{@link #distanceSeriesChampionBonusExp}）を、それぞれ獲得する。ボーナスはいずれも
 * 馬の累積賞金（§27.5の重賞昇級判定にもカウントされる）と、馬主の属する国の国庫
 * （§7、§27.11の調教・維持コストの回収先）に7:3で分割する。
 */
public final class SeriesConquestBonus {

    private SeriesConquestBonus() {}

    /** 三冠（3レース全勝）達成ボーナス（exp、§27.8.5）。4シリーズ共通の一律額。 */
    public static final long TRIPLE_CROWN_BONUS_EXP = 100_000;

    private static final long INDIVIDUAL_SHARE_NUMERATOR = 7;
    private static final long SHARE_DENOMINATOR = 10;

    /** ボーナスのうち、馬の累積賞金（個体）に計上する取り分（exp、端数切り捨て）。 */
    public static long individualShareExp(long bonusExp) {
        if (bonusExp < 0) {
            throw new IllegalArgumentException("ボーナス額が負である: " + bonusExp);
        }
        return bonusExp * INDIVIDUAL_SHARE_NUMERATOR / SHARE_DENOMINATOR;
    }

    /**
     * ボーナスのうち、馬主の属する国の国庫に計上する取り分（exp）。個体取り分の
     * 残余として求めるため、{@link #individualShareExp}との合計が必ずbonusExpと一致する
     * （端数を国庫側に寄せ、損失を出さない）。
     */
    public static long nationalShareExp(long bonusExp) {
        if (bonusExp < 0) {
            throw new IllegalArgumentException("ボーナス額が負である: " + bonusExp);
        }
        return bonusExp - individualShareExp(bonusExp);
    }

    /**
     * 距離別シリーズ（スプリント・マイル・中距離・ステイヤーズ）の制覇ボーナス（exp）。
     * そのシリーズに含まれるレースの1着賞金のうち最高額とする（§27.8.5）——新たな
     * 金額を設定せず、既存の1着賞金表（{@link RaceCalendar}）からそのまま導く。
     *
     * @throws IllegalArgumentException 該当するシリーズが存在しない、または三冠
     *     （全レースG1）のように全勝方式のシリーズを渡した場合
     */
    public static long distanceSeriesChampionBonusExp(String seriesName) {
        List<ScheduledRace> races = RaceCalendar.racesInSeries(seriesName);
        if (races.isEmpty()) {
            throw new IllegalArgumentException("該当するシリーズが見つからない: " + seriesName);
        }
        if (races.stream().anyMatch(race -> race.grade() == Grade.G1)) {
            throw new IllegalArgumentException("三冠シリーズは全勝方式であり、対象外である: " + seriesName);
        }
        return races.stream().mapToLong(ScheduledRace::prizeMoneyExp).max().orElseThrow();
    }

    /** 距離別シリーズの合計得点方式（§27.8.5）における、着順ごとの得点。5着以下は0点。 */
    public static int pointsForFinish(int finishPosition) {
        if (finishPosition < 1) {
            throw new IllegalArgumentException("着順は1以上である必要がある: " + finishPosition);
        }
        return switch (finishPosition) {
            case 1 -> 10;
            case 2 -> 5;
            case 3 -> 3;
            case 4 -> 1;
            default -> 0;
        };
    }
}
