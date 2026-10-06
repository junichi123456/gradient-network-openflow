package jp.mcserver.core.racing;

import java.util.List;

/**
 * 競馬専用ワールドの競馬場（`minecraft_server_spec.md` §27.8.1）。
 *
 * <p>『ウイニングポスト10 2026』の中央10場相当を、このワールド用のバイオーム名に
 * 置き換えたもの。現実の国名・地域名（中央関東／中央関西等）は使わず、
 * {@link Classification}で「主要場／地域場」の2区分のみを持たせる。
 *
 * <p>芝質（{@link TurfGrade}）・坂・小回りは、参照元サイトの目盛り画像からの
 * 読み取りによる暫定値である（§27.8.1・§27.8.4、確認が要る）。
 */
public enum RacingCourse {

    TAIGA("タイガ", Classification.REGIONAL, false, true, TurfGrade.MEDIUM,
            List.of(1200, 1500, 1800, 2000, 2600)),
    RYUHYO("流氷", Classification.REGIONAL, false, true, TurfGrade.MEDIUM,
            List.of(1000, 1200, 1800, 2000, 2600)),
    KOGEN("高原", Classification.REGIONAL, true, true, TurfGrade.LIGHT,
            List.of(1200, 1800, 2000, 2600)),
    SHITCHI("湿地", Classification.REGIONAL, false, false, TurfGrade.LIGHT,
            List.of(1000, 1200, 1400, 1600, 2000, 2200, 2400)),
    HEIGEN("平原", Classification.MAJOR, true, false, TurfGrade.LIGHT,
            List.of(1400, 1600, 1800, 2000, 2300, 2400, 2500, 3400)),
    KYURYO("丘陵", Classification.MAJOR, true, true, TurfGrade.MEDIUM,
            List.of(1200, 1600, 1800, 2000, 2200, 2500, 3600)),
    JUKAI("樹海", Classification.REGIONAL, true, true, TurfGrade.MEDIUM,
            List.of(1200, 1400, 1600, 2000, 2200)),
    CHIKURIN("竹林", Classification.MAJOR, false, false, TurfGrade.LIGHT,
            List.of(1200, 1400, 1600, 1800, 2000, 2200, 2400, 3000, 3200)),
    KEIKOKU("渓谷", Classification.MAJOR, true, false, TurfGrade.LIGHT,
            List.of(1200, 1400, 1600, 1800, 2000, 2200, 2400, 2600, 3000)),
    KAZAN("火山", Classification.REGIONAL, false, true, TurfGrade.LIGHT,
            List.of(1200, 1800, 2000, 2600));

    /** 開催区分（§27.8.1の「開催区分」列）。 */
    public enum Classification { MAJOR, REGIONAL }

    /** 芝質（§27.8.1の「芝質」列を軽／中／重の3段階に単純化したもの）。 */
    public enum TurfGrade { LIGHT, MEDIUM, HEAVY }

    private final String displayName;
    private final Classification classification;
    private final boolean hasHill;
    private final boolean tightTurn;
    private final TurfGrade turfGrade;
    private final List<Integer> turfDistancesMeters;

    RacingCourse(String displayName, Classification classification, boolean hasHill,
            boolean tightTurn, TurfGrade turfGrade, List<Integer> turfDistancesMeters) {
        this.displayName = displayName;
        this.classification = classification;
        this.hasHill = hasHill;
        this.tightTurn = tightTurn;
        this.turfGrade = turfGrade;
        this.turfDistancesMeters = turfDistancesMeters;
    }

    /** ワールド内名称（例: 「タイガ」）。 */
    public String displayName() {
        return displayName;
    }

    public Classification classification() {
        return classification;
    }

    /** 坂を持つか。 */
    public boolean hasHill() {
        return hasHill;
    }

    /** 小回り（タイトターン）のコースか。 */
    public boolean tightTurn() {
        return tightTurn;
    }

    public TurfGrade turfGrade() {
        return turfGrade;
    }

    /** 芝の収録距離（メートル、主なもの）。 */
    public List<Integer> turfDistancesMeters() {
        return turfDistancesMeters;
    }

    /** この競馬場が指定の芝distance（メートル）でレースを開催できるか。 */
    public boolean hostsTurfDistance(int meters) {
        return turfDistancesMeters.contains(meters);
    }
}
