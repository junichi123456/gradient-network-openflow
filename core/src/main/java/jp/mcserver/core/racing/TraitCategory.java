package jp.mcserver.core.racing;

/**
 * 通常・上位特性の競合カテゴリ（`minecraft_server_spec.md` §27.7.2）。
 *
 * <p>同一カテゴリの特性は競合し、系統（{@link TraitDefinition#evolvesFromId}で
 * つながる通常⇔上位のペア）が異なる限り同時に保有できない。{@link #NONE}は
 * どのカテゴリとも競合しない（東京巧者のような競馬場別特性など）。
 */
public enum TraitCategory {
    /** レース系（格付け・開催区分による発動条件）。 */
    RACE_GRADE,
    /** 距離系。 */
    DISTANCE,
    /** 休み明け系。 */
    LAYOFF,
    /** 脚質系。 */
    RUNNING_STYLE,
    /** 季節系。 */
    SEASON,
    /** 坂系（最後の直線の坂の有無）。 */
    HILL,
    /** 適正距離系（距離適性の上限・下限との乖離）。 */
    DISTANCE_APTITUDE,
    /** 競合しない（競馬場別特性など）。 */
    NONE
}
