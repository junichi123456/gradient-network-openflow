package jp.mcserver.core;

/**
 * あるチャンクの帰属。国家名は領土である場合のみ持つ。
 */
public record ChunkOwner(TerritoryRelation relation, String nationName) {

    public ChunkOwner {
        if (relation.isTerritory() && (nationName == null || nationName.isBlank())) {
            throw new IllegalArgumentException("領土には国家名が必要である: " + relation);
        }
    }

    public static ChunkOwner wilderness() {
        return new ChunkOwner(TerritoryRelation.WILDERNESS, null);
    }

    public static ChunkOwner protectedZone() {
        return new ChunkOwner(TerritoryRelation.PROTECTED_ZONE, null);
    }

    public static ChunkOwner of(TerritoryRelation relation, String nationName) {
        return new ChunkOwner(relation, nationName);
    }

    /** 「自国」「同盟国〈X〉」のような表示名。 */
    public String display() {
        return switch (relation) {
            case OWN -> "自国";
            case WILDERNESS -> "領土外";
            case PROTECTED_ZONE -> "保護区";
            case FOREIGN -> nationName;
            default -> relation.label() + "「" + nationName + "」";
        };
    }

    /** 同一の領域とみなせるか。国家名まで一致して初めて同一とする。 */
    public boolean sameArea(ChunkOwner other) {
        if (other == null) {
            return false;
        }
        if (relation != other.relation) {
            return false;
        }
        return nationName == null ? other.nationName == null : nationName.equals(other.nationName);
    }
}
