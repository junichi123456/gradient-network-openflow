package jp.mcserver.core.racing;

/**
 * 通常特性・上位特性の1件（`minecraft_server_spec.md` §27.7.2）。
 *
 * <p>{@link #evolvesFromId}は上位特性が進化元とする通常特性のidで、通常特性では
 * {@code null}。同一系統（進化元が同じ、または自分自身が進化元）は競合しない。
 *
 * @param id 番号（原案の通番。除外した特性の分だけ欠番がある）
 * @param name 特性名
 * @param tier 通常・上位の別
 * @param category 競合カテゴリ（{@link TraitCategory#NONE}なら競合しない）
 * @param triggerCondition 発動条件
 * @param effect 効果（能力プラス補正以外の副次効果を含む説明文）
 * @param abilityBonusSize 効果が能力プラス補正であるときの大きさ。能力プラス補正
 *     以外の効果（速度上昇・疲労軽減・調子上昇など）は{@code null}——具体的な量は
 *     未定（§22・§23）
 * @param evolvesFromId 上位特性の場合、進化元の通常特性のid。通常特性では{@code null}
 */
public record TraitDefinition(
        int id,
        String name,
        TraitTier tier,
        TraitCategory category,
        String triggerCondition,
        String effect,
        AbilityBonusSize abilityBonusSize,
        Integer evolvesFromId) {

    public TraitDefinition {
        if (id <= 0) {
            throw new IllegalArgumentException("番号は正の値である必要がある: " + id);
        }
        if (name == null || name.isBlank()) {
            throw new IllegalArgumentException("特性名が空である: " + id);
        }
        if (tier == TraitTier.UPPER && evolvesFromId == null) {
            throw new IllegalArgumentException("上位特性は進化元の通常特性を持つ必要がある: " + name);
        }
        if (tier == TraitTier.NORMAL && evolvesFromId != null) {
            throw new IllegalArgumentException("通常特性は進化元を持たない: " + name);
        }
    }
}
