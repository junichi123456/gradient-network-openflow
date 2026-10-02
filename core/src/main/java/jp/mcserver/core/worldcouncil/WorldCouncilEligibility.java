package jp.mcserver.core.worldcouncil;

/**
 * 「世界協議」（world_council_spec.md）の参加資格判定。
 *
 * <p>1か国の扱いは宗主国であり、属国も同一の国家に換算する。属国単独では登録できず、
 * 属国の代表者は宗主国の枠に含めて扱う（{@link #effectiveNation}）。
 */
public final class WorldCouncilEligibility {

    private WorldCouncilEligibility() {}

    /** 参加可能な最低国家ランク（属国保有資格＝リーダー枠開放の基準と同じ）。 */
    public static final int MIN_RANK = 7;

    public static boolean eligible(int rank) {
        return rank >= MIN_RANK;
    }

    /**
     * 実効国家名。属国は宗主国の名前に読み替える。
     *
     * @param nationName    当該国家名
     * @param suzerainName  宗主国名（属国でなければ null）
     */
    public static String effectiveNation(String nationName, String suzerainName) {
        return suzerainName != null ? suzerainName : nationName;
    }
}
