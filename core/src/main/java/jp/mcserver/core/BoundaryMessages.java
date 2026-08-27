package jp.mcserver.core;

import java.util.ArrayList;
import java.util.List;

/**
 * 領域の出入りに伴う表示（§4.11）。
 *
 * <p>領土に入った時に「〜に入りました」、出た時に「〜から出ました」を表示する。
 * 領土から別の領土へ直接移った場合は、退出と進入の両方を表示する。
 */
public final class BoundaryMessages {

    private BoundaryMessages() {}

    /**
     * チャンクを跨いだときに表示する行を返す。同一の領域内の移動では空を返す。
     *
     * @param from 直前のチャンクの帰属（初回ログイン時は null）
     * @param to   現在のチャンクの帰属
     */
    public static List<String> onCross(ChunkOwner from, ChunkOwner to) {
        List<String> lines = new ArrayList<>(2);
        if (to == null) {
            throw new IllegalArgumentException("現在の帰属が null である");
        }
        if (to.sameArea(from)) {
            return lines;
        }
        if (from != null && from.relation() != TerritoryRelation.WILDERNESS) {
            lines.add(subject(from) + "から出ました");
        }
        if (to.relation() != TerritoryRelation.WILDERNESS) {
            lines.add(subject(to) + "に入りました");
        }
        return lines;
    }

    /** 「自国の領土」「保護区」のような、文の主語となる表記。 */
    private static String subject(ChunkOwner owner) {
        return owner.relation().isTerritory() ? owner.display() + "の領土" : owner.display();
    }

    /** 棒を所持している首長に対する、現在チャンクの状態表示（§4.11）。 */
    public static String inspect(ChunkOwner here, int owned, int limit) {
        String head = here.relation() == TerritoryRelation.OWN
                ? "このチャンクは自国領土です"
                : "このチャンクは" + here.display() + "です";
        return head + "（保有 " + owned + " / 上限 " + limit + "、残り " + Math.max(0, limit - owned) + "）";
    }
}
