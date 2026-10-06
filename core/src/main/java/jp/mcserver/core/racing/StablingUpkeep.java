package jp.mcserver.core.racing;

/**
 * 競走馬の維持費（厩舎預託費）を国庫（§7）から徴収する仕組み（`minecraft_server_spec.md` §27.11）。
 *
 * <p>保有しているだけでスプリットごとに発生し、**頭数が増えるほど追加の1頭あたりの
 * 単価が上がる**——k頭目の維持費は{@value #BASE_UPKEEP_EXP} exp×kになる。この
 * 「k頭目」は特定のプレイヤー個人ではなく、**国に属する全プレイヤーが所有する
 * 競走馬の合計頭数**（§27.10と同じ対象範囲）で数える。合計は等差数列の和になり、
 * 保有頭数の2乗に比例して重くなる（§22）。
 */
public final class StablingUpkeep {

    private StablingUpkeep() {}

    /** k頭目の維持費の基準単価（exp、§27.11）。k頭目の維持費は{@code BASE_UPKEEP_EXP × k}。 */
    public static final long BASE_UPKEEP_EXP = 200;

    /**
     * 保有頭数のうちk頭目（1始まり）の、1スプリットあたりの維持費（exp）。
     *
     * @param horseRank 保有頭数の中での順位（1始まり）
     */
    public static long upkeepForNthHorse(int horseRank) {
        if (horseRank < 1) {
            throw new IllegalArgumentException("頭数の順位は1以上である必要がある: " + horseRank);
        }
        return BASE_UPKEEP_EXP * horseRank;
    }

    /**
     * 国全体でhorseCount頭を保有しているときの、1スプリットあたりの維持費の合計（exp）。
     * 等差数列の和（{@code BASE_UPKEEP_EXP × horseCount × (horseCount + 1) / 2}）になる。
     */
    public static long totalUpkeepExp(int horseCount) {
        if (horseCount < 0) {
            throw new IllegalArgumentException("保有頭数が負である: " + horseCount);
        }
        return BASE_UPKEEP_EXP * horseCount * (horseCount + 1) / 2;
    }
}
