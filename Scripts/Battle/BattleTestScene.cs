using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Dungeon;
using MysteryDungeon.Entities;
using MysteryDungeon.Grid;
using MysteryDungeon.Species;
using MysteryDungeon.Turn;

namespace MysteryDungeon.Battle;

// 第1段階の検証用ハーネス。UI も選出フェーズも通信も無い状態で、4v4が
// サイクル/ターンの規則どおりに最後まで回ることを確かめる。
//
// 両陣営の行動は「手持ちの中で最も威力の高い技を、生存している敵のうち
// 最も近い1匹へ撃つ」という台本で与える。対戦本番は人が操作するので、
// この選択ロジックは検証以外では使わない。
public partial class BattleTestScene : Node2D
{
    // 構築段階で登録する6匹。ここから4匹を選出する。
    private static readonly string[] PlayerRoster = { "001", "004", "006", "009", "002", "010" };
    private static readonly string[] EnemyRoster = { "005", "008", "007", "003", "011", "012" };

    private const int MaxCycles = 40;

    private GridManager _grid;
    private TurnManager _turnManager;
    private FloorController _floor;
    private readonly BattleScheduler _sched = new();
    private readonly List<string> _doubleActs = new();

    public override void _Ready()
    {
        _grid = new GridManager { Name = "GridManager" };
        AddChild(_grid);

        _turnManager = new TurnManager { Name = "TurnManager" };
        AddChild(_turnManager);

        _floor = new FloorController { Name = "FloorController" };
        AddChild(_floor);
        _floor.InitializeArena(_grid, _turnManager);

        GD.Print($"[BattleTest] アリーナ {BattleBoard.Width}x{BattleBoard.Height} = "
                 + $"{BattleBoard.Width * BattleBoard.Height}マス, 全マス RoomId={BattleBoard.ArenaRoomId}");

        var playerTeam = BuildTeam(PlayerRoster);
        var enemyTeam = BuildTeam(EnemyRoster);
        VerifyTeamRules(playerTeam);

        // 選出フェーズの時間切れ経路（登録順の昇順で自動選出）で4匹に絞る。
        Deploy(Faction.Player, playerTeam.AutoSelect());
        Deploy(Faction.Enemy, enemyTeam.AutoSelect());

        VerifyArena();
        RunBattle();

        GetTree().Quit();
    }

    // 構築段階のチームを組む。技4つは learnset から先頭4件を採って
    // 「ユーザーが構築時に選んだ4技」に見立てる（本番はUIで選ぶ）。
    private static BattleTeam BuildTeam(string[] speciesIds)
    {
        var entries = new List<BattleEntry>();
        foreach (var sid in speciesIds)
        {
            var species = SpeciesDatabase.Instance?.Get(sid);
            var moves = species.Learnset.Select(l => l.MoveId).Distinct()
                               .Take(MoveManager.MaxMoves).ToList();
            entries.Add(new BattleEntry { SpeciesId = sid, MoveIds = moves });
        }
        return new BattleTeam(entries);
    }

    // 構築段階の制約が機械検証で効いていることを確認する。
    private void VerifyTeamRules(BattleTeam team)
    {
        var ok = team.Validate();
        GD.Print($"[検証] 正しい構築が通る: {(ok.Count == 0 ? "OK" : "NG " + string.Join(" / ", ok))}");

        var dupSpecies = new BattleTeam(team.Entries
            .Select((e, i) => i == 1 ? new BattleEntry { SpeciesId = team.Entries[0].SpeciesId, MoveIds = e.MoveIds } : e));
        GD.Print($"[検証] 同一種族の重複を弾く: "
                 + $"{(dupSpecies.Validate().Any(m => m.Contains("同一種族")) ? "OK" : "NG")}");

        var dupItem = new BattleTeam(team.Entries
            .Select(e => new BattleEntry { SpeciesId = e.SpeciesId, MoveIds = e.MoveIds, ItemId = "baked_berries" }));
        GD.Print($"[検証] 持ち物の重複を弾く: "
                 + $"{(dupItem.Validate().Any(m => m.Contains("持ち物の重複")) ? "OK" : "NG")}");

        var offLearnset = new BattleTeam(team.Entries.Select((e, i) => i == 0
            ? new BattleEntry { SpeciesId = e.SpeciesId, MoveIds = new List<string> { "megaton_self_destruct" } }
            : e));
        GD.Print($"[検証] learnset外の技を弾く: "
                 + $"{(offLearnset.Validate().Any(m => m.Contains("learnset外")) ? "OK" : "NG")}");

        var tooMany = new BattleTeam(team.Entries.Select((e, i) => i == 0
            ? new BattleEntry { SpeciesId = e.SpeciesId,
                                MoveIds = e.Species.Learnset.Select(l => l.MoveId).Distinct().Take(5).ToList() }
            : e));
        GD.Print($"[検証] 技5つ以上を弾く: "
                 + $"{(tooMany.Validate().Any(m => m.Contains("つまで")) ? "OK" : "NG")}");

        var sel = team.AutoSelect();
        GD.Print($"[検証] 時間切れ時は登録順の昇順で4匹: "
                 + $"{(sel.Count == 4 && sel.Select(e => e.SpeciesId).SequenceEqual(team.Entries.Take(4).Select(e => e.SpeciesId)) ? "OK" : "NG")} "
                 + $"({string.Join(",", sel.Select(e => e.Species.DisplayName))})");
        GD.Print($"[検証] 選出が3匹だと弾かれる: "
                 + $"{(team.ValidateSelection(sel.Take(3).ToList()).Count > 0 ? "OK" : "NG")}");
    }

    // 自陣6マス(縦2x横3)へ4匹を配置する。自由配置なので、検証では
    // 先頭から順に埋める。
    private void Deploy(Faction faction, List<BattleEntry> selection)
    {
        var area = BattleBoard.FormationArea(faction);
        var tiles = new List<Vector2I>();
        for (int y = area.Position.Y; y < area.Position.Y + area.Size.Y; y++)
            for (int x = area.Position.X; x < area.Position.X + area.Size.X; x++)
                tiles.Add(new Vector2I(x, y));

        for (int i = 0; i < selection.Count; i++)
        {
            var pal = new BattlePal
            {
                SpeciesId = selection[i].SpeciesId,
                Faction = faction,
                Entry = selection[i],   // 構築時に確定した4技と持ち物
            };
            AddChild(pal);                       // _Ready がここで走り種族/Lv50が確定
            pal.Grid = _grid;
            pal.FloorController = _floor;
            pal.PlaceAt(tiles[i]);
            pal.FaceDirection(BattleBoard.Facing(faction));

            _floor.AddArenaActor(pal);
            _sched.Register(pal);

            GD.Print($"[BattleTest] {faction} {pal.ActorName} BST{pal.Bst} "
                     + $"Lv{pal.Stats.Level} HP{pal.Stats.MaxHp} 技{pal.Moves.Slots.Count} @{tiles[i]}");
        }
    }

    // 対戦盤で射程が意図どおりに解釈されるかを、実際の解決器で確認する。
    private void VerifyArena()
    {
        var user = _sched.Roster.First(e => e.Faction == Faction.Player);

        var roomBounds = _floor.GetRoomBoundsAt(user.GridPosition);
        bool roomOk = roomBounds.HasValue
                      && roomBounds.Value.Size.X == BattleBoard.Width
                      && roomBounds.Value.Size.Y == BattleBoard.Height;
        GD.Print($"[検証] Room射程が盤面全体を指す: {(roomOk ? "OK" : "NG")} ({roomBounds})");

        var roomTargets = TargetResolver.Resolve(MoveRange.Room, user, user.GridPosition, _grid, _floor);
        bool roomHitsEnemiesOnly = roomTargets.Count == 4 && roomTargets.All(t => t.Faction == Faction.Enemy);
        GD.Print($"[検証] Room射程が敵4匹のみに当たる: {(roomHitsEnemiesOnly ? "OK" : "NG")} "
                 + $"({roomTargets.Count}体 {string.Join(",", roomTargets.Select(t => t.ActorName))})");

        var areaTargets = TargetResolver.Resolve(MoveRange.Area, user, user.GridPosition, _grid, _floor);
        GD.Print($"[検証] Area射程は3x3で味方も巻き込む: {areaTargets.Count}体 "
                 + $"({string.Join(",", areaTargets.Select(t => $"{t.ActorName}/{t.Faction}"))})");

        VerifyTurnOrder();

        var headbutt = MoveDatabase.Get("MV_140");
        GD.Print($"[検証] ヘッドバットの優先度: {headbutt?.Priority} (期待 1)");
        var other = MoveDatabase.Get("MV_141");
        GD.Print($"[検証] 他技の優先度: {other?.Priority} (期待 0)");
    }

    // 行動順規則の機械検証。優先度 > 合計種族値(低いほうが先) の順で効くこと、
    // 優先度が種族値を上書きすることを、実際の Order() で確かめる。
    private void VerifyTurnOrder()
    {
        var low = _sched.Roster.OrderBy(BattleScheduler.Bst).First();    // タマコッコ BST180
        var high = _sched.Roster.OrderByDescending(BattleScheduler.Bst).First(); // ブシガエル BST265

        // 優先度が同じなら種族値が低いほうが先。
        var byBst = _sched.Order(new BattleScheduler.Commitment(high, null, 0),
                                 new BattleScheduler.Commitment(low, null, 0));
        GD.Print($"[検証] 同優先度なら種族値が低いほうが先: "
                 + $"{(byBst[0].Actor == low ? "OK" : "NG")} "
                 + $"({byBst[0].Actor.ActorName}(BST{BattleScheduler.Bst(byBst[0].Actor)}) が先)");

        // 優先度が高ければ種族値を無視して先。
        var byPriority = _sched.Order(new BattleScheduler.Commitment(high, null, 1),
                                      new BattleScheduler.Commitment(low, null, 0));
        GD.Print($"[検証] 優先度が種族値を上書きする: "
                 + $"{(byPriority[0].Actor == high ? "OK" : "NG")} "
                 + $"({byPriority[0].Actor.ActorName}(BST{BattleScheduler.Bst(byPriority[0].Actor)}, 優先度1) が先)");
    }

    private void RunBattle()
    {
        while (_sched.CycleNumber < MaxCycles && _sched.Winner() == null)
        {
            _sched.BeginCycle();
            var actedThisCycle = new List<Entity>();

            // 1サイクル = 生存している側の頭数ぶんのターン。
            while (!_sched.CycleComplete)
            {
                var a = Commit(Faction.Player);
                var b = Commit(Faction.Enemy);
                if (a.IsEmpty && b.IsEmpty) break;   // 両者とも出せる個体がいない

                // 「1サイクル中、各パルは1ターンだけ行動する」の不変条件。
                foreach (var c in new[] { a, b })
                {
                    if (c.IsEmpty) continue;
                    if (actedThisCycle.Contains(c.Actor))
                        _doubleActs.Add($"C{_sched.CycleNumber} {c.Actor.ActorName}");
                    actedThisCycle.Add(c.Actor);
                }

                _sched.ResolveTurn(a, b);
                if (_sched.Winner() != null) break;
            }

            _sched.EndCycle();
            if (_sched.Winner() != null) break;
        }

        GD.Print($"[検証] 1サイクル中に2回行動した個体: "
                 + $"{(_doubleActs.Count == 0 ? "なし OK" : "NG " + string.Join(",", _doubleActs))}");

        var w = _sched.Winner();
        GD.Print($"[BattleTest] 決着: {(w?.ToString() ?? "引き分け")} "
                 + $"(サイクル{_sched.CycleNumber} / 通算{_sched.TotalTurns}ターン)");
        foreach (var e in _sched.Roster)
            GD.Print($"   {e.Faction} {e.ActorName} {(e.IsAlive ? $"HP{e.Stats.CurrentHp}/{e.Stats.MaxHp}" : "戦闘不能")}");
    }

    // 台本AI: まだ動いていない1匹を選び、最も威力の高い技で最寄りの敵を撃つ。
    private BattleScheduler.Commitment Commit(Faction faction)
    {
        var actor = _sched.AvailableFor(faction).FirstOrDefault();
        if (actor == null) return default;

        var target = _sched.Roster
            .Where(e => e.Faction != faction && e.IsAlive)
            .OrderBy(e => (e.GridPosition - actor.GridPosition).LengthSquared())
            .FirstOrDefault();
        if (target == null) return default;

        var slot = actor.Moves.Slots
            .Where(s => s.CurrentPp > 0 && s.Data.Power > 0)
            .OrderByDescending(s => s.Data.Power)
            .FirstOrDefault() ?? actor.Moves.Slots.FirstOrDefault();
        if (slot == null) return default;

        // 相手のほうを向いてから撃つ（Line/TwoTile が向きを見るため）。
        var delta = target.GridPosition - actor.GridPosition;
        actor.FaceDirection(delta);

        var action = new AttackAction(actor, target, slot, _floor);
        return new BattleScheduler.Commitment(actor, action, slot.Data.Priority);
    }
}
