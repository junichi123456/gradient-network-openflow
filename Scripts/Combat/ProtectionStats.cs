namespace MysteryDungeon.Combat;

// プロテクションの計測用カウンタ（診断専用。ゲームの挙動には使わない）。
// BattleMatchScene の --random が試合の前後で読んで出力する。
public static class ProtectionStats
{
    public static long Used;      // 技が実行された回数（不発を含む）
    public static long Granted;   // 新しく状態が付いた体数
    public static long Blocked;   // 技を無効化した回数
    public static long Expired;   // 使われずにサイクル終了で消えた体数

    public static void Reset()
    {
        Used = 0;
        Granted = 0;
        Blocked = 0;
        Expired = 0;
    }
}
