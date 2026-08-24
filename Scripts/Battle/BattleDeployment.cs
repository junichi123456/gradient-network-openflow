using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Entities;

namespace MysteryDungeon.Battle;

// 配置フェーズ。選出した4匹を自陣の6マス（縦2×横3）へ自由に置く。
// 6マスのうちどの4マスを使うかは自由で、前列/後列の枚数にも縛りはない。
//
// 座標はすべて論理座標（BattleBoard 参照）。相手視点の180度回転は描画側の
// 仕事なので、ここには出てこない。
public sealed class BattleDeployment
{
    // 種族 → 置くマス。選出した4匹ぶん。
    private readonly Dictionary<BattleEntry, Vector2I> _placements;

    public IReadOnlyDictionary<BattleEntry, Vector2I> Placements => _placements;
    public Faction Faction { get; }

    public BattleDeployment(Faction faction, IReadOnlyDictionary<BattleEntry, Vector2I> placements)
    {
        Faction = faction;
        _placements = new Dictionary<BattleEntry, Vector2I>(placements);
    }

    // 1匹を指定マスへ動かす。配置画面の唯一の操作。
    //
    // 置き先に別の1匹がいた場合は入れ替える。「どかしてから置く」を2手に
    // 分けると、片方が盤外にいる中途半端な状態が生まれてしまう。入れ替えなら
    // どの瞬間も4匹が盤上に揃っているので、時間切れがいつ来ても配置は有効。
    public bool Place(BattleEntry entry, Vector2I tile)
    {
        if (entry == null) return false;
        if (!BattleBoard.FormationArea(Faction).HasPoint(tile)) return false;

        var occupant = _placements.FirstOrDefault(p => p.Value == tile).Key;
        if (occupant == entry) return false;                  // 同じマス = 何もしない

        bool had = _placements.TryGetValue(entry, out var from);
        if (occupant != null)
        {
            if (had) _placements[occupant] = from;            // 入れ替え
            else _placements.Remove(occupant);                // 押し出し（未配置と交代）
        }
        _placements[entry] = tile;
        return true;
    }

    public BattleEntry At(Vector2I tile) =>
        _placements.FirstOrDefault(p => p.Value == tile).Key;

    // 配置が規則を満たしているか。満たしていれば空のリストを返す。
    public List<string> Validate()
    {
        var errors = new List<string>();
        var area = BattleBoard.FormationArea(Faction);

        if (Placements.Count != BattleTeam.SelectionSize)
            errors.Add($"配置は{BattleTeam.SelectionSize}匹ちょうど必要（現在{Placements.Count}匹）");

        foreach (var (entry, tile) in Placements)
        {
            string who = entry.Species?.DisplayName ?? entry.SpeciesId;
            if (!area.HasPoint(tile))
                errors.Add($"{who}: 自陣の外に置かれている {tile}（自陣 {area.Position}〜{area.End - Vector2I.One}）");
        }

        // 同じマスに2匹は置けない。
        foreach (var g in Placements.GroupBy(p => p.Value).Where(g => g.Count() > 1))
            errors.Add($"同じマスに{g.Count()}匹が重なっている {g.Key}");

        return errors;
    }

    // 自陣で使えるマスの一覧（6マス）。UI がここから選ばせる想定。
    public static List<Vector2I> AvailableTiles(Faction faction)
    {
        var area = BattleBoard.FormationArea(faction);
        var tiles = new List<Vector2I>();
        for (int y = area.Position.Y; y < area.End.Y; y++)
            for (int x = area.Position.X; x < area.End.X; x++)
                tiles.Add(new Vector2I(x, y));
        return tiles;
    }

    // 配置を決めずに対戦へ進んだ場合の既定配置。選出順に、相手と対面する
    // 前列から詰める。時間切れ時の挙動として選出の自動化と対になる。
    public static BattleDeployment Default(Faction faction, IReadOnlyList<BattleEntry> selection)
    {
        var tiles = AvailableTiles(faction);

        // 前列（相手に近い側）から埋める。論理座標では Player が下(Yが大)に
        // いるので、Player は Y が小さいマスほど相手に近い。
        tiles.Sort((a, b) => faction == Faction.Player
            ? a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X)
            : a.Y != b.Y ? b.Y.CompareTo(a.Y) : a.X.CompareTo(b.X));

        var map = new Dictionary<BattleEntry, Vector2I>();
        for (int i = 0; i < selection.Count && i < tiles.Count; i++)
            map[selection[i]] = tiles[i];
        return new BattleDeployment(faction, map);
    }
}
