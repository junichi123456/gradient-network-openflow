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

        VerifyItems();
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

    // 対戦持ち物16種がデータから正しく読めているか。
    private void VerifyItems()
    {
        var held = ItemDatabase.AllIds().Select(ItemDatabase.Get)
                               .Where(i => i.Type == ItemType.BattleHeld).ToList();
        int consum = held.Count(i => i.ConsumedOnTrigger);
        bool allTyped = held.All(i => i.BattleEffect != BattleItemEffect.None);
        bool allDistinct = held.Select(i => i.BattleEffect).Distinct().Count() == held.Count;

        GD.Print($"[検証] 対戦持ち物が16種そろう: {(held.Count == 16 ? "OK" : "NG")} "
                 + $"(使い切り{consum} / 持続{held.Count - consum})");
        GD.Print($"[検証] 全アイテムに効果種別がついている: {(allTyped ? "OK" : "NG")}");
        GD.Print($"[検証] 効果が重複していない: {(allDistinct ? "OK" : "NG")}");

        VerifyItemEffects();

        // 持ち物を持たせた構築が通ること（重複しない限り）。
        var team = BuildTeam(PlayerRoster);
        var withItems = new BattleTeam(team.Entries.Select((e, i) => new BattleEntry
        {
            SpeciesId = e.SpeciesId, MoveIds = e.MoveIds, ItemId = held[i].Id,
        }));
        GD.Print($"[検証] 別々の持ち物を6匹に配れる: "
                 + $"{(withItems.Validate().Count == 0 ? "OK" : "NG " + string.Join("/", withItems.Validate()))}");
    }

    // 持ち物の効果が実際に戦闘で発火するかを、盤上の個体に持たせて確かめる。
    private void VerifyItemEffects()
    {
        var atk = _sched.Roster.First(e => e.Faction == Faction.Enemy);
        var def = _sched.Roster.First(e => e.Faction == Faction.Player);
        var ally = _sched.Roster.Last(e => e.Faction == Faction.Player);

        // ワイドウォード: Room技を完全無効。
        var roomMove = MoveDatabase.AllIds().Select(MoveDatabase.Get)
            .First(m => m.Range == MoveRange.Room && m.Power > 0 && m.Accuracy >= 100);
        def.HeldItemId = "wide_ward";
        int before = def.Stats.CurrentHp;
        new AttackAction(atk, def, MakeSlot(atk, roomMove), _floor).Execute(0);
        GD.Print($"[検証] ワイドウォードがRoom技を無効化: "
                 + $"{(def.Stats.CurrentHp == before ? "OK" : "NG")} ({roomMove.Name})");
        def.HeldItemId = null;

        // アイアンプレート: 物理被弾時に防御+30% → ダメージが減る。
        var phys = MoveDatabase.AllIds().Select(MoveDatabase.Get)
            .First(m => m.Category == MoveCategory.Physical && m.Range == MoveRange.Adjacent
                        && m.Accuracy >= 100 && m.Power >= 40 && m.RankEffects.Count == 0);
        // 実効威力に掛けているので、ダメージは表記どおりの倍率で動くはず。
        // 端数は最後に一度切り捨てられるので ±1 の誤差を許容する。
        int plain = MeasureHit(atk, def, phys, null);
        int warded = MeasureHit(atk, def, phys, "iron_plate");
        GD.Print($"[検証] アイアンプレートで被ダメがちょうど30%減: "
                 + $"{(System.Math.Abs(warded - plain * 0.70f) <= 1f ? "OK" : "NG")} "
                 + $"({plain} → {warded}, 期待{plain * 0.70f:F1})");

        var spec = MoveDatabase.AllIds().Select(MoveDatabase.Get)
            .First(m => m.Category == MoveCategory.Special && m.Range == MoveRange.Adjacent
                        && m.Accuracy >= 100 && m.Power >= 40 && m.RankEffects.Count == 0);
        int plainS = MeasureHit(atk, def, spec, null);
        int wardedS = MeasureHit(atk, def, spec, "mind_plate");
        GD.Print($"[検証] マインドプレートで被ダメがちょうど40%減: "
                 + $"{(System.Math.Abs(wardedS - plainS * 0.60f) <= 1f ? "OK" : "NG")} "
                 + $"({plainS} → {wardedS}, 期待{plainS * 0.60f:F1})");

        // パワーレンズ: 物理使用時に実効威力+25% → ダメージもちょうど+25%。
        atk.HeldItemId = "power_lens";
        int boosted = MeasureHit(atk, def, phys, null);
        atk.HeldItemId = null;
        GD.Print($"[検証] パワーレンズで与ダメがちょうど25%増: "
                 + $"{(System.Math.Abs(boosted - plain * 1.25f) <= 1f ? "OK" : "NG")} "
                 + $"({plain} → {boosted}, 期待{plain * 1.25f:F1})");

        atk.HeldItemId = "focus_lens";
        int boostedS = MeasureHit(atk, def, spec, null);
        atk.HeldItemId = null;
        GD.Print($"[検証] フォーカスレンズで与ダメがちょうど25%増: "
                 + $"{(System.Math.Abs(boostedS - plainS * 1.25f) <= 1f ? "OK" : "NG")} "
                 + $"({plainS} → {boostedS}, 期待{plainS * 1.25f:F1})");

        // ラストトニック: HPが33%以下になると最大HPの50%回復し、消費される。
        def.Stats.HealToFull();
        def.Stats.TakeDamage((int)(def.Stats.MaxHp * 0.70f));   // 残り30%
        def.HeldItemId = "guard_tonic_50";
        int lowHp = def.Stats.CurrentHp;
        new AttackAction(atk, def, MakeSlot(atk, phys), _floor).Execute(0);
        GD.Print($"[検証] ラストトニックが閾値で発動し消費される: "
                 + $"{(def.Stats.CurrentHp > lowHp && def.HeldItemConsumed ? "OK" : "NG")} "
                 + $"(HP{lowHp} → {def.Stats.CurrentHp}, 消費={def.HeldItemConsumed})");

        foreach (var e in _sched.Roster) { e.HeldItemId = null; e.Stats.HealToFull(); }
        GD.Print($"[検証] 検証後に全個体のHPと持ち物を戻した: "
                 + $"{(_sched.Roster.All(e => e.Stats.CurrentHp == e.Stats.MaxHp) ? "OK" : "NG")}");
    }

    // 同じ攻撃を繰り返し通して、急所が乗っていない素のダメージを測る。
    // 急所は乱数で 1.5 倍に跳ねるだけなので、最小値が非急所の値になる。
    // シード管理をしない方針なので、測定側でこう吸収する。
    private const int MeasureSamples = 16;

    private int MeasureHit(Entity atk, Entity def, MoveData move, string itemId)
    {
        int min = int.MaxValue;
        for (int i = 0; i < MeasureSamples; i++)
        {
            def.Stats.HealToFull();
            def.HeldItemId = itemId;
            int before = def.Stats.CurrentHp;
            new AttackAction(atk, def, MakeSlot(atk, move), _floor).Execute(0);
            min = System.Math.Min(min, before - def.Stats.CurrentHp);
            def.HeldItemId = null;
        }
        def.Stats.HealToFull();
        return min;
    }

    private static MoveSlot MakeSlot(Entity user, MoveData move) => new(move);

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
