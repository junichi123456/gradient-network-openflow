using Godot;

namespace MysteryDungeon.Battle;

// 対戦盤の寸法と座標規約。
//
// 盤面は 8行(縦) x 7列(横) の56マス。縦2x横3の自陣ブロックが縦方向に
// 向かい合うので、奥行きのほうが長い。
//
// 座標は「論理座標」1つだけを持つ。論理座標では常に
//   Faction.Player  が下側 (Y が大きい側)
//   Faction.Enemy   が上側 (Y が小さい側)
// で、描画側が自分の陣営を下に見せるために必要なら180度回転させる。
// つまり両プレイヤーは同じ盤面を別の向きから見るだけで、ロジックは
// 1つの向きしか知らない。
public static class BattleBoard
{
    public const int Width = 7;    // 横
    public const int Height = 8;   // 縦

    // 対戦盤は全マスが同一の部屋。TargetResolver の Room 射程は
    // FloorController.GetRoomBoundsAt() を引くので、これで
    // 「Room = 盤面全体（味方は除外）」が既存コードのまま成立する。
    public const int ArenaRoomId = 0;

    // 各陣営の初期ブロックは縦2x横3。盤面中央に上下で向かい合わせる。
    public const int FormationWidth = 3;
    public const int FormationHeight = 2;

    // 幅7の中央に幅3を置くので左端は 2、上下は 4行ぶんを中央に寄せて
    // Y=2..5 を使う（敵 2..3 / 自分 4..5）。上列同士が接する。
    public const int FormationLeft = (Width - FormationWidth) / 2;          // 2
    public const int EnemyTop = (Height - FormationHeight * 2) / 2;         // 2
    public const int PlayerTop = EnemyTop + FormationHeight;                // 4

    // その陣営が配置に使える6マス。自由配置なので、この中から4マスを選ぶ。
    public static Rect2I FormationArea(Entities.Faction faction) =>
        new(new Vector2I(FormationLeft, faction == Entities.Faction.Player ? PlayerTop : EnemyTop),
            new Vector2I(FormationWidth, FormationHeight));

    // 陣営の正面方向。論理座標で Player は上(-Y)へ、Enemy は下(+Y)へ向く。
    public static Vector2I Facing(Entities.Faction faction) =>
        faction == Entities.Faction.Player ? new Vector2I(0, -1) : new Vector2I(0, 1);

    // 描画用: 相手視点では盤面が180度回っている。ロジックは論理座標のみを
    // 扱い、表示する側だけがこの変換を通す。
    public static Vector2I ToViewOf(Vector2I logical, Entities.Faction viewer) =>
        viewer == Entities.Faction.Player
            ? logical
            : new Vector2I(Width - 1 - logical.X, Height - 1 - logical.Y);
}
