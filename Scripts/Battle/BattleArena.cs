using System.Collections.Generic;
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
}
