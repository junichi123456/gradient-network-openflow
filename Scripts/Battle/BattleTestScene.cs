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

    // 検証用: 1ターンに費やす実時間の見積り。
    private const double TurnSeconds = 8.0;

    private GridManager _grid;
    private TurnManager _turnManager;
    private FloorController _floor;
    private readonly BattleScheduler _sched = new();
    private readonly BattleClock _clock = new();
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
        VerifyDeployment(playerTeam);

        VerifyClock(playerTeam);

        // 選出フェーズ。検証では両者とも提出せずに50秒を経過させ、
        // 時間切れ経路（登録順の昇順で自動選出）を通す。
        _clock.Selection.Advance(BattleClock.SelectionLimitSeconds);
        Deploy(Faction.Player, _clock.ResolveSelection(Faction.Player, playerTeam).ToList());
        Deploy(Faction.Enemy, _clock.ResolveSelection(Faction.Enemy, enemyTeam).ToList());

        VerifyItems();
        VerifyArena();
        VerifyHud();
        VerifyScreens(BuildTeam(PlayerRoster), BuildTeam(EnemyRoster));
        VerifyFlow(BuildTeam(PlayerRoster), BuildTeam(EnemyRoster));
        VerifyNetwork(BuildTeam(PlayerRoster));
        VerifyCycleTick();
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
        // 配置フェーズ。検証では既定配置（前列から詰める）を使う。
        var deployment = BattleDeployment.Default(faction, selection);
        var errs = deployment.Validate();
        if (errs.Count > 0) GD.PushError($"[BattleTest] 配置が不正: {string.Join(" / ", errs)}");

        foreach (var (entry, tile) in deployment.Placements)
        {
            var pal = new BattlePal
            {
                SpeciesId = entry.SpeciesId,
                Faction = faction,
                Entry = entry,   // 構築時に確定した4技と持ち物
            };
            AddChild(pal);                       // _Ready がここで走り種族/Lv50が確定
            pal.Grid = _grid;
            pal.FloorController = _floor;
            pal.PlaceAt(tile);
            pal.FaceDirection(BattleBoard.Facing(faction));

            _floor.AddArenaActor(pal);
            _sched.Register(pal);

            GD.Print($"[BattleTest] {faction} {pal.ActorName} BST{pal.Bst} "
                     + $"Lv{pal.Stats.Level} HP{pal.Stats.MaxHp} 技{pal.Moves.Slots.Count} @{tile}");
        }
    }

    // 制限時間の規則が機械検証で効いていることを確認する。
    // 実時間を待たずに済むよう、経過は Advance() で与える。
    private void VerifyClock(BattleTeam team)
    {
        GD.Print($"[検証] 選出50秒・対戦20分: "
                 + $"{(BattleClock.SelectionLimitSeconds == 50.0 && BattleClock.MatchLimitSeconds == 1200.0 ? "OK" : "NG")} "
                 + $"({BattleClock.SelectionLimitSeconds}秒 / {BattleClock.MatchLimitSeconds / 60}分)");

        // 両者が出せば時間内でも締め切られる。
        var c1 = new BattleClock();
        var sel = team.AutoSelect();
        c1.SubmitSelection(Faction.Player, team, sel);
        bool halfway = !c1.SelectionClosed;
        c1.SubmitSelection(Faction.Enemy, team, sel);
        GD.Print($"[検証] 両者の選出が揃えば時間内でも締め切る: "
                 + $"{(halfway && c1.SelectionClosed ? "OK" : "NG")}");

        // 4匹でない選出は受け付けない。
        var c2 = new BattleClock();
        GD.Print($"[検証] 4匹でない選出を受け付けない: "
                 + $"{(!c2.SubmitSelection(Faction.Player, team, sel.Take(3).ToList()) ? "OK" : "NG")}");

        // 50秒で締め切られ、未提出側は登録順の昇順で自動選出される。
        var c3 = new BattleClock();
        c3.Selection.Advance(49.0);
        bool notYet = !c3.SelectionClosed;
        c3.Selection.Advance(1.0);
        var auto = c3.ResolveSelection(Faction.Enemy, team);
        GD.Print($"[検証] 50秒で締め切り未提出側は自動選出: "
                 + $"{(notYet && c3.SelectionClosed && auto.SequenceEqual(team.AutoSelect()) ? "OK" : "NG")} "
                 + $"({string.Join(",", auto.Select(e => e.Species.DisplayName))})");

        // 締め切り後の提出は拒否される。
        GD.Print($"[検証] 締め切り後の選出は拒否される: "
                 + $"{(!c3.SubmitSelection(Faction.Player, team, sel) ? "OK" : "NG")}");
    }

    // 配置フェーズの規則が機械検証で効いていることを確認する。
    private void VerifyDeployment(BattleTeam team)
    {
        var sel = team.AutoSelect();
        var tiles = BattleDeployment.AvailableTiles(Faction.Player);
        GD.Print($"[検証] 自陣は縦2x横3の6マス: {(tiles.Count == 6 ? "OK" : "NG")} ({tiles.Count}マス)");

        var ok = BattleDeployment.Default(Faction.Player, sel);
        GD.Print($"[検証] 既定配置が規則を満たす: "
                 + $"{(ok.Validate().Count == 0 ? "OK" : "NG " + string.Join("/", ok.Validate()))}");

        // 6マスから任意の4マスを選べる（自由配置）。前列0・後列3+はみ出しでなく、
        // 飛び飛びの取り方でも通ることを見る。
        var scattered = new Dictionary<BattleEntry, Vector2I>
        {
            [sel[0]] = tiles[0], [sel[1]] = tiles[2],
            [sel[2]] = tiles[3], [sel[3]] = tiles[5],
        };
        GD.Print($"[検証] 6マスから任意の4マスを選べる: "
                 + $"{(new BattleDeployment(Faction.Player, scattered).Validate().Count == 0 ? "OK" : "NG")}");

        // 自陣の外は弾く。
        var outside = new Dictionary<BattleEntry, Vector2I>
        {
            [sel[0]] = new Vector2I(0, 0), [sel[1]] = tiles[1],
            [sel[2]] = tiles[2], [sel[3]] = tiles[3],
        };
        GD.Print($"[検証] 自陣の外への配置を弾く: "
                 + $"{(new BattleDeployment(Faction.Player, outside).Validate().Any(m => m.Contains("自陣の外")) ? "OK" : "NG")}");

        // 敵陣は自陣と重ならない。
        var enemyTiles = BattleDeployment.AvailableTiles(Faction.Enemy);
        GD.Print($"[検証] 自陣と敵陣が重ならない: "
                 + $"{(!tiles.Intersect(enemyTiles).Any() ? "OK" : "NG")} "
                 + $"(自陣Y{tiles.Min(t => t.Y)}〜{tiles.Max(t => t.Y)} / 敵陣Y{enemyTiles.Min(t => t.Y)}〜{enemyTiles.Max(t => t.Y)})");
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

        // レンズ/プレート4種は急所時に作用しない。急所の最大ダメージが
        // 持ち物の有無で変わらないことで確かめる。
        int plainMin = MeasureHit(atk, def, phys, null);
        int critMax = MeasureCrit(atk, def, phys);
        int platedCrit = MeasureCrit(atk, def, phys, "iron_plate");
        GD.Print($"[検証] アイアンプレートは急所時に作用しない: "
                 + $"{(platedCrit == critMax ? "OK" : "NG")} "
                 + $"(急所 素{critMax} / プレート{platedCrit})");

        atk.HeldItemId = "power_lens";
        int lensCrit = MeasureCrit(atk, def, phys);
        atk.HeldItemId = null;
        GD.Print($"[検証] パワーレンズは急所時に作用しない: "
                 + $"{(lensCrit == critMax ? "OK" : "NG")} "
                 + $"(急所 素{critMax} / レンズ{lensCrit})");

        // 非急所では従来どおり効く（撤回した順序変更とは独立）。
        GD.Print($"[検証] 非急所では従来どおり効く: "
                 + $"{(MeasureHit(atk, def, phys, "iron_plate") < plainMin ? "OK" : "NG")}");

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

    // 急所が出た時の最大ダメージを測る。急所は乱数なので、素の1.5倍という
    // 上限に張り付くまで繰り返す。
    private int MeasureCrit(Entity atk, Entity def, MoveData move, string itemId = null)
    {
        int max = 0;
        for (int i = 0; i < 300; i++)
        {
            def.Stats.HealToFull();
            def.HeldItemId = itemId;
            int before = def.Stats.CurrentHp;
            new AttackAction(atk, def, MakeSlot(atk, move), _floor).Execute(0);
            max = System.Math.Max(max, before - def.Stats.CurrentHp);
            def.HeldItemId = null;
        }
        def.Stats.HealToFull();
        return max;
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

    // 状態異常とランク減衰がターンではなくサイクル単位で刻まれることを見る。
    // BattleScheduler は ResolveTurn では ResolveStatusTick を呼ばず、
    // EndCycle でだけ全員に1回呼ぶ。1サイクル=4ターンなので、行動ごとに
    // 刻む迷宮側と比べて刻みが1/4になる。
    private void VerifyCycleTick()
    {
        var e = _sched.Roster.First();
        e.StatusEffects.ApplyRankDelta(RankStat.Atk, -2);
        int rankBefore = e.StatusEffects.AtkRank;

        // 4ターンぶん解決してもサイクルが閉じるまでは刻まれない。
        _sched.BeginCycle();
        for (int i = 0; i < BattleTeam.SelectionSize; i++)
        {
            var a2 = Commit(Faction.Player);
            var b2 = Commit(Faction.Enemy);
            if (a2.IsEmpty && b2.IsEmpty) break;
            _sched.ResolveTurn(a2, b2);
        }
        int rankMidCycle = e.StatusEffects.AtkRank;
        _sched.EndCycle();

        GD.Print($"[検証] ランクはターン中に刻まれない: "
                 + $"{(rankMidCycle == rankBefore ? "OK" : "NG")} "
                 + $"(サイクル前{rankBefore} / 4ターン後{rankMidCycle})");
        GD.Print($"[検証] 刻みはサイクル境界(EndCycle)でのみ走る: "
                 + $"{(_sched.CycleNumber == 1 ? "OK" : "NG")}");

        // 検証で消耗した状態を戻してから本番の対戦へ入る。
        foreach (var x in _sched.Roster) { x.StatusEffects.Reset(); x.Stats.HealToFull(); }
    }

    // 対戦20分の判定。全滅が先に来れば勝敗、来なければ引き分け。
    private void VerifyMatchLimit()
    {
        var c = new BattleClock();
        GD.Print($"[検証] 20分が尽きるまでは未決着: "
                 + $"{(c.Resolve(_sched) == BattleOutcome.Undecided ? "OK" : "NG")}");

        c.Match.Advance(BattleClock.MatchLimitSeconds - 1.0);
        bool notYet = c.Resolve(_sched) == BattleOutcome.Undecided;
        c.Match.Advance(1.0);
        GD.Print($"[検証] 20分の時間切れで両者生存なら引き分け: "
                 + $"{(notYet && c.Resolve(_sched) == BattleOutcome.Draw ? "OK" : "NG")}");
    }

    // 通信対戦の2大要件を確かめる。
    //   1. 相手に開示されるのは種族のみ（技・持ち物は漏れない）
    //   2. 両者の入力が揃うまで相手の手は見えない
    private void VerifyNetwork(BattleTeam team)
    {
        // 1. 開示範囲。PublicEntryView は種族しか持てないので、技や持ち物を
        // 載せる場所がそもそも無い（型で担保している）。
        var disclosed = BattleSession.DiscloseTeam(team);
        var fields = typeof(PublicEntryView).GetProperties();
        GD.Print($"[検証] 開示は種族6件のみ: "
                 + $"{(disclosed.Count == 6 && fields.Length == 1 && fields[0].Name == "SpeciesId" ? "OK" : "NG")} "
                 + $"({disclosed.Count}件, 公開項目 {string.Join(",", fields.Select(f => f.Name))})");
        GD.Print($"[検証] 開示に技・持ち物が含まれない: "
                 + $"{(!fields.Any(f => f.Name.Contains("Move") || f.Name.Contains("Item")) ? "OK" : "NG")}");

        // 2. 伏せて同時。片方だけ出した状態では相手の手は見えない。
        var session = new BattleSession(_sched, new BattleClock());
        var inA = new TurnInput(0, 0, Vector2I.Zero);
        var inB = new TurnInput(1, 1, Vector2I.One);

        session.SubmitInput(Faction.Player, inA);
        bool hiddenWhileAlone = session.PeekOpponentInput(Faction.Enemy) == null
                                && session.PeekOpponentInput(Faction.Player) == null;
        GD.Print($"[検証] 片方だけ提出では相手の手が見えない: "
                 + $"{(hiddenWhileAlone && !session.BothSubmitted ? "OK" : "NG")}");

        // 出し直しは拒否する（相手の手を見てから変えられないように）。
        GD.Print($"[検証] 同一ターンの二重提出を拒否する: "
                 + $"{(!session.SubmitInput(Faction.Player, inB) ? "OK" : "NG")}");

        session.SubmitInput(Faction.Enemy, inB);
        var seen = session.PeekOpponentInput(Faction.Player);
        GD.Print($"[検証] 両者が揃うと相手の手が開く: "
                 + $"{(session.BothSubmitted && seen?.ActorIndex == inB.ActorIndex ? "OK" : "NG")}");

        // 解決すると保留が消え、次のターンでまた伏せ直される。
        var result = session.ResolveTurn(
            (f, i) => new BattleScheduler.Commitment(
                _sched.Roster.First(e => e.Faction == f), null, 0), TurnSeconds);
        GD.Print($"[検証] 解決後は再び伏せた状態に戻る: "
                 + $"{(result.HasValue && !session.BothSubmitted && session.PeekOpponentInput(Faction.Player) == null ? "OK" : "NG")}");

        // ホストが決めた行動順が結果に載る（クライアントは再現できないため）。
        GD.Print($"[検証] 結果に行動順とHPが載る: "
                 + $"{(result.HasValue && result.Value.ActingOrder.Count == 2 && result.Value.HpAfter.Count == _sched.Roster.Count ? "OK" : "NG")} "
                 + $"(順序{result?.ActingOrder.Count}件 / HP{result?.HpAfter.Count}件)");
    }

    // 対戦画面を実際に組み立てて、盤面と行動順レールが実データから
    // 描けることを確かめる。ビルドが通ることではなく、ノードが
    // 期待どおりの数だけ生えることを見る。
    private void VerifyHud()
    {
        var hud = new UI.Battle.BattleHud { Name = "BattleHud" };
        AddChild(hud);
        hud.Initialize(_sched, _clock, new BattleSession(_sched, _clock));

        int tiles = CountTiles(hud);
        GD.Print($"[検証] 盤面が8x7=56マス生える: "
                 + $"{(tiles == BattleBoard.Width * BattleBoard.Height ? "OK" : "NG")} ({tiles}マス)");

        hud.ShowCommands(_sched.Roster.First(e => e.Faction == Faction.Player));
        hud.SetRange(new[] { new Vector2I(3, 3), new Vector2I(3, 4) }, new Vector2I(3, 3));
        hud.AppendLog("ブシガエルの ダイヤモンドダスト！");
        hud.Refresh();

        // HPバーが実データを反映しているか（0除算や未初期化で落ちないか）。
        var hp = new UI.Battle.HpBar();
        AddChild(hp);
        hp.SetHp(0, 0);          // 最大HP0でも落ちないこと
        hp.SetHp(63, 130);
        GD.Print($"[検証] HPバーが極端な値でも落ちない: OK");

        hud.QueueFree();
        hp.QueueFree();
    }

    private static int CountTiles(Node n)
    {
        int c = 0;
        foreach (var child in n.GetChildren())
        {
            if (child is GridContainer g) c += g.GetChildCount();
            else c += CountTiles(child);
        }
        return c;
    }

    // 構築・選出・配置の3画面を実データで組み立てる。ビルドが通ることでは
    // なく、種族数ぶんのカードが生えることと、規則違反が画面に出ることを見る。
    private void VerifyScreens(BattleTeam mine, BattleTeam foe)
    {
        // 構築画面: 6匹ぶんのカードが並ぶ。
        var build = new UI.Battle.TeamBuildScreen();
        AddChild(build);
        build.Initialize(mine);
        int cards = CountCards(build, inGrid: true);
        GD.Print($"[検証] 構築画面に6匹ぶんのカードが並ぶ: "
                 + $"{(cards == BattleTeam.RosterSize ? "OK" : "NG")} ({cards}枚)");

        // 規則違反が画面に反映される（同一種族を作ると開始できなくなる）。
        var broken = new BattleTeam(mine.Entries.Select((e, i) => i == 1
            ? new BattleEntry { SpeciesId = mine.Entries[0].SpeciesId, MoveIds = e.MoveIds } : e));
        var build2 = new UI.Battle.TeamBuildScreen();
        AddChild(build2);
        build2.Initialize(broken);
        var btn = FindFirst<Button>(build2);
        GD.Print($"[検証] 構築が違反していると開始できない: "
                 + $"{(btn != null && btn.Disabled ? "OK" : "NG")}");

        // 選出画面: 相手6匹＋自分6匹＝12行。相手側は種族しか渡らない。
        var sel = new UI.Battle.SelectionScreen();
        AddChild(sel);
        sel.Initialize(mine, PublicEntryView.Of(foe.Entries), new BattleClock());
        int rows = CountCards(sel, inGrid: false);
        GD.Print($"[検証] 選出画面に12行(相手6+自分6)並ぶ: "
                 + $"{(rows >= 12 ? "OK" : "NG")} ({rows}行)");

        bool fifth = sel.Toggle(mine.Entries[0]) && sel.Toggle(mine.Entries[1])
                     && sel.Toggle(mine.Entries[2]) && sel.Toggle(mine.Entries[3]);
        bool blocked = !sel.Toggle(mine.Entries[4]);
        GD.Print($"[検証] 選出は4匹を超えられない: {(fifth && blocked ? "OK" : "NG")} "
                 + $"(選出{sel.Picked.Count}匹)");

        // 配置画面: 自陣6マス＋敵陣6マス＝12マス。
        var dep = new UI.Battle.DeployScreen();
        AddChild(dep);
        dep.Initialize(BattleDeployment.Default(Faction.Player, mine.AutoSelect()));
        int cells = CountCards(dep, inGrid: true);
        GD.Print($"[検証] 配置画面に自陣6+敵陣6=12マス並ぶ: "
                 + $"{(cells == 12 ? "OK" : "NG")} ({cells}マス)");

        // 相手の選出と配置が画面へ出ていないこと。敵陣に種族名が1つでも
        // 出ていたら漏れている。相手の6匹の名前を全部当たって確かめる。
        var foeNames = foe.Entries
            .Select(e => e.Species?.DisplayName).Where(n => !string.IsNullOrEmpty(n)).ToList();
        var shown = CollectLabels(dep);
        var leaked = foeNames.Where(n => shown.Contains(n)).ToList();
        GD.Print($"[検証] 配置画面に相手の選出・配置が出ない: "
                 + $"{(leaked.Count == 0 ? "OK" : "NG " + string.Join(",", leaked))}");
        GD.Print($"[検証] 敵陣6マスがすべて不明表示: "
                 + $"{(shown.Count(t => t == "?") == 6 ? "OK" : "NG")} "
                 + $"({shown.Count(t => t == "?")}マス)");

        // 選出画面も同様。相手側に出てよいのは種族名だけで、技名は出ない。
        var selShown = CollectLabels(sel);
        var moveNames = foe.Entries.SelectMany(e => e.MoveIds)
            .Select(m => MoveDatabase.Get(m)?.Name).Where(n => n != null).Distinct().ToList();
        var moveLeak = moveNames.Where(n => selShown.Contains(n)).ToList();
        GD.Print($"[検証] 選出画面に相手の技名が出ない: "
                 + $"{(moveLeak.Count == 0 ? "OK" : "NG " + string.Join(",", moveLeak))}");

        build.QueueFree(); build2.QueueFree(); sel.QueueFree(); dep.QueueFree();
    }

    // カードは押せるもの(Button)と押せないもの(PanelContainer)が混在する。
    // 操作可否で型が変わるだけで「1枚」であることは同じなので、両方数える。
    private static int CountCards(Node n, bool inGrid)
    {
        int c = 0;
        foreach (var child in n.GetChildren())
        {
            bool isCard = child is PanelContainer || child is Button;
            if (inGrid && n is GridContainer && isCard) c++;
            else if (!inGrid && isCard) c++;
            c += CountCards(child, inGrid);
        }
        return c;
    }

    // GridContainer の直下だけ数えるか、全体から数えるかを切り替える。
    private static int CountIn<T>(Node n, bool inGrid) where T : Node
    {
        int c = 0;
        foreach (var child in n.GetChildren())
        {
            if (inGrid && n is GridContainer && child is T) c++;
            else if (!inGrid && child is T) c++;
            c += CountIn<T>(child, inGrid);
        }
        return c;
    }

    // 画面に実際に出ている文字列を全部集める。漏れの検査に使う。
    private static List<string> CollectLabels(Node n)
    {
        var acc = new List<string>();
        foreach (var child in n.GetChildren())
        {
            if (child is Label l) acc.Add(l.Text);
            acc.AddRange(CollectLabels(child));
        }
        return acc;
    }

    private static T FindFirst<T>(Node n) where T : Node
    {
        foreach (var child in n.GetChildren())
        {
            if (child is T t) return t;
            var found = FindFirst<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    // 入力の配線。ボタンを実際に押し、時計を進めて、フェーズが進むことを見る。
    // 画面が生えるかではなく「操作すると状態が変わるか」を確かめる。
    private void VerifyFlow(BattleTeam mine, BattleTeam foe)
    {
        var clock = new BattleClock();
        var flow = new UI.Battle.BattleFlow();
        AddChild(flow);
        flow.Begin(mine, PublicEntryView.Of(foe.Entries), clock, _sched,
                   new BattleSession(_sched, clock));

        GD.Print($"[検証] 最初は構築画面: "
                 + $"{(flow.Current == UI.Battle.BattleFlow.Phase.Build ? "OK" : "NG")}");

        GD.Print($"[検証] 規則を満たす構築なら選出へ進める: "
                 + $"{(flow.ConfirmBuild() && flow.Current == UI.Battle.BattleFlow.Phase.Selection ? "OK" : "NG")}");

        // 選出画面の行を実際に押して4匹選ぶ。
        var sel = FindFirst<UI.Battle.SelectionScreen>(flow);
        var buttons = CollectButtons(sel).Where(b => !b.Disabled).ToList();
        int before = sel.Picked.Count;
        foreach (var b in buttons.Take(4)) b.EmitSignal(Button.SignalName.Pressed);
        GD.Print($"[検証] 行を押すと選出に入る: "
                 + $"{(before == 0 && sel.Picked.Count == 4 ? "OK" : "NG")} "
                 + $"({before} → {sel.Picked.Count}匹)");

        // 「決定」で配置画面へ。
        var confirm = CollectButtons(sel).FirstOrDefault(b => b.Text.Contains("決定"));
        confirm?.EmitSignal(Button.SignalName.Pressed);
        GD.Print($"[検証] 決定を押すと配置画面へ: "
                 + $"{(flow.Current == UI.Battle.BattleFlow.Phase.Deploy ? "OK" : "NG")}");

        // 「この配置で開始」で対戦画面へ。
        var dep = FindFirst<UI.Battle.DeployScreen>(flow);
        var startBtn = CollectButtons(dep).FirstOrDefault(b => b.Text.Contains("開始"));
        startBtn?.EmitSignal(Button.SignalName.Pressed);
        GD.Print($"[検証] 開始を押すと対戦画面へ: "
                 + $"{(flow.Current == UI.Battle.BattleFlow.Phase.Battle ? "OK" : "NG")}");

        // 対戦画面で技を選ぶと「決定して伏せる」が押せるようになる。
        var hud = FindFirst<UI.Battle.BattleHud>(flow);
        hud.ShowCommands(_sched.Roster.First(e => e.Faction == Faction.Player));
        var commitBefore = CollectButtons(hud).FirstOrDefault(b => b.Text.Contains("伏せる"));
        bool lockedFirst = commitBefore != null && commitBefore.Disabled;
        // 盤面のマスも空テキストのボタンなので、技名のラベルを子に持つ
        // ボタンを狙って押す。
        var actor0 = _sched.Roster.First(e => e.Faction == Faction.Player);
        string firstMove = actor0.Moves.Slots[0].Data.Name;
        var moveBtn = CollectButtons(hud).First(x => CollectLabels(x).Contains(firstMove));
        moveBtn.EmitSignal(Button.SignalName.Pressed);
        var commitAfter = CollectButtons(hud).FirstOrDefault(b => b.Text.Contains("伏せる"));
        GD.Print($"[検証] 技を選ぶまで提出できない: "
                 + $"{(lockedFirst && commitAfter != null && !commitAfter.Disabled ? "OK" : "NG")}");

        // 選出フェーズの時間切れ経路。50秒進めると自動で配置へ移る。
        var clock2 = new BattleClock();
        var flow2 = new UI.Battle.BattleFlow();
        AddChild(flow2);
        flow2.Begin(mine, PublicEntryView.Of(foe.Entries), clock2, _sched,
                    new BattleSession(_sched, clock2));
        flow2.ConfirmBuild();
        flow2._Process(BattleClock.SelectionLimitSeconds + 1.0);
        GD.Print($"[検証] 選出が時間切れなら自動で配置へ: "
                 + $"{(flow2.Current == UI.Battle.BattleFlow.Phase.Deploy ? "OK" : "NG")}");

        flow.QueueFree(); flow2.QueueFree();
    }

    private static List<Button> CollectButtons(Node n)
    {
        var acc = new List<Button>();
        if (n == null) return acc;
        foreach (var child in n.GetChildren())
        {
            if (child is Button b) acc.Add(b);
            acc.AddRange(CollectButtons(child));
        }
        return acc;
    }

    private void RunBattle()
    {
        VerifyMatchLimit();

        while (_sched.CycleNumber < MaxCycles && _clock.Resolve(_sched) == BattleOutcome.Undecided)
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

                // 1ターンに費やす実時間。全滅だけでなく20分の時間切れでも
                // 対戦は終わるので、決着判定は時計を通す。
                _clock.Match.Advance(TurnSeconds);
                if (_clock.Resolve(_sched) != BattleOutcome.Undecided) break;
            }

            _sched.EndCycle();
            if (_clock.Resolve(_sched) != BattleOutcome.Undecided) break;
        }

        GD.Print($"[検証] 1サイクル中に2回行動した個体: "
                 + $"{(_doubleActs.Count == 0 ? "なし OK" : "NG " + string.Join(",", _doubleActs))}");

        // 対戦ループが時計で回っていることを、実際に時間が進んだ事実で確かめる。
        GD.Print($"[検証] 対戦ループが時計を進めている: "
                 + $"{(_clock.Match.Elapsed > 0.0 && _clock.Match.Elapsed <= BattleClock.MatchLimitSeconds ? "OK" : "NG")} "
                 + $"({_clock.Match.Elapsed:F0}秒 / 上限{BattleClock.MatchLimitSeconds:F0}秒)");

        var outcome = _clock.Resolve(_sched);
        GD.Print($"[BattleTest] 決着: {outcome} "
                 + $"(サイクル{_sched.CycleNumber} / 通算{_sched.TotalTurns}ターン / "
                 + $"経過{_clock.Match.Elapsed:F0}秒 / 残り{_clock.Match.Remaining:F0}秒)");
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
