package jp.mcserver.core.racing;

import java.time.DayOfWeek;

/**
 * 年間レースカレンダー（`minecraft_server_spec.md` §27.8.3）上の1レース。
 *
 * @param name レース名（開催競馬場のバイオーム名を冠する、§27.8.2）
 * @param grade 格付け
 * @param course 開催競馬場
 * @param distanceMeters 芝の距離（メートル）
 * @param split 開催スプリット（1〜4、§27.9）
 * @param day 開催曜日（主要レースは土日のいずれか）
 * @param seriesName 所属シリーズ名（大地三冠・花冠三冠・新緑三冠・黄金三冠・
 *     スプリントシリーズ・マイルシリーズ・中距離シリーズ・ステイヤーズシリーズ）
 * @param classicRace 3歳馬クラシック路線（大地三冠・花冠三冠）に属し、出走が
 *     スプリット3・4に限られるか（§27.9）
 */
public record ScheduledRace(
        String name,
        Grade grade,
        RacingCourse course,
        int distanceMeters,
        int split,
        DayOfWeek day,
        String seriesName,
        boolean classicRace) {

    public ScheduledRace {
        if (!course.hostsTurfDistance(distanceMeters)) {
            throw new IllegalArgumentException(
                    course.displayName() + "は芝" + distanceMeters + "mを収録していない");
        }
        if (split < 1 || split > 4) {
            throw new IllegalArgumentException("スプリットが範囲外である: " + split);
        }
        if (day != DayOfWeek.SATURDAY && day != DayOfWeek.SUNDAY) {
            throw new IllegalArgumentException("主要レースは土日のいずれかで開催する: " + day);
        }
    }
}
