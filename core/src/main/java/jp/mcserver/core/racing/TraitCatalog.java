package jp.mcserver.core.racing;

import static jp.mcserver.core.racing.AbilityBonusSize.EXTRA_LARGE;
import static jp.mcserver.core.racing.AbilityBonusSize.LARGE;
import static jp.mcserver.core.racing.AbilityBonusSize.MEDIUM;
import static jp.mcserver.core.racing.AbilityBonusSize.SMALL;
import static jp.mcserver.core.racing.TraitCategory.DISTANCE;
import static jp.mcserver.core.racing.TraitCategory.DISTANCE_APTITUDE;
import static jp.mcserver.core.racing.TraitCategory.HILL;
import static jp.mcserver.core.racing.TraitCategory.LAYOFF;
import static jp.mcserver.core.racing.TraitCategory.NONE;
import static jp.mcserver.core.racing.TraitCategory.RACE_GRADE;
import static jp.mcserver.core.racing.TraitCategory.RUNNING_STYLE;
import static jp.mcserver.core.racing.TraitCategory.SEASON;
import static jp.mcserver.core.racing.TraitTier.NORMAL;
import static jp.mcserver.core.racing.TraitTier.UPPER;

import java.util.List;
import java.util.Optional;

/**
 * 通常特性・上位特性のカタログ（`minecraft_server_spec.md` §27.7.2）。
 *
 * <p>原案（104件）のうち、このワールドに存在しない条件——海外遠征（§1で
 * Java Edition限定・中央10場のみと決めた本ワールドに海外レースは無い）、
 * 香港・ドバイ・パリロンシャン・イギリスアスコットの競馬場別特性（同じ理由）
 * ——にあたる{@value #EXCLUDED_COUNT}件を除いた{@value #ENTRY_COUNT}件を収録する。
 * 競馬場別特性は、該当する4件（東京・中山・京都・阪神）のみ§27.8.1のバイオーム名
 * （平原・丘陵・竹林・渓谷）に改名して残した。
 */
public final class TraitCatalog {

    private TraitCatalog() {}

    /** 原案から除外した特性の件数（海外遠征2件＋海外競馬場別特性8件）。 */
    public static final int EXCLUDED_COUNT = 10;

    /** 収録した特性の件数。 */
    public static final int ENTRY_COUNT = 94;

    public static final List<TraitDefinition> ALL = List.of(
            new TraitDefinition(1, "大舞台", NORMAL, RACE_GRADE,
                    "GIレース", "能力プラス補正(大)", LARGE, null),
            new TraitDefinition(2, "晴れ舞台", UPPER, RACE_GRADE,
                    "GIレース", "能力プラス補正(特大)", EXTRA_LARGE, 1),
            new TraitDefinition(3, "GⅡ大将", NORMAL, RACE_GRADE,
                    "GⅡレース", "能力プラス補正(大)", LARGE, null),
            new TraitDefinition(4, "GⅡの守護者", UPPER, RACE_GRADE,
                    "GⅡレース", "能力プラス補正(特大)", EXTRA_LARGE, 3),
            new TraitDefinition(5, "交流重賞巧者", NORMAL, RACE_GRADE,
                    "地方の交流重賞", "能力プラス補正(中)", MEDIUM, null),
            new TraitDefinition(6, "交流重賞ハンター", UPPER, RACE_GRADE,
                    "地方の交流重賞", "能力プラス補正(大)", LARGE, 5),
            new TraitDefinition(7, "ローカル", NORMAL, RACE_GRADE,
                    "ローカル開催", "能力プラス補正(中)", MEDIUM, null),
            new TraitDefinition(8, "全国行脚", UPPER, RACE_GRADE,
                    "ローカル開催", "能力プラス補正(大)", LARGE, 7),

            new TraitDefinition(11, "根幹距離", NORMAL, DISTANCE,
                    "1200/1600/2000/2400/3000m", "ランダムで能力プラス補正(中)", MEDIUM, null),
            new TraitDefinition(12, "根幹の鬼", UPPER, DISTANCE,
                    "1200/1600/2000/2400/3000m", "ランダムで能力プラス補正(大)", LARGE, 11),
            new TraitDefinition(13, "非根幹距離", NORMAL, DISTANCE,
                    "1200/1600/2000/2400/3000m以外", "ランダムで能力プラス補正(中)", MEDIUM, null),
            new TraitDefinition(14, "非根幹の鬼", UPPER, DISTANCE,
                    "1200/1600/2000/2400/3000m以外", "ランダムで能力プラス補正(大)", LARGE, 13),
            new TraitDefinition(15, "超長距離", NORMAL, DISTANCE,
                    "3000m以上のレース", "能力プラス補正(中)", MEDIUM, null),
            new TraitDefinition(16, "長距離砲", UPPER, DISTANCE,
                    "3000m以上のレース", "能力プラス補正(大)", LARGE, 15),

            new TraitDefinition(17, "鉄砲", NORMAL, LAYOFF,
                    "休み明け", "能力プラス補正(小)", SMALL, null),
            new TraitDefinition(18, "狙い撃ち", UPPER, LAYOFF,
                    "休み明け", "能力プラス補正(中)", MEDIUM, 17),
            new TraitDefinition(19, "叩き良化", NORMAL, LAYOFF,
                    "休み明け初戦、2戦目の後", "ランダムで調子上昇", null, null),
            new TraitDefinition(20, "右肩上がり", UPPER, LAYOFF,
                    "休み明け初戦、2戦目の後", "ランダムで調子上昇（叩き良化より上昇しやすい）", null, 19),

            new TraitDefinition(21, "乾坤一擲", NORMAL, RACE_GRADE,
                    "GIレース", "ランダムで能力プラス補正(大)", LARGE, null),
            new TraitDefinition(22, "渾身の激走", UPPER, RACE_GRADE,
                    "GIレース", "ランダムで能力プラス補正(特大)", EXTRA_LARGE, 21),

            new TraitDefinition(23, "大駆け", NORMAL, NONE,
                    "4番人気以降", "ランダムで能力プラス補正(大)", LARGE, null),
            new TraitDefinition(24, "大番狂わせ", UPPER, NONE,
                    "4番人気以降", "ランダムで能力プラス補正(特大)", EXTRA_LARGE, 23),

            new TraitDefinition(25, "タフネス", NORMAL, NONE,
                    "レース後", "疲労軽減(小)/育成度上昇(小)", null, null),
            new TraitDefinition(26, "超回復", UPPER, NONE,
                    "レース後", "疲労軽減(中)/育成度上昇(中)", null, 25),

            new TraitDefinition(27, "牡馬混合", NORMAL, RACE_GRADE,
                    "牝馬のみ牡牝混合GIレース", "能力プラス補正(中)", MEDIUM, null),
            new TraitDefinition(28, "女王君臨", UPPER, RACE_GRADE,
                    "牝馬のみ牡牝混合GIレース", "能力プラス補正(大)", LARGE, 27),
            new TraitDefinition(29, "軽ハンデ", NORMAL, RACE_GRADE,
                    "ハンデ戦で軽ハンデ", "能力プラス補正(大)", LARGE, null),
            new TraitDefinition(30, "魅惑の軽斤量", UPPER, RACE_GRADE,
                    "ハンデ戦で軽ハンデ", "能力プラス補正(特大)", EXTRA_LARGE, 29),

            new TraitDefinition(31, "スタート", NORMAL, RUNNING_STYLE,
                    "レーススタート時", "スタートが得意になる", null, null),
            new TraitDefinition(32, "ロケットスタート", UPPER, RUNNING_STYLE,
                    "レーススタート時", "スタートがさらに得意になる", null, 31),
            new TraitDefinition(33, "高速逃げ", NORMAL, RUNNING_STYLE,
                    "大逃げ、逃げ", "能力プラス補正(中)", MEDIUM, null),
            new TraitDefinition(34, "神速逃げ", UPPER, RUNNING_STYLE,
                    "大逃げ、逃げ", "能力プラス補正(大)", LARGE, 33),
            new TraitDefinition(35, "二の脚", NORMAL, RUNNING_STYLE,
                    "先行", "能力プラス補正(中)", MEDIUM, null),
            new TraitDefinition(36, "横綱相撲", UPPER, RUNNING_STYLE,
                    "先行", "能力プラス補正(大)", LARGE, 35),
            new TraitDefinition(37, "直一気", NORMAL, RUNNING_STYLE,
                    "差し、追込", "能力プラス補正(中)", MEDIUM, null),
            new TraitDefinition(38, "鬼脚", UPPER, RUNNING_STYLE,
                    "差し、追込", "能力プラス補正(大)", LARGE, 37),

            new TraitDefinition(39, "強心臓", NORMAL, DISTANCE_APTITUDE,
                    "距離適性の上限以上のレース", "ランダムで能力プラス補正(小)", SMALL, null),
            new TraitDefinition(40, "鋼の心臓", UPPER, DISTANCE_APTITUDE,
                    "距離適性の上限以上のレース", "ランダムで能力プラス補正(中)", MEDIUM, 39),

            new TraitDefinition(41, "距離短縮", NORMAL, DISTANCE,
                    "前走（5着以下）より200m以上短い距離のレース", "能力プラス補正(中)", MEDIUM, null),
            new TraitDefinition(42, "短縮一変", UPPER, DISTANCE,
                    "前走（5着以下）より200m以上短い距離のレース", "能力プラス補正(大)", LARGE, 41),
            new TraitDefinition(43, "距離延長", NORMAL, DISTANCE,
                    "前走（5着以下）より200m以上長い距離のレース", "能力プラス補正(中)", MEDIUM, null),
            new TraitDefinition(44, "延長一変", UPPER, DISTANCE,
                    "前走（5着以下）より200m以上長い距離のレース", "能力プラス補正(大)", LARGE, 43),

            new TraitDefinition(45, "完全燃焼", NORMAL, LAYOFF,
                    "休み明け", "能力プラス補正(中)/ランダムでレース後疲労上昇(小)", MEDIUM, null),
            new TraitDefinition(46, "全身全霊", UPPER, LAYOFF,
                    "休み明け", "能力プラス補正(大)/ランダムでレース後疲労上昇(中)", LARGE, 45),

            new TraitDefinition(47, "学習能力", NORMAL, NONE,
                    "近4走以内に同じ競馬場の重賞勝利", "能力プラス補正(中)/ランダムでレース後育成度上昇(小)", MEDIUM, null),
            new TraitDefinition(48, "勝利の再現", UPPER, NONE,
                    "近4走以内に同じ競馬場の重賞勝利", "能力プラス補正(大)/ランダムでレース後育成度上昇(中)", LARGE, 47),

            new TraitDefinition(49, "春競馬", NORMAL, SEASON,
                    "春（3月〜5月）のレース", "能力プラス補正(小)", SMALL, null),
            new TraitDefinition(50, "春満開", UPPER, SEASON,
                    "春（3月〜5月）のレース", "能力プラス補正(中)", MEDIUM, 49),
            new TraitDefinition(51, "夏競馬", NORMAL, SEASON,
                    "夏（6月〜8月）のレース", "能力プラス補正(小)", SMALL, null),
            new TraitDefinition(52, "夏馬", UPPER, SEASON,
                    "夏（6月〜8月）のレース", "能力プラス補正(中)", MEDIUM, 51),
            new TraitDefinition(53, "秋競馬", NORMAL, SEASON,
                    "秋（9月〜11月）のレース", "能力プラス補正(小)", SMALL, null),
            new TraitDefinition(54, "秋の嵐", UPPER, SEASON,
                    "秋（9月〜11月）のレース", "能力プラス補正(中)", MEDIUM, 53),
            new TraitDefinition(55, "冬競馬", NORMAL, SEASON,
                    "冬（12月〜2月）のレース", "能力プラス補正(小)", SMALL, null),
            new TraitDefinition(56, "冬将軍", UPPER, SEASON,
                    "冬（12月〜2月）のレース", "能力プラス補正(中)", MEDIUM, 55),

            new TraitDefinition(57, "平原巧者", NORMAL, NONE,
                    "平原競馬場（元・東京競馬場）", "能力プラス補正(小)", SMALL, null),
            new TraitDefinition(58, "平原の申し子", UPPER, NONE,
                    "平原競馬場（元・東京競馬場）", "能力プラス補正(中)", MEDIUM, 57),
            new TraitDefinition(59, "丘陵巧者", NORMAL, NONE,
                    "丘陵競馬場（元・中山競馬場）", "能力プラス補正(小)", SMALL, null),
            new TraitDefinition(60, "丘陵の申し子", UPPER, NONE,
                    "丘陵競馬場（元・中山競馬場）", "能力プラス補正(中)", MEDIUM, 59),
            new TraitDefinition(61, "竹林巧者", NORMAL, NONE,
                    "竹林競馬場（元・京都競馬場）", "能力プラス補正(小)", SMALL, null),
            new TraitDefinition(62, "竹林の申し子", UPPER, NONE,
                    "竹林競馬場（元・京都競馬場）", "能力プラス補正(中)", MEDIUM, 61),
            new TraitDefinition(63, "渓谷巧者", NORMAL, NONE,
                    "渓谷競馬場（元・阪神競馬場）", "能力プラス補正(小)", SMALL, null),
            new TraitDefinition(64, "渓谷の申し子", UPPER, NONE,
                    "渓谷競馬場（元・阪神競馬場）", "能力プラス補正(中)", MEDIUM, 63),

            new TraitDefinition(65, "坂越え", NORMAL, HILL,
                    "最後の直線に坂のある競馬場", "ランダムで能力プラス補正(中)", MEDIUM, null),
            new TraitDefinition(66, "タフランナー", UPPER, HILL,
                    "最後の直線に坂のある競馬場", "ランダムで能力プラス補正(大)", LARGE, 65),
            new TraitDefinition(67, "平坦巧者", NORMAL, HILL,
                    "最後の直線に坂のない競馬場", "ランダムで能力プラス補正(中)", MEDIUM, null),
            new TraitDefinition(68, "フラットランナー", UPPER, HILL,
                    "最後の直線に坂のない競馬場", "ランダムで能力プラス補正(大)", LARGE, 67),

            new TraitDefinition(69, "行きっぷり", NORMAL, DISTANCE_APTITUDE,
                    "距離適性の下限以下のレース", "ランダムで能力プラス補正(小)", SMALL, null),
            new TraitDefinition(70, "前進気勢", UPPER, DISTANCE_APTITUDE,
                    "距離適性の下限以下のレース", "ランダムで能力プラス補正(中)", MEDIUM, 69),

            new TraitDefinition(71, "一人旅", NORMAL, RUNNING_STYLE,
                    "大逃げ、逃げ", "能力プラス補正(小)/大逃げで最後まで粘りやすくなる", SMALL, null),
            new TraitDefinition(72, "唯我独尊", UPPER, RUNNING_STYLE,
                    "大逃げ、逃げ", "能力プラス補正(中)/大逃げで最後までさらに粘りやすくなる", MEDIUM, 71),
            new TraitDefinition(73, "ペースメイク", NORMAL, RUNNING_STYLE,
                    "溜め逃げ（ペース・位置取り不問）", "能力プラス補正(小)", SMALL, null),
            new TraitDefinition(74, "レースメイク", UPPER, RUNNING_STYLE,
                    "溜め逃げ（ペース・位置取り不問）", "能力プラス補正(中)", MEDIUM, 73),
            new TraitDefinition(75, "最短距離", NORMAL, RUNNING_STYLE,
                    "イン狙い", "能力プラス補正(小)", SMALL, null),
            new TraitDefinition(76, "インベタ張り付き", UPPER, RUNNING_STYLE,
                    "イン狙い", "能力プラス補正(中)", MEDIUM, 75),
            new TraitDefinition(77, "ロングスパート", NORMAL, RUNNING_STYLE,
                    "まくり（ペース不問）", "能力プラス補正(小)", SMALL, null),
            new TraitDefinition(78, "ひとまくり", UPPER, RUNNING_STYLE,
                    "まくり（ペース不問）", "能力プラス補正(中)", MEDIUM, 77),
            new TraitDefinition(79, "決め打ち", NORMAL, RUNNING_STYLE,
                    "最後方強襲（ペース・位置取り不問）", "能力プラス補正(小)", SMALL, null),
            new TraitDefinition(80, "丁半博打", UPPER, RUNNING_STYLE,
                    "最後方強襲（ペース・位置取り不問）", "能力プラス補正(中)", MEDIUM, 79),

            new TraitDefinition(87, "スプリントギア", NORMAL, DISTANCE,
                    "1400m以下のレース", "ランダムで序盤から道中で速度上昇(中)", null, null),
            new TraitDefinition(88, "激流チェイス", UPPER, DISTANCE,
                    "1400m以下のレース", "ランダムで序盤から道中で速度上昇(大)", null, 87),
            new TraitDefinition(89, "スプリントターボ", NORMAL, DISTANCE,
                    "1400m以下のレース", "ランダムで仕掛けどころで速度上昇(中)", null, null),
            new TraitDefinition(90, "光速スパート", UPPER, DISTANCE,
                    "1400m以下のレース", "ランダムで仕掛けどころで速度上昇(大)", null, 89),
            new TraitDefinition(91, "マイルギア", NORMAL, DISTANCE,
                    "1401〜1800mのレース", "ランダムで序盤から道中で速度上昇(中)", null, null),
            new TraitDefinition(92, "勇往邁進", UPPER, DISTANCE,
                    "1401〜1800mのレース", "ランダムで序盤から道中で速度上昇(大)", null, 91),
            new TraitDefinition(93, "マイルターボ", NORMAL, DISTANCE,
                    "1401〜1800mのレース", "ランダムで仕掛けどころで速度上昇(中)", null, null),
            new TraitDefinition(94, "疾風迅雷", UPPER, DISTANCE,
                    "1401〜1800mのレース", "ランダムで仕掛けどころで速度上昇(大)", null, 93),
            new TraitDefinition(95, "クラシックギア", NORMAL, DISTANCE,
                    "1801〜2400mのレース", "ランダムで序盤から道中で速度上昇(中)", null, null),
            new TraitDefinition(96, "王者の行軍", UPPER, DISTANCE,
                    "1801〜2400mのレース", "ランダムで序盤から道中で速度上昇(大)", null, 95),
            new TraitDefinition(97, "クラシックターボ", NORMAL, DISTANCE,
                    "1801〜2400mのレース", "ランダムで仕掛けどころで速度上昇(中)", null, null),
            new TraitDefinition(98, "王者の進撃", UPPER, DISTANCE,
                    "1801〜2400mのレース", "ランダムで仕掛けどころで速度上昇(大)", null, 97),
            new TraitDefinition(99, "長距離ギア", NORMAL, DISTANCE,
                    "2401m以上のレース", "ランダムで序盤から道中で速度上昇(中)", null, null),
            new TraitDefinition(100, "長駆追走", UPPER, DISTANCE,
                    "2401m以上のレース", "ランダムで序盤から道中で速度上昇(大)", null, 99),
            new TraitDefinition(101, "長距離ターボ", NORMAL, DISTANCE,
                    "2401m以上のレース", "ランダムで仕掛けどころで速度上昇(中)", null, null),
            new TraitDefinition(102, "長駆突入", UPPER, DISTANCE,
                    "2401m以上のレース", "ランダムで仕掛けどころで速度上昇(大)", null, 101));

    /**
     * 特性が属する系統の根（通常特性自身のid、または上位特性の進化元のid）。
     * 同じ系統どうしは競合しない（通常⇔上位は同じ枠を使う関係のため）。
     */
    public static int lineageRootId(TraitDefinition trait) {
        return trait.evolvesFromId() != null ? trait.evolvesFromId() : trait.id();
    }

    /**
     * 2つの特性が競合するか（同時に保有できないか）。系統が異なり、かつ
     * {@link TraitCategory#NONE}以外の同一カテゴリであれば競合する（§27.7.2）。
     */
    public static boolean conflicts(TraitDefinition a, TraitDefinition b) {
        if (lineageRootId(a) == lineageRootId(b)) {
            return false;
        }
        if (a.category() == TraitCategory.NONE) {
            return false;
        }
        return a.category() == b.category();
    }

    /** idから特性を取得する。 */
    public static Optional<TraitDefinition> byId(int id) {
        return ALL.stream().filter(trait -> trait.id() == id).findFirst();
    }
}
