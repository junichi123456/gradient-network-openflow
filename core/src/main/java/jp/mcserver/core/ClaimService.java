package jp.mcserver.core;

import java.util.HashSet;
import java.util.Set;

/**
 * 棒による領土の拡張・削除（§4.11）。
 *
 * <p>権利者（首長）が棒を持ってチャンク上で操作する。拡張は上限に余裕がある場合のみ実行でき、
 * 削除はテキストによる確認を要する。
 */
public final class ClaimService {

    private ClaimService() {}

    /** 削除の確認が有効な時間（秒）。 */
    public static final int CONFIRMATION_TTL_SECONDS = 30;

    /** 戦争の勝利によるボーナスチャンクの累積上限（§11.7）。 */
    public static final int MAX_BONUS_CHUNKS = 40;

    /**
     * 判定に必要な領土の状態。
     *
     * @param bonusChunks 戦争の勝利で得たボーナスチャンク（rank14以下では0として渡す）
     */
    public record TerritoryState(String nationName, int rank, ChunkPos capital,
                                 Set<ChunkPos> chunks, int bonusChunks) {

        public TerritoryState {
            chunks = Set.copyOf(chunks);
            if (bonusChunks < 0 || bonusChunks > MAX_BONUS_CHUNKS) {
                throw new IllegalArgumentException("ボーナスチャンクが範囲外である: " + bonusChunks);
            }
        }

        /** 保有可能なチャンク数の上限。 */
        public int limit() {
            return Formulas.chunks(rank) + bonusChunks;
        }

        public int owned() {
            return chunks.size();
        }

        /** 拡張に使える残り。 */
        public int remaining() {
            return Math.max(0, limit() - owned());
        }
    }

    /** 対象チャンクの外的条件。領土システムの外側から与える。 */
    public record ChunkCondition(boolean protectedZone, boolean ownedByOtherNation,
                                 boolean reacquisitionRestricted) {

        public static ChunkCondition free() {
            return new ChunkCondition(false, false, false);
        }
    }

    public enum ExpandDenial {
        NONE, NOT_LEADER, ALREADY_OWNED, AT_LIMIT, NOT_ADJACENT,
        PROTECTED_ZONE, OWNED_BY_OTHER, REACQUISITION_RESTRICTED
    }

    public record ExpandResult(boolean ok, ExpandDenial denial, String message) {}

    public enum RemoveDenial {
        NONE, NOT_LEADER, NOT_OWNED, CAPITAL, WOULD_DISCONNECT, LAST_CHUNK, ENCLOSED
    }

    /**
     * @param requiresConfirmation 実行可能であり、テキストによる確認を待つ状態
     */
    public record RemoveResult(boolean ok, RemoveDenial denial, String message,
                               boolean requiresConfirmation) {}

    /**
     * 領土の拡張。
     *
     * <p>新規取得チャンクは既存領土のいずれかに隣接している必要がある（§4.10）。
     * 同盟国・属国の領土は隣接扱いにしない。領土が空の場合（最初の野営地）は隣接を要求しない。
     */
    public static ExpandResult expand(TerritoryState state, ChunkPos target,
                                      ChunkCondition condition, boolean isLeader) {
        if (!isLeader) {
            return deny(ExpandDenial.NOT_LEADER, "領土を拡張できるのは首長のみです");
        }
        if (state.chunks().contains(target)) {
            return deny(ExpandDenial.ALREADY_OWNED, "このチャンクは既に自国領土です");
        }
        if (condition.protectedZone()) {
            return deny(ExpandDenial.PROTECTED_ZONE, "保護区は領土にできません");
        }
        if (condition.ownedByOtherNation()) {
            return deny(ExpandDenial.OWNED_BY_OTHER, "このチャンクは他国の領土です");
        }
        if (condition.reacquisitionRestricted()) {
            return deny(ExpandDenial.REACQUISITION_RESTRICTED, "このチャンクは再取得制限の範囲内です");
        }
        if (state.remaining() <= 0) {
            return deny(ExpandDenial.AT_LIMIT,
                    "拡張できるチャンクがありません（保有 " + state.owned() + " / 上限 " + state.limit()
                            + "）。ランクを上げるまで拡張できません");
        }
        if (!state.chunks().isEmpty() && !isAdjacentToTerritory(state.chunks(), target)) {
            return deny(ExpandDenial.NOT_ADJACENT, "既存の領土に隣接していません");
        }
        int remainingAfter = state.remaining() - 1;
        return new ExpandResult(true, ExpandDenial.NONE,
                "領土を拡張しました " + target + "（残り " + remainingAfter + "）");
    }

    /**
     * 領土の削除。実行可能な場合は確認待ちを返し、確定は {@link #confirmRemoval} で行う。
     *
     * <p>首都チャンクは削除できない。削除により残存領土が首都と連結でなくなる場合も拒否する
     * （§4.7 の連結性の要件を、任意削除にも適用する）。
     *
     * <p>さらに、<b>四辺すべてを自国領土に囲まれたチャンクは削除できない</b>。
     * これを認めると、領土の内側に領土外の穴を作り、チャンクを消費せずに囲い込んで
     * 実質的な支配下に置けてしまうためである。
     */
    public static RemoveResult remove(TerritoryState state, ChunkPos target, boolean isLeader) {
        if (!isLeader) {
            return denyRemove(RemoveDenial.NOT_LEADER, "領土を削除できるのは首長のみです");
        }
        if (!state.chunks().contains(target)) {
            return denyRemove(RemoveDenial.NOT_OWNED, "このチャンクは自国領土ではありません");
        }
        if (target.equals(state.capital())) {
            return denyRemove(RemoveDenial.CAPITAL, "首都チャンクは削除できません");
        }
        if (state.chunks().size() <= 1) {
            return denyRemove(RemoveDenial.LAST_CHUNK, "最後の1チャンクは削除できません");
        }
        if (ChunkRelease.exposure(state.chunks(), target) == 0) {
            return denyRemove(RemoveDenial.ENCLOSED,
                    "四辺を自国領土に囲まれたチャンクは削除できません（領土外の土地を囲い込むことになるため）");
        }
        Set<ChunkPos> after = new HashSet<>(state.chunks());
        after.remove(target);
        if (!ChunkRelease.isConnected(after, state.capital())) {
            return denyRemove(RemoveDenial.WOULD_DISCONNECT,
                    "このチャンクを削除すると領土が首都から分断されます");
        }
        return new RemoveResult(true, RemoveDenial.NONE,
                "領土 " + target + " を削除します。取り消せません。確認するには "
                        + CONFIRMATION_TTL_SECONDS + "秒以内に /territory confirm と入力してください",
                true);
    }

    /** 削除の確認待ち。 */
    public record PendingRemoval(ChunkPos target, long issuedAtSeconds) {

        public boolean valid(ChunkPos current, long nowSeconds) {
            return target.equals(current) && nowSeconds - issuedAtSeconds <= CONFIRMATION_TTL_SECONDS;
        }
    }

    /**
     * 削除の確定。確認が対象チャンクと一致し、かつ期限内である場合のみ成立する。
     *
     * @return 削除を実行してよいか
     */
    public static boolean confirmRemoval(PendingRemoval pending, ChunkPos target, long nowSeconds) {
        return pending != null && pending.valid(target, nowSeconds);
    }

    /** 既存領土に直交隣接しているか（§4.10）。 */
    static boolean isAdjacentToTerritory(Set<ChunkPos> territory, ChunkPos target) {
        for (ChunkPos n : target.orthogonalNeighbors()) {
            if (territory.contains(n)) {
                return true;
            }
        }
        return false;
    }

    private static ExpandResult deny(ExpandDenial denial, String message) {
        return new ExpandResult(false, denial, message);
    }

    private static RemoveResult denyRemove(RemoveDenial denial, String message) {
        return new RemoveResult(false, denial, message, false);
    }
}
