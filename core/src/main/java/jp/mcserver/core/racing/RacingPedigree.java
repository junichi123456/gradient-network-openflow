package jp.mcserver.core.racing;

import java.util.HashSet;
import java.util.List;
import java.util.Set;

/**
 * 競馬専用ワールド専用の血統登録（`minecraft_server_spec.md` §27.2）。
 *
 * <p>本来の設定は父方・母方それぞれ5世代（本馬を含めず2^1〜2^5＝合計62頭）だが、
 * 62頭ぶんの祖先データをすべて個体管理するのは非現実的なため、実装上は
 * <b>直系4代（父母2＋祖父母4＋曾祖父母8＋高祖父母16＝合計30頭）を実データとして
 * 保持</b>し、5代目以降は系統タグのみで簡略集計する近似を取る。
 *
 * <p>{@link jp.mcserver.core.HorseTraining.Parentage}（§26.7.3、2世代・6頭までの
 * オーバーワールド用の簡易モデル）とは独立した、このワールド専用の血統である。
 */
public record RacingPedigree(
        String parentAId,
        String parentBId,
        List<String> grandparents,
        List<String> greatGrandparents,
        List<String> greatGreatGrandparents) {

    /** 各世代の祖先数（祖父母4・曾祖父母8・高祖父母16）。 */
    public static final int GRANDPARENT_COUNT = 4;
    public static final int GREAT_GRANDPARENT_COUNT = 8;
    public static final int GREAT_GREAT_GRANDPARENT_COUNT = 16;

    /** 実データとして保持する祖先の総数（父母2＋祖父母4＋曾祖父母8＋高祖父母16）。 */
    public static final int TRACKED_ANCESTOR_COUNT =
            2 + GRANDPARENT_COUNT + GREAT_GRANDPARENT_COUNT + GREAT_GREAT_GRANDPARENT_COUNT;

    public RacingPedigree {
        requireSize(grandparents, GRANDPARENT_COUNT, "祖父母");
        requireSize(greatGrandparents, GREAT_GRANDPARENT_COUNT, "曾祖父母");
        requireSize(greatGreatGrandparents, GREAT_GREAT_GRANDPARENT_COUNT, "高祖父母");
    }

    private static void requireSize(List<String> list, int expected, String label) {
        if (list.size() != expected) {
            throw new IllegalArgumentException(
                    label + "は" + expected + "頭ぶん必要である（実際: " + list.size() + "頭）");
        }
    }

    /** 記録されている祖先のIDの集合（{@code null} を除く。最大{@value #TRACKED_ANCESTOR_COUNT}頭）。 */
    public Set<String> ancestors() {
        Set<String> ids = new HashSet<>();
        addIfPresent(ids, parentAId);
        addIfPresent(ids, parentBId);
        grandparents.forEach(id -> addIfPresent(ids, id));
        greatGrandparents.forEach(id -> addIfPresent(ids, id));
        greatGreatGrandparents.forEach(id -> addIfPresent(ids, id));
        return ids;
    }

    private static void addIfPresent(Set<String> ids, String id) {
        if (id != null) {
            ids.add(id);
        }
    }
}
