namespace MysteryDungeon.Combat;

// 制限技の計測用カウンタ（診断専用。ゲームの挙動には使わない）。
// BattleMatchScene の --random / --r4-test が試合の前後で読んで出力する。
public static class RestrictionStats
{
    // ItemJam で Holds() が false にされた回数（その持ち物がそのとき問われた効果と
    // 一致していた場合だけ数える＝止められた効果判定の回数）。
    public static long ItemChecksSuppressed;

    // 制限技が実際にかかった回数（種類ごと）。
    public static readonly long[] Applied = new long[System.Enum.GetValues<RestrictionKind>().Length];

    // ItemJam を無効にして R4 を「ただの攻撃技」にする診断スイッチ（--r4-test の対照群）。
    public static bool DisableItemJam;

    public static void Reset()
    {
        ItemChecksSuppressed = 0;
        System.Array.Clear(Applied);
    }
}
