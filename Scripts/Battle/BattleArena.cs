using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Dungeon;
using MysteryDungeon.Entities;
using MysteryDungeon.Grid;
using MysteryDungeon.Turn;

namespace MysteryDungeon.Battle;

// 対戦盤の実体（8x7の1部屋）と、そこへパルを立てる手続き。
//
// **立てるのは選出と配置が決まったあと。** 以前は画面を出す前に既定配置で
// 立てていたので、選出画面で別の4匹を選んでも盤面は最初の4匹のままだった。
// 選出→配置→生成の順にすれば、その食い違いは起こらない。
public sealed class BattleArena
{
    private readonly Node _host;

    public GridManager Grid { get; }
    public TurnManager Turns { get; }
    public FloorController Floor { get; }

    public BattleArena(Node host)
    {
        _host = host;

        Grid = new GridManager { Name = "GridManager" };
        host.AddChild(Grid);

        Turns = new TurnManager { Name = "TurnManager" };
        host.AddChild(Turns);

        Floor = new FloorController { Name = "FloorController" };
        host.AddChild(Floor);
        Floor.InitializeArena(Grid, Turns);
    }

    // 配置どおりに立てる。盤面の描画はHUDが持つので実体は隠す
    // （実体は座標・HP・技・持ち物の入れ物として要る）。
    public List<BattlePal> Spawn(BattleDeployment deployment, BattleScheduler scheduler)
    {
        var spawned = new List<BattlePal>();
        if (deployment == null) return spawned;

        foreach (var (entry, tile) in deployment.Placements)
        {
            var pal = new BattlePal
            {
                SpeciesId = entry.SpeciesId,
                Faction = deployment.Faction,
                Entry = entry,          // 構築時に確定した4技と持ち物
            };
            _host.AddChild(pal);        // _Ready がここで走り種族/Lv50が確定
            pal.Grid = Grid;
            pal.FloorController = Floor;
            pal.PlaceAt(tile);
            pal.FaceDirection(BattleBoard.Facing(deployment.Faction));
            pal.Visible = false;

            Floor.AddArenaActor(pal);
            scheduler.Register(pal);
            spawned.Add(pal);
        }
        return spawned;
    }

    // 対戦開始時、weather_on_entry を持つ特性の天候を発動させる。
    // **両陣営が立ち終わってから**呼ぶ（片側だけ立った時点で回すと、
    // 相手がまだ居ないので発動順が決まらない）。
    //
    // 順序は「合計種族値が高い側から先、低い側が最後」。迷宮側の規則
    // （FloorController.ApplyTraitWeather）と同じく**最後に発動した天候が
    // 残る**ので、これは低種族値側に天候の主導権を渡すという意味になる
    // ——行動順（種族値が低いほうが先に動く §行動順）と同じ「非力な側への
    // 補償」を、天候にも同じ向きで効かせるための逆順。高種族値側が天候を
    // 取りにいっても、相手が同じ手を持っていれば上書きされる。
    //
    // 同じ天候を既に張っている holder は何もしない（FloorController 側の
    // 「すでにその天候になっている場合は発動しない」がそのまま効く）。
    public void ApplyEntryWeather(BattleScheduler scheduler)
    {
        if (scheduler == null) return;

        var sides = scheduler.Roster
            .GroupBy(e => e.Faction)
            .Select(g => (Faction: g.Key,
                          Bst: g.Sum(BattleScheduler.Bst),
                          Members: g.ToList()))
            .OrderByDescending(x => x.Bst)
            .ThenBy(x => (int)x.Faction)      // 同値なら陣営順で固定（再現性のため）
            .ToList();

        foreach (var side in sides)
            foreach (var actor in side.Members)
                Floor.ApplyWeatherOnEntry(actor);
    }
}
