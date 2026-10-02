package jp.mcserver.core.racing;

/**
 * 競馬専用ワールドの5大能力値（`minecraft_server_spec.md` §27.3）。
 *
 * <p>§26.7（オーバーワールドの馬）とは独立した、このワールド専用のスケール
 * （0〜100、{@link AbilityValue}）を持つ。
 */
public enum AbilityStat {
    SPEED,
    STAMINA,
    POWER,
    GUTS,
    WISDOM
}
