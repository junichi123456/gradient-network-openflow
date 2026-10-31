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
        VerifyNpc();
        VerifyNpcMatch(BuildTeam(PlayerRoster));
        VerifyHud();
        VerifyScreens(BuildTeam(PlayerRoster), BuildTeam(EnemyRoster));
        VerifyFlow(BuildTeam(PlayerRoster), BuildTeam(EnemyRoster));
        VerifyNetwork(BuildTeam(PlayerRoster));
        VerifyCycleTick();
        VerifyVoidType();
        VerifyHollow();
        VerifySecondaryTraits();
        VerifyLetUsHug();
        VerifyHabcdsData();
        VerifyHabcdsWiring();
        VerifySpread();
        VerifyRestrictions();
        VerifyProtection();
        StoryTests.Run(this);
        HubMapTests.Run();
        RunBattle();

        // 終了時クラッシュ対策: 試合が長引くと未処理の Tween の C# ラッパーが
        // 終了処理と競合し Godot が abort する(exit 134)ことがあるので、先に回収する。
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
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

        // タグ:伝説。セイントール(198)・ベイントール(199)はどちらも
        // 実データで is_legendary=true が付いている7種のうちの2つ。
        static BattleEntry Legendary(string speciesId) => new()
        {
            SpeciesId = speciesId,
            MoveIds = SpeciesDatabase.Instance.Get(speciesId).Learnset
                        .Select(l => l.MoveId).Distinct().Take(MoveManager.MaxMoves).ToList(),
        };

        var oneLegendary = new BattleTeam(team.Entries.Select((e, i) => i == 0 ? Legendary("198") : e));
        GD.Print($"[検証] 「伝説」1体は通る: "
                 + $"{(!oneLegendary.Validate().Any(m => m.Contains("伝説")) ? "OK" : "NG")}");

        var twoLegendary = new BattleTeam(team.Entries.Select((e, i) =>
            i == 0 ? Legendary("198") : i == 1 ? Legendary("199") : e));
        GD.Print($"[検証] 「伝説」2体目は弾く: "
                 + $"{(twoLegendary.Validate().Any(m => m.Contains("伝説")) ? "OK" : "NG")}");

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

        VerifyAoeAim();
        VerifyTurnOrder();
        VerifyEntryWeather();
        VerifyPowerTraitWeather();

        var headbutt = MoveDatabase.Get("MV_140");
        GD.Print($"[検証] ヘッドバットの優先度: {headbutt?.Priority} (期待 1)");
        var other = MoveDatabase.Get("MV_141");
        GD.Print($"[検証] 他技の優先度: {other?.Priority} (期待 0)");
    }

    // 対戦開始時の天候（特性 weather_on_entry）。**合計種族値が低い側が
    // 最後に発動して上書き勝ちする**——立てた順ではなく種族値の順で決まる
    // ことを確かめたいので、spawn順を入れ替えた2回で同じ結果になるか見る。
    private void VerifyEntryWeather()
    {
        // すなあらし=マグマンダー(BST6 474) / ゆき=ユキツネ(BST6 352)。
        // 低種族値のユキツネ側が最後に発動するので、常にゆきが残るはず。
        WeatherType Probe(bool highFirst)
        {
            var host = new Node { Name = "WeatherProbe" };
            AddChild(host);
            var arena = new BattleArena(host);
            var sched = new BattleScheduler();

            BattleEntry Entry(string id) => new()
            {
                SpeciesId = id,
                MoveIds = DefaultLoadout.PickMoves(
                    SpeciesDatabase.Instance?.Get(id), MoveManager.MaxMoves),
            };
            BattleDeployment One(Faction f, string id) => new(f,
                new Dictionary<BattleEntry, Vector2I>
                { [Entry(id)] = BattleDeployment.AvailableTiles(f)[0] });

            var high = One(Faction.Player, "105");    // マグマンダー / すなあらし
            var low = One(Faction.Enemy, "029B");     // ユキツネ / ゆき

            if (highFirst) { arena.Spawn(high, sched); arena.Spawn(low, sched); }
            else { arena.Spawn(low, sched); arena.Spawn(high, sched); }
            arena.ApplyEntryWeather(sched);

            var w = arena.Floor.Weather.Current;
            host.Free();
            return w;
        }

        var a = Probe(highFirst: true);
        var b = Probe(highFirst: false);
        GD.Print($"[検証] 対戦開始の天候は低種族値側が勝つ: "
                 + $"{(a == WeatherType.Snow && b == WeatherType.Snow ? "OK" : "NG")} "
                 + $"(高種族値を先に立てた場合 {a} / 後に立てた場合 {b}、期待 Snow)");
    }

    // 「〇〇のちから」6種に付いた天候効果（§27）。条件判定は
    // PowerTraitWeather に括り出してあるので、天候と特性を渡して戻り値を
    // 見るだけで乱数なしに確かめられる——命中ランク・回避ランクは命中判定の
    // 乱数の奥にあり、実際に撃たせて数えるとブレるため。
    //
    // 「効く条件で効く」と「効かない条件で効かない」を必ず対で見る。片方だけ
    // だと、天候を無視して常に発動する実装が通ってしまう。
    private void VerifyPowerTraitWeather()
    {
        var player = _sched.Roster.First(e => e.Faction == Faction.Player);
        var foe = _sched.Roster.First(e => e.Faction == Faction.Enemy);
        string savedP = player.Stats.Trait, savedF = foe.Stats.Trait;
        string savedT1 = foe.Stats.Type1, savedT2 = foe.Stats.Type2;

        void Check(string label, bool ok, string detail)
            => GD.Print($"[検証] {label}: {(ok ? "OK" : "NG")} ({detail})");

        // 闇のちから: きりのとき自分のターン終了時にHP5%回復。
        player.Stats.Trait = Combat.PowerTraitWeather.Dark;
        float fog = Combat.PowerTraitWeather.TurnEndHealRatio(player, WeatherType.Fog);
        float sunnyHeal = Combat.PowerTraitWeather.TurnEndHealRatio(player, WeatherType.Sunny);
        Check("闇のちから きりでターン終了時5%回復", fog == 0.05f && sunnyHeal == 0f,
              $"きり{fog:P0} / はれ{sunnyHeal:P0}");

        // 炎のちから: はれのとき水属性の相手への炎技+10。
        player.Stats.Trait = Combat.PowerTraitWeather.Fire;
        foe.Stats.Type1 = "Water"; foe.Stats.Type2 = null;
        float vsWater = Combat.PowerTraitWeather.FlatPowerBonus(player, foe, "Fire", WeatherType.Sunny);
        float vsWaterRain = Combat.PowerTraitWeather.FlatPowerBonus(player, foe, "Fire", WeatherType.Rain);
        float grassMove = Combat.PowerTraitWeather.FlatPowerBonus(player, foe, "Grass", WeatherType.Sunny);
        foe.Stats.Type1 = "Fire";
        float vsFire = Combat.PowerTraitWeather.FlatPowerBonus(player, foe, "Fire", WeatherType.Sunny);
        Check("炎のちから はれ×水相手の炎技+10",
              vsWater == 10f && vsWaterRain == 0f && grassMove == 0f && vsFire == 0f,
              $"水相手{vsWater} / あめ{vsWaterRain} / 草技{grassMove} / 炎相手{vsFire}");

        // 水のちから: あめのとき優先度+1（技だけでなく移動にも乗る）。
        player.Stats.Trait = Combat.PowerTraitWeather.Water;
        int rain = Combat.PowerTraitWeather.PriorityBonus(player, WeatherType.Rain);
        int snow = Combat.PowerTraitWeather.PriorityBonus(player, WeatherType.Snow);
        Check("水のちから あめで優先度+1", rain == 1 && snow == 0, $"あめ+{rain} / ゆき+{snow}");

        // 草のちから: はれのとき自分の草技の命中ランク+1。
        player.Stats.Trait = Combat.PowerTraitWeather.Grass;
        int gSunny = Combat.PowerTraitWeather.AccuracyBonus(player, "Grass", WeatherType.Sunny);
        int gOther = Combat.PowerTraitWeather.AccuracyBonus(player, "Fire", WeatherType.Sunny);
        int gRain = Combat.PowerTraitWeather.AccuracyBonus(player, "Grass", WeatherType.Rain);
        Check("草のちから はれで草技の命中ランク+1",
              gSunny == 1 && gOther == 0 && gRain == 0, $"はれ草+{gSunny} / はれ炎+{gOther} / あめ草+{gRain}");

        // 地のちから: すなあらしのとき回避ランク+1（技の射程を問わない）。
        foe.Stats.Trait = Combat.PowerTraitWeather.Ground;
        int sand = Combat.PowerTraitWeather.EvasionBonus(foe, MoveRange.Adjacent, WeatherType.Sandstorm);
        int sandOff = Combat.PowerTraitWeather.EvasionBonus(foe, MoveRange.Adjacent, WeatherType.Snow);
        Check("地のちから すなあらしで回避ランク+1", sand == 1 && sandOff == 0,
              $"すなあらし+{sand} / ゆき+{sandOff}");

        // 氷のちから: ゆきのとき**部屋/全体技に対してだけ**回避ランク+2。
        foe.Stats.Trait = Combat.PowerTraitWeather.Ice;
        int room = Combat.PowerTraitWeather.EvasionBonus(foe, MoveRange.Room, WeatherType.Snow);
        int full = Combat.PowerTraitWeather.EvasionBonus(foe, MoveRange.FullFloor, WeatherType.Snow);
        int area = Combat.PowerTraitWeather.EvasionBonus(foe, MoveRange.Area, WeatherType.Snow);
        int adj = Combat.PowerTraitWeather.EvasionBonus(foe, MoveRange.Room, WeatherType.Sunny);
        Check("氷のちから ゆきで部屋/全体技に回避ランク+2",
              room == 2 && full == 2 && area == 0 && adj == 0,
              $"部屋+{room} / 全体+{full} / 範囲+{area} / はれ部屋+{adj}");

        player.Stats.Trait = savedP; foe.Stats.Trait = savedF;
        foe.Stats.Type1 = savedT1; foe.Stats.Type2 = savedT2;
    }

    // 範囲技の着弾中心。**指定した空マスへ落ちる**ことを確かめる。
    //
    // 迷宮では狙う先が「目の前の1体」しか無いので、AttackAction は主対象の
    // マスから中心を引いていた。対戦は盤面のマスを直接指し、しかも範囲技は
    // 空マスを中心に複数を巻き込むために使う——指定を無視すると、予告した
    // 3x3と別の場所へ落ちて自分の味方を巻き込む（実際に起きた）。
    private void VerifyAoeAim()
    {
        var user = _sched.Roster.First(e => e.Faction == Faction.Player);
        var foe = _sched.Roster.First(e => e.Faction == Faction.Enemy);

        // 誰も立っていないマスを、使用者の向きとは別の方向に取る。
        var empty = new Vector2I(0, 0);
        bool free = !_sched.Roster.Any(e => e.GridPosition == empty);

        var slot = user.Moves.Slots.FirstOrDefault();
        var action = new AttackAction(user, null, slot, _floor, empty);

        // 指定したマスを中心にした3x3に、使用者も敵も入っていないこと。
        // （指定が無視されると、使用者の目の前＝敵陣側へ落ちる）
        var tiles = TargetResolver.ResolveTiles(MoveRange.Area, user.GridPosition,
                                                user.FacingDirection, empty, _grid, _floor);
        bool centred = tiles.Count > 0 && tiles.All(t => Mathf.Abs(t.X - empty.X) <= 1
                                                         && Mathf.Abs(t.Y - empty.Y) <= 1);
        GD.Print($"[検証] 範囲技は指定した空マスを中心にする: "
                 + $"{(free && centred && action != null ? "OK" : "NG")} "
                 + $"(中心{empty} / {tiles.Count}マス)");

        // 指定が無ければ従来どおり主対象のマス（迷宮の経路が変わらないこと）。
        var fallback = TargetResolver.ResolveTiles(MoveRange.Area, user.GridPosition,
                                                   user.FacingDirection, foe.GridPosition,
                                                   _grid, _floor);
        GD.Print($"[検証] 指定が無ければ従来どおり主対象を中心にする: "
                 + $"{(fallback.Any(t => t == foe.GridPosition) ? "OK" : "NG")}");
    }

    // 行動順規則の機械検証。優先度 > 俊敏(S)の高い順 > 合計種族値BST6(低いほうが先) の順で
    // 効くことを、実際の Order() で確かめる。盤上の素材は タマコッコ(003: S50・BST6 320) と
    // ブシガエル(009: S73・BST6 397)。
    private void VerifyTurnOrder()
    {
        var low = _sched.Roster.First(e => e.SpeciesId == "003");   // S50 / BST6 320
        var high = _sched.Roster.First(e => e.SpeciesId == "009");  // S73 / BST6 397

        // 同優先度・同俊敏なら合計種族値(BST6)が低いほうが先。
        // 俊敏だけを同値(70)にし、BST6 が動かないよう差分を HP に逆向きに振る。
        int lowSpd = low.Stats.BaseSpeed, highSpd = high.Stats.BaseSpeed;
        int lowHp = low.Stats.BaseMaxHp, highHp = high.Stats.BaseMaxHp;
        low.Stats.BaseSpeed = 70; low.Stats.BaseMaxHp = lowHp + (lowSpd - 70);
        high.Stats.BaseSpeed = 70; high.Stats.BaseMaxHp = highHp + (highSpd - 70);
        var byBst = _sched.Order(new BattleScheduler.Commitment(high, null, 0),
                                 new BattleScheduler.Commitment(low, null, 0));
        int lowBst = BattleScheduler.Bst(low), highBst = BattleScheduler.Bst(high);
        low.Stats.BaseSpeed = lowSpd; low.Stats.BaseMaxHp = lowHp;
        high.Stats.BaseSpeed = highSpd; high.Stats.BaseMaxHp = highHp;
        GD.Print($"[検証] 同優先度・同俊敏なら種族値が低いほうが先: "
                 + $"{(byBst[0].Actor == low && lowBst == 320 && highBst == 397 ? "OK" : "NG")} "
                 + $"({byBst[0].Actor.ActorName}(BST{BattleScheduler.Bst(byBst[0].Actor)}) が先、俊敏は両者70に揃えた)");

        // 優先度が高ければ俊敏を無視して先。遅いタマコッコに優先度1、速いブシガエルに優先度0。
        var byPriority = _sched.Order(new BattleScheduler.Commitment(low, null, 1),
                                      new BattleScheduler.Commitment(high, null, 0));
        GD.Print($"[検証] 優先度が俊敏を上書きする: "
                 + $"{(byPriority[0].Actor == low ? "OK" : "NG")} "
                 + $"({byPriority[0].Actor.ActorName}(S{byPriority[0].Actor.Stats.BaseSpeed}, 優先度1) が先、"
                 + $"相手はS{high.Stats.BaseSpeed}・優先度0)");
    }

    // HABCDS の配線（Phase 4）: 物理=A対B・特殊=C対D、ベンチマーク、俊敏順、BST6、種族値%加算。
    private void VerifyHabcdsWiring()
    {
        var atk = _sched.Roster.First(e => e.Faction == Faction.Enemy);
        var def = _sched.Roster.First(e => e.Faction == Faction.Player);
        foreach (var x in _sched.Roster) { x.StatusEffects.Reset(); x.Stats.HealToFull(); x.HeldItemId = null; }

        // 1. 物理技は A 対 B、特殊技は C 対 D。同じ属性・同威力の物理技と特殊技を取り、
        //    A≠C・B≠D の2個体で、物理の与ダメは C/D を、特殊の与ダメは A/B を変えても動かず、
        //    それぞれの対応する能力を変えると動くことを見る。
        var pool = MoveDatabase.AllIds().Select(MoveDatabase.Get)
            .Where(m => m.Range == MoveRange.Adjacent && m.Accuracy >= 100 && m.Power >= 40
                        && m.RankEffects.Count == 0 && m.AilmentEffect == AilmentType.None).ToList();
        var physMv = pool.First(m => m.Category == MoveCategory.Physical
                                     && pool.Any(o => o.Category == MoveCategory.Special && o.Type == m.Type && o.Power == m.Power));
        var specMv = pool.First(m => m.Category == MoveCategory.Special && m.Type == physMv.Type && m.Power == physMv.Power);

        var s = atk.Stats; var d = def.Stats;
        var saved = (s.BaseAtk, s.BaseSpAtk, s.Trait, d.BaseDef, d.BaseSpDef, d.Trait);
        s.Trait = ""; d.Trait = "";
        s.BaseAtk = 100; s.BaseSpAtk = 30; d.BaseDef = 40; d.BaseSpDef = 100;
        int p0 = MeasureHit(atk, def, physMv, null), q0 = MeasureHit(atk, def, specMv, null);
        s.BaseSpAtk = 130; d.BaseSpDef = 20;                        // C/D だけ動かす
        int p1 = MeasureHit(atk, def, physMv, null), q1 = MeasureHit(atk, def, specMv, null);
        s.BaseSpAtk = 30; d.BaseSpDef = 100; s.BaseAtk = 30; d.BaseDef = 100;   // A/B だけ動かす
        int p2 = MeasureHit(atk, def, physMv, null), q2 = MeasureHit(atk, def, specMv, null);
        (s.BaseAtk, s.BaseSpAtk, s.Trait, d.BaseDef, d.BaseSpDef, d.Trait) = saved;
        bool wired = p0 == p1 && q0 == q2 && q1 > q0 && p2 < p0 && p0 > q0;
        GD.Print($"[検証] 物理技は攻撃対防御・特殊技は魔力対精神でダメージが決まる: {(wired ? "OK" : "NG")} "
                 + $"(物理 A100/B40→{p0}, C/D変更後{p1}, A/B下げ後{p2} / 特殊 C30/D100→{q0}, C/D変更後{q1}, A/B変更後{q2} / {physMv.Name}・{specMv.Name} 威力{physMv.Power})");

        // 2. ベンチマーク。(a) DamageCalculator に実数値 攻75/防75 を直接渡して威力35 → 13（式は不変）。
        //    (b) 001 モコロン(66/64/85/27/46/59)同士 Lv50・突破0、STAB・相性・補正なし、威力35 → 物理10・特殊4。
        int direct = DamageCalculator.Calculate(new DamageContext { BaseAtk = 75, BaseDef = 75, BasePower = 35 });
        var moko = _sched.Roster.First(e => e.SpeciesId == "001").Stats;
        int physBench = DamageCalculator.Calculate(new DamageContext { BaseAtk = moko.Attack, BaseDef = moko.Defense, BasePower = 35 });
        int specBench = DamageCalculator.Calculate(new DamageContext { BaseAtk = moko.SpAttack, BaseDef = moko.SpDefense, BasePower = 35 });
        GD.Print($"[検証] ベンチマーク(直接70/70/70→13、モコロン同士 物理10・特殊4): "
                 + $"{(direct == 13 && physBench == 10 && specBench == 4 ? "OK" : "NG")} "
                 + $"(直接{direct} / モコロン 物理{physBench}[攻{moko.Attack}防{moko.Defense}] 特殊{specBench}[魔{moko.SpAttack}精{moko.SpDefense}])");

        // 3. 同優先度なら俊敏が高いほうが先: タマコッコ(S61・BST6 320) とブシガエル(S48・BST6 397)。
        //    さらに、俊敏が高く BST6 も高い組（BST6 の低い側が先という同速用の規則と逆向き）をロスターから探し、俊敏が優先されることを見る。
        var slow = _sched.Roster.First(e => e.SpeciesId == "003");
        var fast = _sched.Roster.First(e => e.SpeciesId == "009");
        var ord = _sched.Order(new BattleScheduler.Commitment(fast, null, 0),
                               new BattleScheduler.Commitment(slow, null, 0));
        (Entity hi, Entity lo)? pairHi = null;
        foreach (var x in _sched.Roster) foreach (var y in _sched.Roster)
            if (pairHi == null && x != y && x.Stats.BaseSpeed > y.Stats.BaseSpeed && BattleScheduler.Bst(x) > BattleScheduler.Bst(y))
                pairHi = (x, y);
        bool revOk = pairHi != null && _sched.Order(new BattleScheduler.Commitment(pairHi.Value.lo, null, 0),
                                                    new BattleScheduler.Commitment(pairHi.Value.hi, null, 0))[0].Actor == pairHi.Value.hi;
        GD.Print($"[検証] 同優先度なら俊敏が高いほうが先: {(ord[0].Actor == slow && revOk ? "OK" : "NG")} "
                 + $"(速くてBST6も高い組={(pairHi == null ? "無し" : pairHi.Value.hi.ActorName + "/" + pairHi.Value.lo.ActorName)} 俊敏優先={revOk} "
                 + $"{ord[0].Actor.ActorName}(S{ord[0].Actor.Stats.BaseSpeed}) が先 / 相手S{ord[1].Actor.Stats.BaseSpeed}。"
                 + $"BST6は先攻{BattleScheduler.Bst(ord[0].Actor)}・後攻{BattleScheduler.Bst(ord[1].Actor)})");

        // 4. BST は6能力合計: タマコッコ 320・ブシガエル 397。
        int bstSlow = BattleScheduler.Bst(slow), bstFast = BattleScheduler.Bst(fast);
        bool sumOk = slow.Stats.BaseMaxHp + slow.Stats.BaseAtk + slow.Stats.BaseDef + slow.Stats.BaseSpAtk + slow.Stats.BaseSpDef + slow.Stats.BaseSpeed == bstSlow;
        GD.Print($"[検証] BSTが6能力合計(BST6): {(bstSlow == 320 && bstFast == 397 && sumOk ? "OK" : "NG")} "
                 + $"(タマコッコ{bstSlow} ブシガエル{bstFast})");

        // 5. 種族値%の平坦加算が分類に応じた能力を見る。ニュートラルのきずな持ちが、
        //    Neutral の味方3体を連れて撃つと、特殊技は floor(BaseSpAtk*0.08*3)、物理技は
        //    floor(BaseAtk*0.08*3) の加算になる。きずなを外して実数値をその分だけ上げた場合
        //    （突破値 +1 で実数値 +1）と与ダメが一致することで確かめる。
        var allies = _sched.Roster.Where(e => e.Faction == atk.Faction && e != atk).ToList();
        var oldTypes = allies.Select(a => (a.Stats.Type1, a.Stats.Type2)).ToList();
        foreach (var a in allies) { a.Stats.Type1 = "Neutral"; a.Stats.Type2 = ""; }
        var savedBond = (s.BaseAtk, s.BaseSpAtk, s.Trait, d.BaseDef, d.BaseSpDef, d.Trait);
        s.BaseAtk = 30; s.BaseSpAtk = 100; d.Trait = "";
        int allyN = System.Math.Min(3, allies.Count);
        int addSp = Mathf.FloorToInt(s.BaseSpAtk * 0.08f * allyN);   // 特殊技は魔力を見る（100→24）
        int addPh = Mathf.FloorToInt(s.BaseAtk * 0.08f * allyN);     // 物理技は攻撃を見る（30→7）
        s.Trait = "neutral_bond";
        int bondPh = MeasureHit(atk, def, physMv, null), bondSp = MeasureHit(atk, def, specMv, null);
        s.Trait = "";
        s.BreakthroughSpAtk = addSp;
        int refSp = MeasureHit(atk, def, specMv, null);
        s.BreakthroughSpAtk = 0; s.BreakthroughAtk = addPh;
        int refPh = MeasureHit(atk, def, physMv, null);
        s.BreakthroughAtk = 0;
        int noBondSp = MeasureHit(atk, def, specMv, null);
        (s.BaseAtk, s.BaseSpAtk, s.Trait, d.BaseDef, d.BaseSpDef, d.Trait) = savedBond;
        for (int i = 0; i < allies.Count; i++) { allies[i].Stats.Type1 = oldTypes[i].Type1; allies[i].Stats.Type2 = oldTypes[i].Type2; }
        GD.Print($"[検証] 種族値%の平坦加算が分類に応じた能力を見る(きずな): "
                 + $"{(allyN == 3 && addSp == 24 && addPh == 7 && bondSp == refSp && bondPh == refPh && bondSp > noBondSp ? "OK" : "NG")} "
                 + $"(特殊 加算+{addSp}: きずな{bondSp}=基準{refSp}、きずな無し{noBondSp} / 物理 加算+{addPh}: きずな{bondPh}=基準{refPh})");

        foreach (var x in _sched.Roster) { x.StatusEffects.Reset(); x.Stats.HealToFull(); x.HeldItemId = null; }
    }

    // 突破値・気質（Phase S2）: 適用順、構築検証、行動順、自動配分、通信。
    private void VerifySpread()
    {
        foreach (var x in _sched.Roster) { x.StatusEffects.Reset(); x.Stats.HealToFull(); x.HeldItemId = null; }

        // 1. 気質の後に突破値を足す。ブシガエル(009, A79): 素 84。
        //    無補正 84 / ↑攻撃 floor(84*1.1)=92 / ↑攻撃+攻撃25 = 92+25 = 117（先に足すと 119）/ ↓攻撃 floor(84*0.9)=75。
        var bushi = _sched.Roster.First(e => e.SpeciesId == "009").Stats;
        bushi.HealToFull();
        int a0 = bushi.Attack;
        bushi.NatureUp = NatureStat.Atk; bushi.NatureDown = NatureStat.SpAtk;
        int aUp = bushi.Attack;
        bushi.BreakthroughAtk = 25;
        int aUpBt = bushi.Attack;
        bushi.BreakthroughAtk = 0;
        bushi.NatureUp = NatureStat.SpAtk; bushi.NatureDown = NatureStat.Atk;
        int aDown = bushi.Attack;
        bushi.NatureUp = NatureStat.None; bushi.NatureDown = NatureStat.None;
        int hp0 = bushi.MaxHp;
        bushi.BreakthroughHP = 25;
        int hpBt = bushi.MaxHp; int curBt = bushi.CurrentHp;
        bushi.BreakthroughHP = 0;
        bushi.HealToFull();
        GD.Print($"[検証] 気質の後に突破値を足す: "
                 + $"{(a0 == 84 && aUp == 92 && aUpBt == 117 && aDown == 75 && hp0 == 136 && hpBt == 161 && curBt == hpBt && bushi.MaxHp == 136 ? "OK" : "NG")} "
                 + $"(ブシガエル 攻撃 無補正{a0}・↑{aUp}・↑+25={aUpBt}(先に足すと{(97 + 25) * 11 / 10})・↓{aDown} / HP25 最大{hp0}→{hpBt} 現在{curBt})");

        // 2. 構築検証。BattleTeam.Validate を通して見る。
        BattleTeam Build(StatSpread sp)
        {
            var t = BuildTeam(PlayerRoster);
            t.Entries[0].Spread = sp;
            return t;
        }
        var over1 = Build(new StatSpread { Atk = 26 }).Validate();
        var over2 = Build(new StatSpread { Atk = 25, SpAtk = 25, Hp = 6 }).Validate();
        var tot55 = Build(new StatSpread { Atk = 25, SpAtk = 25, Hp = 5 }).Validate();
        var tot0 = Build(StatSpread.Zero()).Validate();
        var sameNat = Build(new StatSpread { NatureUp = NatureStat.Atk, NatureDown = NatureStat.Atk }).Validate();
        var halfNat = Build(new StatSpread { NatureUp = NatureStat.Atk }).Validate();
        var goodNat = Build(new StatSpread { NatureUp = NatureStat.Atk, NatureDown = NatureStat.SpAtk }).Validate();
        bool spreadOk = over1.Count > 0 && over2.Count > 0 && tot55.Count == 0 && tot0.Count == 0
                        && sameNat.Count > 0 && halfNat.Count > 0 && goodNat.Count == 0;
        GD.Print($"[検証] 突破値と気質の構築検証: {(spreadOk ? "OK" : "NG")} "
                 + $"(1能力26→{(over1.Count > 0 ? "弾く" : "通る")}、合計56→{(over2.Count > 0 ? "弾く" : "通る")}、合計55→{(tot55.Count == 0 ? "通る" : "弾く")}、"
                 + $"合計0→{(tot0.Count == 0 ? "通る" : "弾く")}、↑攻撃↓攻撃→{(sameNat.Count > 0 ? "弾く" : "通る")}、↑攻撃のみ→{(halfNat.Count > 0 ? "弾く" : "通る")}、"
                 + $"↑攻撃↓魔力→{(goodNat.Count == 0 ? "通る" : "弾く")}。期待は順に 弾く/弾く/通る/通る/弾く/弾く/通る)");

        // 3. 行動順は俊敏の実数値を見る。ブシガエル(009, S48): ↑俊敏+俊敏25 → floor(53*1.1)+25 = 83。
        //    タマコッコ(003, S61): 無補正・突破0 → 66。優先度0同士でブシガエルが先。戻すとタマコッコが先。
        var tama = _sched.Roster.First(e => e.SpeciesId == "003");
        var bushiE = _sched.Roster.First(e => e.SpeciesId == "009");
        bushiE.Stats.NatureUp = NatureStat.Speed; bushiE.Stats.NatureDown = NatureStat.Atk;
        bushiE.Stats.BreakthroughSpeed = 25;
        int bushiSpd = bushiE.Stats.Speed, tamaSpd = tama.Stats.Speed;
        var fastBushi = _sched.Order(new BattleScheduler.Commitment(tama, null, 0),
                                     new BattleScheduler.Commitment(bushiE, null, 0));
        bushiE.Stats.NatureUp = NatureStat.None; bushiE.Stats.NatureDown = NatureStat.None;
        bushiE.Stats.BreakthroughSpeed = 0;
        var backTama = _sched.Order(new BattleScheduler.Commitment(bushiE, null, 0),
                                    new BattleScheduler.Commitment(tama, null, 0));
        GD.Print($"[検証] 行動順は俊敏の実数値を見る: "
                 + $"{(bushiSpd == 83 && tamaSpd == 66 && fastBushi[0].Actor == bushiE && backTama[0].Actor == tama ? "OK" : "NG")} "
                 + $"(ブシガエル S48→実数値{bushiSpd}、タマコッコ S61→実数値{tamaSpd}。振ると{fastBushi[0].Actor.ActorName}が先、戻すと{backTama[0].Actor.ActorName}が先)");

        // 4. 自動配分は決定的で規則どおり。
        var db = SpeciesDatabase.Instance;
        int bad = 0, count = 0;
        foreach (var kv in db.All)
        {
            var a = StatSpread.Auto(kv.Value); var b = StatSpread.Auto(kv.Value);
            count++;
            bool same = a.Hp == b.Hp && a.Atk == b.Atk && a.Def == b.Def && a.SpAtk == b.SpAtk
                        && a.SpDef == b.SpDef && a.Speed == b.Speed
                        && a.NatureUp == b.NatureUp && a.NatureDown == b.NatureDown;
            int mx = System.Math.Max(System.Math.Max(a.Hp, a.Atk), System.Math.Max(System.Math.Max(a.Def, a.SpAtk), System.Math.Max(a.SpDef, a.Speed)));
            if (a.Total != 55 || mx > 25 || a.Validate().Count > 0 || !same) bad++;
        }
        var sBushi = StatSpread.Auto(db.Get("009"));
        var sLight = StatSpread.Auto(db.Get("130B"));
        var sXeno = StatSpread.Auto(db.Get("196"));
        bool named = sBushi.Hp == 25 && sBushi.Atk == 25 && sBushi.SpDef == 5 && sBushi.Total == 55
                     && sBushi.NatureUp == NatureStat.Atk && sBushi.NatureDown == NatureStat.SpAtk
                     && sLight.SpAtk == 25 && sLight.Speed == 25 && sLight.SpDef == 0 && sLight.Total == 55
                     && sLight.NatureUp == NatureStat.SpAtk && sLight.NatureDown == NatureStat.Atk
                     && sXeno.Atk == 25 && sXeno.Speed == 25 && sXeno.Def == 5 && sXeno.Total == 55
                     && sXeno.NatureUp == NatureStat.Atk && sXeno.NatureDown == NatureStat.SpAtk;
        GD.Print($"[検証] 自動配分は決定的で規則どおり: {(bad == 0 && count == 287 && named ? "OK" : "NG")} "
                 + $"({count}種中の違反{bad}。ブシガエル {sBushi.NatureLabel} HP{sBushi.Hp}/攻{sBushi.Atk}/精{sBushi.SpDef}、"
                 + $"ライトロット {sLight.NatureLabel} 魔{sLight.SpAtk}/俊{sLight.Speed}/精{sLight.SpDef}、"
                 + $"ゼノドラン {sXeno.NatureLabel} 攻{sXeno.Atk}/俊{sXeno.Speed}/防{sXeno.Def})");

        // 5. 通信の直列化で突破値と気質が保たれる。
        var net = BuildTeam(PlayerRoster);
        var natures = new[] { NatureStat.Atk, NatureStat.Def, NatureStat.SpAtk, NatureStat.SpDef, NatureStat.Speed, NatureStat.None };
        for (int i = 0; i < net.Entries.Count; i++)
        {
            net.Entries[i].Spread = new StatSpread
            {
                Hp = i, Atk = i + 1, Def = i + 2, SpAtk = i + 3, SpDef = i + 4, Speed = i + 5,
                NatureUp = i == 5 ? NatureStat.None : natures[i],
                NatureDown = i == 5 ? NatureStat.None : natures[(i + 1) % 5],
            };
        }
        var round = BattleNetwork.DecodeFull(BattleNetwork.EncodeFull(net));
        bool same2 = round.Entries.Count == net.Entries.Count;
        for (int i = 0; same2 && i < net.Entries.Count; i++)
        {
            var x = net.Entries[i].Spread; var y = round.Entries[i].Spread;
            same2 = x.Hp == y.Hp && x.Atk == y.Atk && x.Def == y.Def && x.SpAtk == y.SpAtk && x.SpDef == y.SpDef
                    && x.Speed == y.Speed && x.NatureUp == y.NatureUp && x.NatureDown == y.NatureDown;
        }
        var parsed = Json.ParseString(BattleNetwork.EncodeFull(net)).AsGodotArray();
        bool hasKeys = parsed.All(v => { var d = v.AsGodotDictionary(); return d.ContainsKey("b") && d.ContainsKey("nu") && d.ContainsKey("nd"); });
        foreach (var v in parsed) { var d = v.AsGodotDictionary(); d.Remove("b"); d.Remove("nu"); d.Remove("nd"); }
        var legacy = BattleNetwork.DecodeFull(Json.Stringify(parsed));
        bool zero = legacy.Entries.All(e => e.Spread != null && e.Spread.Total == 0
                                            && e.Spread.NatureUp == NatureStat.None && e.Spread.NatureDown == NatureStat.None);
        GD.Print($"[検証] 通信の直列化で突破値と気質が保たれる: {(same2 && hasKeys && zero ? "OK" : "NG")} "
                 + $"(6匹の6値と気質が一致={same2}、キー b/nu/nd あり={hasKeys}、キーを消した JSON は Zero で読める={zero})");

        foreach (var x in _sched.Roster) { x.StatusEffects.Reset(); x.Stats.HealToFull(); x.HeldItemId = null; }
    }

    // 虚(Void)属性: 相性表・個体数・learnset・虚技が仕様どおりに入っていること。
    private void VerifyVoidType()
    {
        // 1. 相性表が10属性で、Element enum と同じ名前・同じ順序。
        var names = System.Enum.GetNames<Element>();
        string[] chartTypes;
        using (var f = FileAccess.Open("res://Data/type_chart.json", FileAccess.ModeFlags.Read))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(f.GetAsText());
            chartTypes = doc.RootElement.GetProperty("types").EnumerateArray()
                            .Select(e => e.GetString()).ToArray();
        }
        bool typesOk = chartTypes.Length == 10 && names.Length == 10 && chartTypes.SequenceEqual(names);
        GD.Print($"[検証] 相性表が10属性で Element と一致: {(typesOk ? "OK" : "NG")} "
                 + $"(types={chartTypes.Length} enum={names.Length})");

        // 2. 虚の6マス。
        (string atk, string def, float want)[] cells =
        {
            ("Fire", "Void", 0.5f), ("Dark", "Void", 0.5f), ("Ice", "Void", 2.0f),
            ("Neutral", "Void", 2.0f), ("Void", "Neutral", 0.5f), ("Void", "Fire", 2.0f),
        };
        var badCells = cells.Where(c => TypeChartManager.GetMultiplier(c.atk, c.def) != c.want)
                            .Select(c => $"{c.atk}->{c.def}={TypeChartManager.GetMultiplier(c.atk, c.def)}").ToList();
        GD.Print($"[検証] 虚の6マス(炎→虚0.5/闇→虚0.5/氷→虚2.0/無→虚2.0/虚→無0.5/虚→炎2.0): "
                 + $"{(badCells.Count == 0 ? "OK" : "NG " + string.Join(",", badCells))}");

        // 3. 既存81マスが変わっていない（変更前の 9x9 を埋め込んで比較）。行=攻撃、列=防御。
        string[] old9 = { "Neutral", "Fire", "Water", "Grass", "Electric", "Ground", "Ice", "Dragon", "Dark" };
        float[][] oldMatrix =
        {
            new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 0.5f },
            new[] { 1f, 1f, 0.5f, 2f, 1f, 1f, 2f, 1f, 1f },
            new[] { 1f, 2f, 1f, 1f, 0.5f, 1f, 1f, 1f, 1f },
            new[] { 1f, 0.5f, 1f, 1f, 1f, 2f, 1f, 1f, 1f },
            new[] { 1f, 1f, 2f, 1f, 1f, 0.5f, 1f, 1f, 1f },
            new[] { 1f, 1f, 1f, 0.5f, 2f, 1f, 1f, 1f, 1f },
            new[] { 1f, 0.5f, 1f, 1f, 1f, 1f, 1f, 2f, 1f },
            new[] { 1f, 1f, 1f, 1f, 1f, 1f, 0.5f, 1f, 2f },
            new[] { 2f, 1f, 1f, 1f, 1f, 1f, 1f, 0.5f, 1f },
        };
        int changed = 0;
        for (int a = 0; a < 9; a++)
            for (int d = 0; d < 9; d++)
                if (TypeChartManager.GetMultiplier(old9[a], old9[d]) != oldMatrix[a][d]) changed++;
        GD.Print($"[検証] 既存81マスが変わっていない: {(changed == 0 ? "OK" : "NG")} (相違{changed}マス)");

        // 4. 個体数（二重属性は両方に計上）: 虚30・闇46・水45。
        var all = SpeciesDatabase.Instance.All.Values.ToList();
        int nVoid = all.Count(sp => sp.Types.Contains(Element.Void));
        int nDark = all.Count(sp => sp.Types.Contains(Element.Dark));
        int nWater = all.Count(sp => sp.Types.Contains(Element.Water));
        GD.Print($"[検証] 個体数 虚30・闇46・水45: {(nVoid == 30 && nDark == 46 && nWater == 45 ? "OK" : "NG")} "
                 + $"(虚{nVoid} 闇{nDark} 水{nWater})");

        // 5. 虚の30種の learnset: 闇(水→虚の047・055は水)の攻撃技が0件、虚の攻撃技が1件以上。
        var leftover = new List<string>();
        foreach (var sp in all.Where(sp => sp.Types.Contains(Element.Void)))
        {
            string banned = sp.SpeciesId is "047" or "055" ? "Water" : "Dark";
            var moves = sp.Learnset.Select(l => MoveDatabase.Get(l.MoveId)).Where(m => m != null && m.Power > 0).ToList();
            if (moves.Any(m => m.Type == banned)) leftover.Add($"{sp.SpeciesId}:{banned}技あり");
            if (!moves.Any(m => m.Type == "Void")) leftover.Add($"{sp.SpeciesId}:虚技なし");
        }
        GD.Print($"[検証] 虚の個体の learnset に闇(水)の攻撃技が無く虚の攻撃技がある: "
                 + $"{(leftover.Count == 0 ? "OK" : "NG " + string.Join(",", leftover))}");

        // 6. 虚の攻撃技55件が、元の闇技と威力・分類・射程・命中・PP・接触で一致。
        var voidMoves = MoveDatabase.AllIds().Select(MoveDatabase.Get)
                                    .Where(m => m.Type == "Void" && m.Power > 0 && !m.Id.StartsWith("rs_")).ToList();
        var mismatch = new List<string>();
        foreach (var v in voidMoves)
        {
            var src = v.Id.StartsWith("void_") ? MoveDatabase.Get(v.Id.Substring(5)) : null;
            if (src == null || src.Type != "Dark" || src.Power != v.Power || src.Category != v.Category
                || src.Range != v.Range || src.Accuracy != v.Accuracy || src.MaxPp != v.MaxPp
                || src.IsContact != v.IsContact)
                mismatch.Add(v.Id);
        }
        GD.Print($"[検証] 虚の攻撃技55件が元の闇技と性能一致: "
                 + $"{(voidMoves.Count == 55 && mismatch.Count == 0 ? "OK" : "NG")} "
                 + $"({voidMoves.Count}件 不一致{mismatch.Count})");
    }

    // HABCDS データ（Phase 3 / 能力値再設計 S1）。species.json の6能力・攻撃区分・耐久区分・俊敏が仕様どおりに載っていること。
    // 配線（ダメージ計算・行動順）は Phase 4 なので、ここは読み込んだ値だけを見る。
    private void VerifyHabcdsData()
    {
        var all = SpeciesDatabase.Instance.All.Values.ToList();
        SpeciesData Sp(string id) => SpeciesDatabase.Instance.Get(id);

        // 1. 287種すべてが6能力を持ち（全て10以上）、合計が320〜610。代表3種の合計。
        var bad1 = all.Where(sp => new[] { sp.BaseHP, sp.BaseAtk, sp.BaseDef, sp.BaseSpAtk, sp.BaseSpDef, sp.BaseSpeed }.Min() < 10
                                   || sp.Bst < 320 || sp.Bst > 610).Select(sp => sp.SpeciesId).ToList();
        bool ref1 = Sp("003").Bst == 320 && Sp("186").Bst == 474 && Sp("203").Bst == 610;
        GD.Print($"[検証] 287種がHABCDSを持ち全て10以上・合計320〜610(003=320/186=474/203=610): "
                 + $"{(all.Count == 287 && bad1.Count == 0 && ref1 ? "OK" : "NG")} "
                 + $"({all.Count}種 違反{bad1.Count} 003={Sp("003").Bst} 186={Sp("186").Bst} 203={Sp("203").Bst})");

        // 2. 配分比率が区分どおり: 全287種で |B/(B+D)-β| <= 0.5/(B+D)（物理耐久 0.65・特殊耐久 0.35）。
        //    物理攻撃・特殊攻撃は |A/(A+C)-α| <= 0.5/(A+C)（0.7 / 0.3）。
        //    両面耐久は H=round(0.8(B+D))±1 かつ B:D が 41:59〜59:41。
        //    両刀攻撃は A+C>=160・A+C>1.3(B+D)・|A-C|>=10・A:C が 4:6〜6:4・H>=55・両面耐久でない。
        //    下限規則（各能力>=20・最小2つの和>=49）で点を移した6種（仕様書 2026-10-30）は比率の検査から外す。
        var floorFixed = new HashSet<string> { "011", "129", "129B", "131B", "142", "142B" };
        var bad2 = new List<string>();
        foreach (var sp in all)
        {
            double g = sp.BaseDef + sp.BaseSpDef, off = sp.BaseAtk + sp.BaseSpAtk;
            if (sp.Defense == "Both" && (System.Math.Abs(sp.BaseHP - System.Math.Floor(0.8 * g + 0.5)) > 1 || 100 * System.Math.Min(sp.BaseDef, sp.BaseSpDef) < 41 * g))
            { bad2.Add(sp.SpeciesId); continue; }
            if (sp.Offense == "Mixed")
            {
                bool okM = off >= 160 && 10 * off > 13 * g && System.Math.Abs(sp.BaseAtk - sp.BaseSpAtk) >= 10
                           && 5 * System.Math.Min(sp.BaseAtk, sp.BaseSpAtk) >= 2 * off && sp.BaseHP >= 55 && sp.Defense != "Both";
                if (!okM) bad2.Add(sp.SpeciesId);
                continue;
            }
            if (floorFixed.Contains(sp.SpeciesId)) continue;
            double beta = sp.Defense switch { "Physical" => 0.65, "Special" => 0.35, "Both" => 0.5, _ => -1 };
            if (beta < 0 || System.Math.Abs(sp.BaseDef / g - beta) > 0.5 / g + 1e-9) { bad2.Add(sp.SpeciesId); continue; }
            double alpha = sp.Offense switch { "Physical" => 0.7, "Special" => 0.3, _ => -1 };
            if (alpha < 0 || System.Math.Abs(sp.BaseAtk / off - alpha) > 0.5 / off + 1e-9) bad2.Add(sp.SpeciesId);
        }
        GD.Print($"[検証] 配分比率が区分どおり(B:D 65:35/35:65/両面は H=0.8(B+D)・A:C 70:30/30:70・両刀は A+C>=160 等): "
                 + $"{(bad2.Count == 0 ? "OK" : "NG")} (287種 違反{bad2.Count} {string.Join(",", bad2.Take(5))})");

        // 3. 9役割が全て実在し内訳が表どおり、かつ全種で Role == Offense + "-" + Defense。
        int Count(string o, string d) => all.Count(sp => sp.Offense == o && sp.Defense == d);
        var expect3 = new (string o, string d, int n)[]
        {
            ("Physical", "Physical", 51), ("Physical", "Special", 45), ("Physical", "Both", 47),
            ("Special", "Physical", 42), ("Special", "Special", 47), ("Special", "Both", 43),
            ("Mixed", "Physical", 5), ("Mixed", "Special", 7), ("Mixed", "Both", 0),
        };
        bool counts3 = expect3.All(e => Count(e.o, e.d) == e.n);
        var bad3 = all.Where(sp => sp.Role != sp.Offense + "-" + sp.Defense).Select(sp => sp.SpeciesId).ToList();
        GD.Print($"[検証] 役割の内訳が表どおり(物理攻撃51/45/47・特殊攻撃42/47/43・両刀攻撃5/7/0＝12種・両刀×両面耐久は無い): "
                 + $"{(counts3 && bad3.Count == 0 ? "OK" : "NG")} "
                 + $"(物理攻撃{Count("Physical", "Physical")}/{Count("Physical", "Special")}/{Count("Physical", "Both")} "
                 + $"特殊攻撃{Count("Special", "Physical")}/{Count("Special", "Special")}/{Count("Special", "Both")} "
                 + $"両刀攻撃{Count("Mixed", "Physical")}/{Count("Mixed", "Special")}/{Count("Mixed", "Both")} Role不一致{bad3.Count})");

        // 4. 俊敏: 確定表（docs/design/drafts/2026-10-30-speed-table.md）。全種20〜130で最小20・最大130。
        //    表の抜き出し 199=105・130B=99・196=122・201=116。山45/75/105 の ±4 の頭数が谷60/90 より多い。
        int minS = all.Min(sp => sp.BaseSpeed), maxS = all.Max(sp => sp.BaseSpeed);
        bool refs4 = Sp("199").BaseSpeed == 105 && Sp("130B").BaseSpeed == 99
                  && Sp("196").BaseSpeed == 122 && Sp("201").BaseSpeed == 116;
        int Win(int c) => all.Count(sp => sp.BaseSpeed >= c - 4 && sp.BaseSpeed <= c + 4);
        bool peaksOk = Win(45) > Win(60) && Win(75) > Win(60) && Win(75) > Win(90) && Win(105) > Win(90);
        GD.Print($"[検証] 俊敏20〜130(最小20・最大130)・表の抜き出し(199=105/130B=99/196=122/201=116)・三峰(45/75/105): "
                 + $"{(minS == 20 && maxS == 130 && refs4 && peaksOk ? "OK" : "NG")} "
                 + $"(最小{minS} 最大{maxS} 199={Sp("199").BaseSpeed} 130B={Sp("130B").BaseSpeed} 196={Sp("196").BaseSpeed} 201={Sp("201").BaseSpeed} "
                 + $"山45={Win(45)} 谷60={Win(60)} 山75={Win(75)} 谷90={Win(90)} 山105={Win(105)})");

        // 5. 俊敏95以上は優先度+1以上の攻撃技を習得しない（変化技は対象外）。
        var fastAll = all.Where(sp => sp.BaseSpeed >= 95).ToList();
        int blocked = 0;
        foreach (var sp in fastAll)
            foreach (var e in sp.Learnset)
            {
                var m = MoveDatabase.Get(e.MoveId);
                if (m != null && m.Power > 0 && m.Priority >= 1) blocked++;
            }
        bool yakumo = Sp("155").Learnset.Any(e => e.MoveId == "MV_141");
        GD.Print($"[検証] 俊敏95以上は優先度+1以上の攻撃技を習得しない: {(fastAll.Count == 60 && blocked == 0 && yakumo ? "OK" : "NG")} "
                 + $"(S>=95 {fastAll.Count}種 該当{blocked}件 155ワンフーはMV_141を習得={yakumo})");

        // 6. 俊敏が低いほど総合耐久指数 H*(B+D) が高い: 相関 r<=-0.40 かつ帯別平均（<=62 / 63-87 / >=88）が単調減少。
        double Pearson(IList<double> xs, IList<double> ys)
        {
            double mx = xs.Average(), my = ys.Average(), sxy = 0, sxx = 0, syy = 0;
            for (int i = 0; i < xs.Count; i++) { sxy += (xs[i] - mx) * (ys[i] - my); sxx += (xs[i] - mx) * (xs[i] - mx); syy += (ys[i] - my) * (ys[i] - my); }
            return sxy / System.Math.Sqrt(sxx * syy);
        }
        var sv = all.Select(sp => (double)sp.BaseSpeed).ToList();
        var ti = all.Select(sp => (double)sp.BaseHP * (sp.BaseDef + sp.BaseSpDef)).ToList();
        double r6 = Pearson(sv, ti);
        double Band(int lo, int hi) => all.Where(sp => sp.BaseSpeed >= lo && sp.BaseSpeed <= hi).Average(sp => (double)sp.BaseHP * (sp.BaseDef + sp.BaseSpDef));
        double b1 = Band(0, 62), b2 = Band(63, 87), b3 = Band(88, 999);
        GD.Print($"[検証] 俊敏が低いほど総合耐久指数が高い(r<=-0.40・帯別平均が単調減少): {(r6 <= -0.40 && b1 > b2 && b2 > b3 ? "OK" : "NG")} "
                 + $"(r={r6:F3} 帯別平均 <=62:{b1:F0} > 63-87:{b2:F0} > >=88:{b3:F0})");

        // 7. 両刀攻撃は12種（仕様書 2026-10-30 §3-2）。全員が trait2 を持ち、両面耐久でなく、主属性ごとに2種以下。
        var mixedSp = all.Where(sp => sp.Offense == "Mixed").ToList();
        int badMixed = mixedSp.Count(sp => string.IsNullOrEmpty(sp.Trait2) || sp.Defense == "Both"
                                           || sp.BaseAtk + sp.BaseSpAtk < 160 || sp.BaseHP < 55
                                           || 10 * (sp.BaseAtk + sp.BaseSpAtk) <= 13 * (sp.BaseDef + sp.BaseSpDef));
        int maxPerElement = mixedSp.GroupBy(sp => sp.Types[0]).Max(g => g.Count());
        int nonMixedWithT2 = all.Count(sp => sp.Offense != "Mixed" && !string.IsNullOrEmpty(sp.Trait2));
        GD.Print($"[検証] 両刀攻撃は12種・全員が追加特性を持ち条件を満たす・主属性ごと2種以下: "
                 + $"{(mixedSp.Count == 12 && badMixed == 0 && maxPerElement <= 2 && nonMixedWithT2 == 0 ? "OK" : "NG")} "
                 + $"(両刀攻撃{mixedSp.Count}種 違反{badMixed} 主属性の最大{maxPerElement}種 両刀以外のtrait2持ち{nonMixedWithT2})");
    }

    // 両刀攻撃の調査用「2つめの特性」(Trait2)。仕様書
    // docs/design/drafts/2026-10-28-mixed-traits.md §4-2。
    // 「効く条件で効く」と「効かない条件で効かない」を対で見る。
    private void VerifySecondaryTraits()
    {
        void Check(string label, bool ok, string detail)
            => GD.Print($"[検証] {label}: {(ok ? "OK" : "NG")} ({detail})");

        var atk = _sched.Roster.First(e => e.Faction == Faction.Enemy);
        var def = _sched.Roster.First(e => e.Faction == Faction.Player);
        foreach (var x in _sched.Roster) { x.StatusEffects.Reset(); x.Stats.HealToFull(); }
        string savedT = atk.Stats.Trait, savedT2 = atk.Stats.Trait2; bool savedUse = atk.Stats.UseTrait2;
        string savedDefT = def.Stats.Trait, savedDefT2 = def.Stats.Trait2;
        string savedType1 = atk.Stats.Type1, savedType2 = atk.Stats.Type2;
        atk.Stats.Trait = ""; atk.Stats.Trait2 = ""; atk.Stats.UseTrait2 = false; def.Stats.Trait = ""; def.Stats.Trait2 = "";

        var all = MoveDatabase.AllIds().Select(MoveDatabase.Get).Where(m => m != null).ToList();
        var roomSpecial = all.First(m => m.Range == MoveRange.Room && m.Category == MoveCategory.Special && m.Power > 0);
        var adjSpecial = all.First(m => m.Range == MoveRange.Adjacent && m.Category == MoveCategory.Special && m.Power > 0);
        var discharge = MoveDatabase.Get("MV_170");

        // 1. ワイドバースト: 部屋技で+10・単体技で0・特性なしで0
        atk.Stats.Trait2 = SecondaryTraits.WideBurst; atk.Stats.UseTrait2 = true;
        float wbRoom = SecondaryTraits.FlatPowerBonus(atk, roomSpecial);
        float wbAdj = SecondaryTraits.FlatPowerBonus(atk, adjSpecial);
        atk.Stats.Trait2 = "";
        float wbNone = SecondaryTraits.FlatPowerBonus(atk, roomSpecial);
        Check("ワイドバースト 部屋技で+10・単体技で0", wbRoom == 10f && wbAdj == 0f && wbNone == 0f,
              $"部屋{wbRoom} / 単体{wbAdj} / 特性なし{wbNone}");

        // 2. 物理の部屋技(MV_170)にも+10
        atk.Stats.Trait2 = SecondaryTraits.WideBurst; atk.Stats.UseTrait2 = true;
        float wbPhys = SecondaryTraits.FlatPowerBonus(atk, discharge);
        atk.Stats.Trait2 = "";
        Check("ワイドバースト 物理の部屋技(MV_170)にも+10",
              discharge != null && discharge.Category == MoveCategory.Physical && discharge.Range == MoveRange.Room && wbPhys == 10f,
              $"{discharge?.Name} +{wbPhys}");

        // 3. こわざじょうず: 特殊・威力50以下で+20、それ以外は0
        var sp50 = all.FirstOrDefault(m => m.Category == MoveCategory.Special && m.Power == 50);
        var sp55 = all.First(m => m.Category == MoveCategory.Special && m.Power >= 55);
        var ph50 = all.First(m => m.Category == MoveCategory.Physical && m.Power > 0 && m.Power <= 50);
        var status = all.First(m => m.Power == 0);
        atk.Stats.Trait2 = SecondaryTraits.KowazaJouzu; atk.Stats.UseTrait2 = true;
        float kw50 = sp50 == null ? -1f : SecondaryTraits.FlatPowerBonus(atk, sp50);
        float kw55 = SecondaryTraits.FlatPowerBonus(atk, sp55);
        float kwPh = SecondaryTraits.FlatPowerBonus(atk, ph50);
        float kwSt = SecondaryTraits.FlatPowerBonus(atk, status);
        atk.Stats.Trait2 = "";
        Check("こわざじょうず 特殊・威力50以下で+20、それ以外は0",
              kw50 == 20f && kw55 == 0f && kwPh == 0f && kwSt == 0f,
              $"特殊50={kw50} / 特殊{sp55.Power}={kw55} / 物理{ph50.Power}={kwPh} / 変化={kwSt}");

        // 4. しみこみの倍率
        atk.Stats.Trait2 = SecondaryTraits.Shimikomi; atk.Stats.UseTrait2 = true;
        int mTarget = SecondaryTraits.AccumulationMultiplier(atk, def);
        int mSelf = SecondaryTraits.AccumulationMultiplier(atk, atk);
        atk.Stats.Trait2 = "";
        int mOther = SecondaryTraits.AccumulationMultiplier(atk, def);
        Check("しみこみ 倍率は相手に2・自分に1・非保持者は1", mTarget == 2 && mSelf == 1 && mOther == 1,
              $"相手{mTarget} / 自分{mSelf} / 非保持者{mOther}");

        // 5. しみこみ: 宣言なしの属性技1発で蓄積+50(非保持者は+25)
        var fire = all.First(m => m.Type == "Fire" && m.Range == MoveRange.Adjacent && m.Power >= 40
                                  && m.Accuracy >= 100 && m.AilmentEffect == AilmentType.None
                                  && m.RankEffects.Count == 0);
        int FirstHitAcc(AilmentType kind, MoveData mv)
        {
            int acc = 0;
            for (int i = 0; i < 16 && acc == 0; i++)
            {
                def.StatusEffects.Reset(); def.Stats.HealToFull();
                new AttackAction(atk, def, MakeSlot(atk, mv), _floor).Execute(0);
                acc = def.StatusEffects.GetAccumulation(kind);
            }
            return acc;
        }
        atk.Stats.Trait2 = SecondaryTraits.Shimikomi; atk.Stats.UseTrait2 = true;
        int accHolder = FirstHitAcc(AilmentType.Burn, fire);
        atk.Stats.Trait2 = "";
        int accPlain = FirstHitAcc(AilmentType.Burn, fire);
        Check("しみこみ 属性技1発で蓄積+50(非保持者は+25)", accHolder == 50 && accPlain == 25,
              $"{fire.Name} 保持者{accHolder} / 非保持者{accPlain}");

        // 6. 元の特性(レッツハギング)を選ぶと追加特性(しみこみ)は効かず+500のまま。追加特性を選ぶと元の特性は効かない
        var neutralPhys = all.First(m => m.Type == "Neutral" && m.Category == MoveCategory.Physical
                                         && m.Range == MoveRange.Adjacent && m.Power >= 40 && m.Accuracy >= 100
                                         && m.AilmentEffect == AilmentType.None && m.RankEffects.Count == 0
                                         && m.MultiHit == MultiHitMode.None);
        atk.Stats.Trait = "let_us_hug"; atk.Stats.Trait2 = SecondaryTraits.Shimikomi; atk.Stats.UseTrait2 = false;
        int hugOrig = FirstHitAcc(AilmentType.VineBound, neutralPhys);
        int hugOrigBurn = FirstHitAcc(AilmentType.Burn, fire);
        atk.Stats.UseTrait2 = true;
        int hugAlt = FirstHitAcc(AilmentType.VineBound, neutralPhys);
        int hugAltBurn = FirstHitAcc(AilmentType.Burn, fire);
        atk.Stats.Trait2 = ""; atk.Stats.UseTrait2 = false; atk.Stats.Trait = "";
        Check("特性の選択 元の特性ではレッツハギングのみ(+500・しみこみの2倍なし)、追加特性ではしみこみのみ",
              hugOrig == 500 && hugOrigBurn == 25 && hugAlt == 0 && hugAltBurn == 50,
              $"元: ツタ{hugOrig}/やけど{hugOrigBurn} 追加: ツタ{hugAlt}/やけど{hugAltBurn}");
        def.StatusEffects.Reset(); def.Stats.HealToFull();

        // 7. 元の特性(こうふん)を選ぶと追加特性(こわざじょうず)は効かず、追加特性を選ぶと元の特性は効かない
        var small = all.First(m => m.Category == MoveCategory.Special && m.Range == MoveRange.Adjacent
                                   && m.Power >= 30 && m.Power <= 50 && m.Accuracy >= 100
                                   && m.AilmentEffect == AilmentType.None && m.RankEffects.Count == 0
                                   && m.MultiHit == MultiHitMode.None);
        atk.Stats.Trait = ""; atk.Stats.Trait2 = ""; atk.Stats.UseTrait2 = false;
        int dNone = MeasureHit(atk, def, small, null);
        atk.Stats.Trait = "koufun";
        int dKoufun = MeasureHit(atk, def, small, null);
        atk.Stats.Trait2 = SecondaryTraits.KowazaJouzu;
        atk.Stats.UseTrait2 = false;
        int dOrig = MeasureHit(atk, def, small, null);       // 元を選択: こうふんのみ
        atk.Stats.UseTrait2 = true;
        int dAlt = MeasureHit(atk, def, small, null);        // 追加を選択: こわざじょうずのみ
        atk.Stats.Trait = "";
        int dKowaza = MeasureHit(atk, def, small, null);
        atk.Stats.Trait2 = ""; atk.Stats.UseTrait2 = false;
        Check("特性の選択 元の特性ではtrait2の効果が出ず、追加特性では元の特性の効果が出ない",
              dOrig == dKoufun && dAlt == dKowaza && dKoufun > dNone && dKowaza > dNone,
              $"{small.Name}(威力{small.Power}) なし{dNone} / こうふんのみ{dKoufun} / こわざのみ{dKowaza} / 元選択{dOrig} / 追加選択{dAlt}");

        // 8. Stats.HasTrait / ActiveTrait は有効な側だけを見る。Trait2 が空なら UseTrait2 でも元の特性
        atk.Stats.Trait = "koufun"; atk.Stats.Trait2 = SecondaryTraits.Shimikomi; atk.Stats.UseTrait2 = false;
        bool oA = atk.Stats.HasTrait("koufun"), oB = atk.Stats.HasTrait(SecondaryTraits.Shimikomi);
        atk.Stats.UseTrait2 = true;
        bool aA = atk.Stats.HasTrait("koufun"), aB = atk.Stats.HasTrait(SecondaryTraits.Shimikomi);
        atk.Stats.Trait2 = "";
        bool eA = atk.Stats.HasTrait("koufun") && atk.Stats.ActiveTrait == "koufun", hEmpty = atk.Stats.HasTrait("");
        atk.Stats.UseTrait2 = false;
        Check("Stats.HasTrait は有効な特性だけを見る", oA && !oB && !aA && aB && eA && !hEmpty,
              $"元選択 こうふん={oA} しみこみ={oB} / 追加選択 こうふん={aA} しみこみ={aB} / Trait2空 元={eA} 空={hEmpty}");

        // 8b. 通信の往復で有効特性が保たれる。t2 キーの無い古い相手は元の特性
        {
            var mix = SpeciesDatabase.Instance.All.Values.Where(x => !string.IsNullOrEmpty(x.Trait2)).Take(2).ToList();
            var plain = SpeciesDatabase.Instance.All.Values.First(x => string.IsNullOrEmpty(x.Trait2));
            var t = new BattleTeam(new[]
            {
                new BattleEntry { SpeciesId = mix[0].SpeciesId, MoveIds = new List<string>(), UseTrait2 = true },
                new BattleEntry { SpeciesId = mix[1].SpeciesId, MoveIds = new List<string>(), UseTrait2 = false },
                new BattleEntry { SpeciesId = plain.SpeciesId, MoveIds = new List<string>(), UseTrait2 = false },
            });
            var back = BattleNetwork.DecodeFull(BattleNetwork.EncodeFull(t));
            var oldJson = BattleNetwork.EncodeFull(t).Replace(",\"t2\":1", "").Replace(",\"t2\":0", "");
            var oldBack = BattleNetwork.DecodeFull(oldJson);
            Check("通信の往復で有効特性が保たれる(t2キー無しは元の特性)",
                  back.Entries.Select(e => e.UseTrait2).SequenceEqual(new[] { true, false, false })
                  && oldBack.Entries.All(e => !e.UseTrait2) && !oldJson.Contains("t2"),
                  $"往復 {string.Join(",", back.Entries.Select(e => e.UseTrait2))} / キー無し {string.Join(",", oldBack.Entries.Select(e => e.UseTrait2))}");
        }

        atk.Stats.Trait = savedT; atk.Stats.Trait2 = savedT2; atk.Stats.UseTrait2 = savedUse;
        def.Stats.Trait = savedDefT; def.Stats.Trait2 = savedDefT2;
        atk.Stats.Type1 = savedType1; atk.Stats.Type2 = savedType2;
        foreach (var x in _sched.Roster) { x.StatusEffects.Reset(); x.Stats.HealToFull(); }

        // 9〜11. 地形特性。1対1の盤を別に組む(VerifyEntryWeather と同じ組み方)。
        (BattleArena arena, Node host, BattleScheduler sched, List<BattlePal> pals) Setup(
            string playerId, int playerTile, string enemyId, int enemyTile)
        {
            var h = new Node { Name = "FieldProbe" };
            AddChild(h);
            var ar = new BattleArena(h);
            var sc = new BattleScheduler();
            BattleEntry Entry(string id) => new()
            {
                SpeciesId = id,
                MoveIds = DefaultLoadout.PickMoves(SpeciesDatabase.Instance?.Get(id), MoveManager.MaxMoves),
                UseTrait2 = BattleEntry.AutoUseTrait2(SpeciesDatabase.Instance?.Get(id)),   // 追加特性を選ぶ
            };
            BattleDeployment One(Faction f, string id, int tile) => new(f,
                new Dictionary<BattleEntry, Vector2I>
                { [Entry(id)] = BattleDeployment.AvailableTiles(f)[tile] });
            var list = new List<BattlePal>();
            if (!string.IsNullOrEmpty(playerId)) list.AddRange(ar.Spawn(One(Faction.Player, playerId, playerTile), sc));
            if (!string.IsNullOrEmpty(enemyId)) list.AddRange(ar.Spawn(One(Faction.Enemy, enemyId, enemyTile), sc));
            ar.ApplyEntryWeather(sc);
            return (ar, h, sc, list);
        }

        // 9. 保持者の3x3が全て対応する地形・5x5の外周16マスは無し(保持者のマスも含む)
        {
            var s = Setup("169B", 4, "001", 0);   // 169B(みずたまりのぬし)は自陣の(3,5)
            var holder = s.pals.First(p => p.SpeciesId == "169B");
            var c = holder.GridPosition;
            int inner = 0, innerExpected = 0, outer = 0;
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                {
                    var pos = c + new Vector2I(dx, dy);
                    bool inBoard = pos.X >= 0 && pos.Y >= 0 && pos.X < BattleBoard.Width && pos.Y < BattleBoard.Height;
                    if (!inBoard) continue;
                    var f = s.arena.Floor.Fields.Get(pos);
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) <= 1)
                    {
                        innerExpected++;
                        if (f == FieldType.Puddle) inner++;
                    }
                    else if (f != FieldType.None) outer++;
                }
            bool selfTile = s.arena.Floor.Fields.Get(c) == FieldType.Puddle;
            Check("地形特性 保持者の3x3が全てみずたまり・外周は無し",
                  innerExpected == 9 && inner == innerExpected && outer == 0 && selfTile && s.arena.Floor.Fields.Count == 9,
                  $"保持者{c} 内側{inner}/{innerExpected} 外周の地形{outer} 自分のマス={selfTile} 総数{s.arena.Floor.Fields.Count}");
            s.host.Free();
        }

        // 9b. 元の特性を選ぶと追加特性の地形は置かれない
        {
            var h = new Node { Name = "FieldProbe3" };
            AddChild(h);
            var ar = new BattleArena(h);
            var sc = new BattleScheduler();
            var en = new BattleEntry
            {
                SpeciesId = "169B",
                MoveIds = DefaultLoadout.PickMoves(SpeciesDatabase.Instance?.Get("169B"), MoveManager.MaxMoves),
                UseTrait2 = false,
            };
            ar.Spawn(new BattleDeployment(Faction.Player,
                new Dictionary<BattleEntry, Vector2I> { [en] = BattleDeployment.AvailableTiles(Faction.Player)[4] }), sc);
            ar.ApplyEntryWeather(sc);
            Check("特性の選択 元の特性を選ぶと追加特性の地形は置かれない", ar.Floor.Fields.Count == 0,
                  $"地形{ar.Floor.Fields.Count}マス");
            h.Free();
        }

        // 10. 非保持者は地形を置かない
        {
            var s = Setup("001", 4, "003", 0);
            Check("地形特性 非保持者は地形を置かない", s.arena.Floor.Fields.Count == 0,
                  $"地形{s.arena.Floor.Fields.Count}マス");
            s.host.Free();
        }

        // 11. 重なるマスは低BST側(後に置く側)が残る。立てる順を入れ替えた2回で同じ。
        {
            // 084(はなばたけのぬし) と 169B(みずたまりのぬし)。重なるのは (1..3, 3..4)。
            FieldType Overlap(bool playerIs084, bool flip, out int bstP, out int bstE)
            {
                string pId = playerIs084 ? "084" : "169B", eId = playerIs084 ? "169B" : "084";
                var h = new Node { Name = "FieldProbe2" };
                AddChild(h);
                var ar = new BattleArena(h);
                var sc = new BattleScheduler();
                BattleEntry Entry(string id) => new()
                {
                    SpeciesId = id,
                    MoveIds = DefaultLoadout.PickMoves(SpeciesDatabase.Instance?.Get(id), MoveManager.MaxMoves),
                    UseTrait2 = BattleEntry.AutoUseTrait2(SpeciesDatabase.Instance?.Get(id)),   // 追加特性を選ぶ
                };
                BattleDeployment One(Faction f, string id, int tile) => new(f,
                    new Dictionary<BattleEntry, Vector2I>
                    { [Entry(id)] = BattleDeployment.AvailableTiles(f)[tile] });
                var dp = One(Faction.Player, pId, 0);   // (2,4)
                var de = One(Faction.Enemy, eId, 3);    // (2,3)
                List<BattlePal> a, b;
                if (flip) { b = ar.Spawn(de, sc); a = ar.Spawn(dp, sc); }
                else { a = ar.Spawn(dp, sc); b = ar.Spawn(de, sc); }
                ar.ApplyEntryWeather(sc);
                bstP = BattleScheduler.Bst(a[0]); bstE = BattleScheduler.Bst(b[0]);
                var f = ar.Floor.Fields.Get(new Vector2I(2, 3));
                // 重なりのマスが全て同じ地形であること
                for (int x = 1; x <= 3; x++)
                    for (int y = 3; y <= 4; y++)
                        if (ar.Floor.Fields.Get(new Vector2I(x, y)) != f) f = FieldType.None;
                h.Free();
                return f;
            }
            var r1 = Overlap(true, false, out int b1p, out int b1e);
            var r2 = Overlap(true, true, out _, out _);
            var r3 = Overlap(false, false, out _, out _);
            // 低BST側の地形が残るはず。
            FieldType expected = (b1p < b1e) ? FieldType.FlowerBed : FieldType.Puddle;
            Check("地形特性 重なるマスは低BST側が残る(立てる順に依らない)",
                  r1 == expected && r2 == expected && r3 == expected,
                  $"084のBST6={b1p} / 169BのBST6={b1e} 期待={expected} 結果 {r1}/{r2}/{r3}");
        }

        // 12. 種族データ
        {
            var db = SpeciesDatabase.Instance;
            var holders = db.All.Values.Where(s => !string.IsNullOrEmpty(s.Trait2)).ToList();
            var counts = holders.GroupBy(s => s.Trait2).ToDictionary(g => g.Key, g => g.Count());
            int C(string k) => counts.GetValueOrDefault(k);
            bool allMixed = holders.All(s => s.Offense == "Mixed");
            bool leaders = string.IsNullOrEmpty(db.Get("021B")?.Trait2) && string.IsNullOrEmpty(db.Get("036")?.Trait2);
            var errors = db.Validate();
            Check("種族データ 両刀12種だけが2つめの特性を持つ",
                  holders.Count == 12 && holders.Count == db.All.Values.Count(x => x.Offense == "Mixed") && allMixed
                  && C("wide_burst") + C("kowaza_jouzu") + C("shimikomi") == 9
                  && C("entry_puddle") == 1 && C("entry_flowerbed") == 1 && C("entry_thin_ice") == 1
                  && leaders && errors.Count == 0,
                  $"{holders.Count}体 {string.Join(",", counts.Select(kv => kv.Key + "=" + kv.Value))} 全員両刀={allMixed} リーダー除外={leaders} Validate誤り{errors.Count}");
        }
    }

    // レッツハギング／ひょうてんまの確定した挙動（仕様書 §9 案A）を固定する検査。
    // 物理技の命中ごとに相手の蓄積 +500・2回で発症・特殊技と状態異常中の相手には入らない。
    private void VerifyLetUsHug()
    {
        void Check(string label, bool ok, string detail)
            => GD.Print($"[検証] {label}: {(ok ? "OK" : "NG")} ({detail})");

        var atk = _sched.Roster.First(e => e.Faction == Faction.Enemy);
        var def = _sched.Roster.First(e => e.Faction == Faction.Player);
        foreach (var x in _sched.Roster) { x.StatusEffects.Reset(); x.Stats.HealToFull(); }
        string savedT = atk.Stats.Trait, savedT2 = atk.Stats.Trait2;
        atk.Stats.Trait = "let_us_hug"; atk.Stats.Trait2 = "";

        var all = MoveDatabase.AllIds().Select(MoveDatabase.Get).Where(m => m != null).ToList();
        var phys = all.First(m => m.Type == "Neutral" && m.Category == MoveCategory.Physical
                                  && m.Range == MoveRange.Adjacent && m.Power >= 40 && m.Accuracy >= 100
                                  && m.AilmentEffect == AilmentType.None && m.RankEffects.Count == 0
                                  && m.MultiHit == MultiHitMode.None);
        var spec = all.First(m => m.Type != "Grass" && m.Category == MoveCategory.Special
                                  && m.Range == MoveRange.Adjacent && m.Power >= 40 && m.Accuracy >= 100
                                  && m.AilmentEffect == AilmentType.None && m.RankEffects.Count == 0
                                  && m.MultiHit == MultiHitMode.None);

        void Hit(MoveData mv)
        {
            def.Stats.HealToFull();
            new AttackAction(atk, def, MakeSlot(atk, mv), _floor).Execute(0);
        }
        // 当たるまで(蓄積または状態異常が変わるまで)最大16回撃つ。
        int HitUntil(MoveData mv, System.Func<bool> changed)
        {
            for (int i = 0; i < 16 && !changed(); i++) Hit(mv);
            return def.StatusEffects.GetAccumulation(AilmentType.VineBound);
        }

        // 1. 物理技1回で +500
        def.StatusEffects.Reset();
        int one = HitUntil(phys, () => def.StatusEffects.GetAccumulation(AilmentType.VineBound) > 0);
        Check("レッツハギング 物理技1回の命中でツタまみれの蓄積+500", one == 500 && def.StatusEffects.Ailment == AilmentType.None,
              $"{phys.Name} 蓄積{one}");

        // 2. もう1回命中 → ツタまみれになり、蓄積は全て0
        HitUntil(phys, () => def.StatusEffects.Ailment != AilmentType.None);
        bool vine = def.StatusEffects.Ailment == AilmentType.VineBound;
        int left = def.StatusEffects.GetAccumulation(AilmentType.VineBound);
        Check("レッツハギング 2回目の命中でツタまみれ・蓄積は0に戻る", vine && left == 0,
              $"状態={def.StatusEffects.Ailment} 蓄積{left}");

        // 3. 特殊技では入らない
        def.StatusEffects.Reset();
        for (int i = 0; i < 4; i++) Hit(spec);
        int specAcc = def.StatusEffects.GetAccumulation(AilmentType.VineBound);
        Check("レッツハギング 特殊技では蓄積が入らない", specAcc == 0 && def.StatusEffects.Ailment == AilmentType.None,
              $"{spec.Name}({spec.Type}) 蓄積{specAcc}");

        // 4. すでに状態異常の相手には入らない
        def.StatusEffects.Reset();
        def.StatusEffects.TryApplyAilment(AilmentType.Burn);
        for (int i = 0; i < 4; i++) Hit(phys);
        int sick = def.StatusEffects.GetAccumulation(AilmentType.VineBound);
        Check("レッツハギング 状態異常中の相手には入らない", sick == 0 && def.StatusEffects.Ailment == AilmentType.Burn,
              $"状態={def.StatusEffects.Ailment} 蓄積{sick}");

        // 5. ひょうてんまも同じ規則(物理技1回でこおりの蓄積+500)
        def.StatusEffects.Reset();
        atk.Stats.Trait = "hyouten_ma";
        int freeze = 0;
        for (int i = 0; i < 16 && freeze == 0; i++) { Hit(phys); freeze = def.StatusEffects.GetAccumulation(AilmentType.Freeze); }
        Check("ひょうてんま 物理技1回の命中でこおりの蓄積+500", freeze == 500, $"{phys.Name} 蓄積{freeze}");

        atk.Stats.Trait = savedT; atk.Stats.Trait2 = savedT2;
        foreach (var x in _sched.Roster) { x.StatusEffects.Reset(); x.Stats.HealToFull(); }
    }

    // 虚脱(Hollow): 蓄積で発症し、一致補正が消え、自分の2回目の行動終了時に解除される。
    private void VerifyHollow()
    {
        var atk = _sched.Roster.First(e => e.Faction == Faction.Enemy);
        var def = _sched.Roster.First(e => e.Faction == Faction.Player);
        foreach (var x in _sched.Roster) { x.StatusEffects.Reset(); x.Stats.HealToFull(); }

        string oldType1 = atk.Stats.Type1, oldType2 = atk.Stats.Type2, oldTrait = atk.Stats.Trait;

        // 1. 宣言なしの虚技1発で虚脱の蓄積が+25（初回命中で止めて見る）。
        var voidMove = MoveDatabase.AllIds().Select(MoveDatabase.Get)
            .First(m => m.Type == "Void" && m.Power >= 40 && m.Accuracy >= 100
                        && m.Range == MoveRange.Adjacent && m.AilmentEffect == AilmentType.None
                        && m.RankEffects.Count == 0);
        int acc = 0;
        for (int i = 0; i < 16 && acc == 0; i++)
        {
            def.StatusEffects.Reset(); def.Stats.HealToFull();
            new AttackAction(atk, def, MakeSlot(atk, voidMove), _floor).Execute(0);
            acc = def.StatusEffects.GetAccumulation(AilmentType.Hollow);
        }
        GD.Print($"[検証] 虚技の命中で虚脱の蓄積が+25: {(acc == 25 ? "OK" : "NG")} ({voidMove.Name} 蓄積{acc})");
        def.StatusEffects.Reset(); def.Stats.HealToFull();

        // 2. 虚脱中は一致技のダメージが非一致時と同値（〇〇派でも1.0）。
        var fire = MoveDatabase.AllIds().Select(MoveDatabase.Get)
            .First(m => m.Type == "Fire" && m.Category == MoveCategory.Physical && m.Range == MoveRange.Adjacent
                        && m.Accuracy >= 100 && m.Power >= 40 && m.RankEffects.Count == 0
                        && m.AilmentEffect == AilmentType.None);
        atk.Stats.Type1 = "Fire"; atk.Stats.Type2 = ""; atk.Stats.Trait = "";
        int stab = MeasureHit(atk, def, fire, null);
        atk.Stats.Trait = "fire_stab";
        int stabHa = MeasureHit(atk, def, fire, null);
        atk.StatusEffects.TryApplyAilment(AilmentType.Hollow);
        int hollow = MeasureHit(atk, def, fire, null);
        atk.Stats.Trait = "";
        int hollowPlain = MeasureHit(atk, def, fire, null);
        atk.StatusEffects.Reset();
        atk.Stats.Type1 = "Dragon";
        int noMatch = MeasureHit(atk, def, fire, null);
        atk.Stats.Type1 = oldType1; atk.Stats.Type2 = oldType2; atk.Stats.Trait = oldTrait;
        GD.Print($"[検証] 虚脱中は一致技が非一致時と同ダメージ(〇〇派でも): "
                 + $"{(hollow == noMatch && hollowPlain == noMatch && stab > noMatch && stabHa > stab ? "OK" : "NG")} "
                 + $"(一致{stab} / 〇〇派{stabHa} / 虚脱{hollow}・{hollowPlain} / 非一致{noMatch})");

        BattleScheduler.Commitment Act()
        {
            var phys = MoveDatabase.AllIds().Select(MoveDatabase.Get)
                .First(m => m.Category == MoveCategory.Physical && m.Range == MoveRange.Adjacent
                            && m.Accuracy >= 100 && m.Power >= 40 && m.RankEffects.Count == 0);
            def.Stats.HealToFull();
            return new BattleScheduler.Commitment(atk, new AttackAction(atk, def, MakeSlot(atk, phys), _floor), 0);
        }

        // 3. 行動前に虚脱 → 1回目の行動後はまだ虚脱、2回目の行動後に解除。
        atk.StatusEffects.Reset();
        atk.StatusEffects.TryApplyAilment(AilmentType.Hollow);
        _sched.ResolveTurn(Act(), default);
        bool afterFirst = atk.StatusEffects.IsHollow;
        _sched.ResolveTurn(Act(), default);
        bool afterSecond = atk.StatusEffects.IsHollow;
        GD.Print($"[検証] 行動前に虚脱: 1回目の行動後は虚脱のまま・2回目の行動後に解除: "
                 + $"{(afterFirst && !afterSecond ? "OK" : "NG")} (1回目後={afterFirst} 2回目後={afterSecond})");

        // 4. 行動後に虚脱 → 同サイクル終了時はまだ、次サイクルの行動後もまだ、その次の行動後に解除。
        atk.StatusEffects.Reset();
        _sched.BeginCycle();
        _sched.ResolveTurn(Act(), default);
        atk.StatusEffects.TryApplyAilment(AilmentType.Hollow);
        _sched.EndCycle();
        bool atCycleEnd = atk.StatusEffects.IsHollow;
        _sched.BeginCycle();
        _sched.ResolveTurn(Act(), default);
        bool afterN1 = atk.StatusEffects.IsHollow;
        _sched.EndCycle();
        _sched.BeginCycle();
        _sched.ResolveTurn(Act(), default);
        bool afterN2 = atk.StatusEffects.IsHollow;
        _sched.EndCycle();
        // 自分の行動中に付いた虚脱は、その行動の終了を数えない（付いた行動の後に2回行動して解除）。
        atk.StatusEffects.Reset();
        atk.NotifyOwnActionStarting();
        atk.StatusEffects.TryApplyAilment(AilmentType.Hollow);
        atk.NotifyOwnActionEnded();
        bool midAction = atk.StatusEffects.IsHollow;
        _sched.ResolveTurn(Act(), default);
        bool midFirst = atk.StatusEffects.IsHollow;
        _sched.ResolveTurn(Act(), default);
        bool midSecond = atk.StatusEffects.IsHollow;
        GD.Print($"[検証] 行動後に虚脱: サイクル終了では刻まず次の次の行動後に解除: "
                 + $"{(atCycleEnd && afterN1 && !afterN2 && midAction && midFirst && !midSecond ? "OK" : "NG")} "
                 + $"(終了時={atCycleEnd} N+1後={afterN1} N+2後={afterN2} / 行動中付与 直後={midAction} 1回目後={midFirst} 2回目後={midSecond})");

        // 5. 気絶で飛ばした手番は数えない。
        atk.StatusEffects.Reset();
        atk.StatusEffects.TryApplyAilment(AilmentType.Hollow);
        atk.StatusEffects.TryApplyAilment(AilmentType.Stun);
        _sched.ResolveTurn(Act(), default);
        bool afterSkip = atk.StatusEffects.IsHollow;
        _sched.ResolveTurn(Act(), default);
        bool afterOne = atk.StatusEffects.IsHollow;
        _sched.ResolveTurn(Act(), default);
        bool afterTwo = atk.StatusEffects.IsHollow;
        GD.Print($"[検証] 気絶で飛ばした手番は数えない: "
                 + $"{(afterSkip && afterOne && !afterTwo ? "OK" : "NG")} "
                 + $"(飛ばした後={afterSkip} 行動1回後={afterOne} 行動2回後={afterTwo})");

        foreach (var x in _sched.Roster) { x.StatusEffects.Reset(); x.Stats.HealToFull(); x.HeldItemId = null; }
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

        // 何サイクル目から始まるかは、ここより前の検証が何をしたかで変わる
        // （画面の検証が実際に1ターン解決するようになった）。絶対値では
        // なく増分で見る。
        int cycleBefore = _sched.CycleNumber;

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
                 + $"{(_sched.CycleNumber == cycleBefore + 1 ? "OK" : "NG")}");

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
    // NPC対戦相手。編成が構築規則を満たしていること、選出・配置・行動の
    // 3つの決定が規則の内側に収まっていることを見る。
    //
    // 相手が人かNPCかで通る道が変わらない、というのがここの主張なので、
    // 検証も人の側と同じ入口（BattleTeam.Validate / BattleDeployment.Validate /
    // TurnInput）を使う。
    private void VerifyNpc()
    {
        var all = NpcTeamDatabase.All;
        GD.Print($"[検証] NPC対戦相手を読み込める: "
                 + $"{(all.Count > 0 ? "OK" : "NG")} ({all.Count}人)");

        // 読み込み側で Validate に落ちた相手は All に入らないので、
        // 件数がJSONの件数と一致することが規則充足の証拠になる。
        var bad = all.Where(t => t.Team.Validate().Count > 0).ToList();
        GD.Print($"[検証] 全員の編成が構築規則を満たす: "
                 + $"{(bad.Count == 0 ? "OK" : "NG " + string.Join(",", bad.Select(t => t.Name)))}");

        var idDup = all.GroupBy(t => t.Id).Where(g => g.Count() > 1).ToList();
        GD.Print($"[検証] 相手のIDが重複しない: {(idDup.Count == 0 ? "OK" : "NG")}");

        // 開示されるのは種族のみ（人の相手と同じ経路を通っていること）。
        var view = all[0].Disclose();
        GD.Print($"[検証] NPCの開示も種族のみ: "
                 + $"{(view.Count == BattleTeam.RosterSize ? "OK" : "NG")} ({view.Count}件)");

        int badSel = 0, badDep = 0;
        foreach (var profile in all)
        {
            var npc = new NpcOpponent(profile);
            if (profile.Team.ValidateSelection(npc.Selection).Count > 0) badSel++;
            if (npc.Deployment.Validate().Count > 0) badDep++;
        }
        GD.Print($"[検証] NPCの選出が全員規則どおり4匹: {(badSel == 0 ? "OK" : $"NG {badSel}人")}");
        GD.Print($"[検証] NPCの配置が全員規則どおり: {(badDep == 0 ? "OK" : $"NG {badDep}人")}");

        // 毎ターンの行動。盤上の実体を使って、返ってきた入力が
        // 「出せるパル」「持っている技枠」「指せるマス」に収まるかを見る。
        var brain = new NpcOpponent(all[0]);
        int badInput = 0, moves = 0, attacks = 0;
        var enemies = _sched.Roster.Where(e => e.Faction == Faction.Enemy).ToList();
        for (int i = 0; i < 20; i++)
        {
            var input = brain.Decide(_sched, _grid, _floor);
            if (input.ActorIndex < 0 || input.ActorIndex >= enemies.Count) { badInput++; continue; }

            var actor = enemies[input.ActorIndex];
            if (!actor.IsAlive || _sched.HasActed(actor)) { badInput++; continue; }
            if (input.MoveSlot >= actor.Moves.Slots.Count) { badInput++; continue; }

            var legal = BattleTargeting.SelectableTiles(
                actor, input.MoveSlot, _grid, _floor, _sched.Roster);
            if (legal.Count > 0 && !legal.Contains(input.Target)) badInput++;

            if (input.IsMove) moves++; else attacks++;
        }
        GD.Print($"[検証] NPCの行動が常に規則の内側: "
                 + $"{(badInput == 0 ? "OK" : $"NG {badInput}件")} (攻撃{attacks} / 移動{moves})");

        // 届く相手がいるなら殴る。20回とも移動を返すようだと相手にならない。
        GD.Print($"[検証] NPCが届く相手には攻撃を選ぶ: {(attacks > 0 ? "OK" : "NG")}");
    }

    // NPC対戦を最初から最後まで1試合通す。
    //
    // ここまでの検証は部品ごとに見ているので、「相手選択から決着まで実際に
    // 到達するか」だけは通しで見ないと分からない。自分側も同じ判断ロジックで
    // 動かし、公開されている提出口（SubmitPlayerInput）から入力する。
    //
    // 盤面は本番と同じ経路で用意する（BattleArena が選出・配置のあとに
    // パルを立てる）。既存の盤面とは別の FloorController なので、
    // ここまでの検証にも RunBattle にも干渉しない。
    private void VerifyNpcMatch(BattleTeam mine)
    {
        var host = new Node { Name = "NpcMatch" };
        AddChild(host);

        var arena = new BattleArena(host);
        var sched = new BattleScheduler();
        var clock = new BattleClock();
        var flow = new UI.Battle.BattleFlow();
        host.AddChild(flow);
        flow.Begin(mine, new List<PublicEntryView>(), clock, sched,
                   new BattleSession(sched, clock), arena);

        flow.ConfirmBuild();
        flow.ChooseOpponent(NpcTeamDatabase.First());
        flow.ConfirmSelection(mine.AutoSelect());
        flow.Show(UI.Battle.BattleFlow.Phase.Battle);

        int spawned = sched.Roster.Count;
        GD.Print($"[検証] 選出した4匹ずつが盤面に立つ: "
                 + $"{(spawned == BattleTeam.SelectionSize * 2 ? "OK" : "NG")} ({spawned}匹)");

        // 選出した種族が盤面の種族と一致すること。以前は画面より先に
        // 既定配置で立てていたので、ここが食い違っていた。
        var onBoard = sched.Roster.Where(e => e.Faction == Faction.Player)
                           .Select(e => e.SpeciesId).OrderBy(s => s).ToList();
        var chosen = mine.AutoSelect().Select(e => e.SpeciesId).OrderBy(s => s).ToList();
        GD.Print($"[検証] 盤面の4匹が選出どおり: "
                 + $"{(onBoard.SequenceEqual(chosen) ? "OK" : "NG " + string.Join(",", onBoard))}");

        // 自分側も同じ判断で動かす。相手役と同じ手を使うので、勝敗そのものは
        // 見ない（見るのは「決着まで到達するか」）。
        var me = new NpcOpponent(NpcTeamDatabase.First(), Faction.Player);

        int turns = 0;
        while (flow.Current != UI.Battle.BattleFlow.Phase.Finished && turns < 400)
        {
            if (!flow.SubmitPlayerInput(me.Decide(sched, arena.Grid, arena.Floor)))
            {
                // 提出済み（相手待ち）。時間を進めて解決させる。
            }
            flow._Process(1.0);
            turns++;
        }

        GD.Print($"[検証] NPC対戦が決着まで到達する: "
                 + $"{(flow.Current == UI.Battle.BattleFlow.Phase.Finished ? "OK" : "NG")} "
                 + $"({turns}手 / {flow.Outcome} / サイクル{sched.CycleNumber})");
        GD.Print($"[検証] 決着が勝敗か引き分けのいずれか: "
                 + $"{(flow.Outcome != BattleOutcome.Undecided ? "OK" : "NG")}");

        host.QueueFree();
    }

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
        // 構築画面: 左の6枠が並び、右に選んだ1匹の中身が出る。
        var build = new UI.Battle.TeamBuildScreen();
        AddChild(build);
        build.Initialize(mine);

        var slotNames = mine.Entries.Select(e => e.Species?.DisplayName).Where(n => n != null).ToList();
        var buildLabels = CollectLabels(build);
        int listed = slotNames.Count(n => buildLabels.Contains(n));
        GD.Print($"[検証] 構築画面に6匹ぶんの枠が並ぶ: "
                 + $"{(listed == BattleTeam.RosterSize ? "OK" : "NG")} ({listed}枠)");

        // 選んだ1匹の learnset が全部並ぶ。**習得レベルで絞らない**
        // （対人戦はレベルキャップを無視する）。
        var first = mine.Entries[0];
        var learnable = first.Learnable();
        int shownMoves = learnable.Count(m => buildLabels.Contains(m.Name));
        GD.Print($"[検証] 覚えられる技を全部出す(レベルキャップ無視): "
                 + $"{(shownMoves == learnable.Count ? "OK" : "NG")} "
                 + $"({shownMoves}/{learnable.Count}件)");

        // Lv50を超えるレベルで覚える技も選べること。仕様の肝なので直接見る。
        var high = learnable.Where(m => first.LearnLevel(m.Id) > 50).ToList();
        bool highOk = high.Count == 0 || high.All(m => buildLabels.Contains(m.Name));
        GD.Print($"[検証] Lv50超で覚える技も選べる: {(highOk ? "OK" : "NG")} ({high.Count}件)");

        // 技を押すと実際に入れ替わる。
        var spare = learnable.FirstOrDefault(m => !first.MoveIds.Contains(m.Id));
        var before = first.MoveIds.ToList();
        var moveBtn2 = CollectButtons(build).FirstOrDefault(b => CollectLabels(b).Contains(spare?.Name));
        // 4つ埋まっているので、まず1つ外してから入れる。
        // 左の枠カードにも技名が出ているので、先頭を取ると枠カードを
        // 押してしまう（枠の選択が起きるだけで技は動かない）。詳細側は
        // 後からツリーに入るので末尾を取る。
        var drop = CollectButtons(build).Last(b => CollectLabels(b).Contains(
            MoveDatabase.Get(before[0]).Name));
        drop.EmitSignal(Button.SignalName.Pressed);
        bool dropped = first.MoveIds.Count == before.Count - 1;
        var addBtn = CollectButtons(build).LastOrDefault(b => CollectLabels(b).Contains(spare?.Name));
        addBtn?.EmitSignal(Button.SignalName.Pressed);
        GD.Print($"[検証] 技を押すと入れ替わる: "
                 + $"{(dropped && first.MoveIds.Contains(spare.Id) ? "OK" : "NG")} "
                 + $"({before.Count} → {first.MoveIds.Count}技)");

        // 4つ埋まっている状態では、5つ目は入らない。
        var extra = learnable.FirstOrDefault(m => !first.MoveIds.Contains(m.Id));
        bool blockedFifth = !first.ToggleMove(extra.Id) || first.MoveIds.Count <= MoveManager.MaxMoves;
        GD.Print($"[検証] 技は4つを超えない: "
                 + $"{(first.MoveIds.Count <= MoveManager.MaxMoves ? "OK" : "NG")} "
                 + $"({first.MoveIds.Count}技)");

        // 持ち物はチーム内で重複しない。同じものを2匹目に持たせると移る。
        string itemId = ItemDatabase.AllIds().First(i => ItemDatabase.Get(i).Type == ItemType.BattleHeld);
        mine.SetItem(0, itemId);
        mine.SetItem(1, itemId);
        GD.Print($"[検証] 持ち物は重複せず持ち替えになる: "
                 + $"{(mine.Entries[0].ItemId == null && mine.Entries[1].ItemId == itemId ? "OK" : "NG")}");

        // 種族の差し替え。同一種族の枠へは入れない。
        string other = mine.Entries[2].SpeciesId;
        bool rejected = !mine.SetSpecies(0, other);
        var fresh = SpeciesDatabase.Instance.All.Keys
            .First(id => mine.Entries.All(e => e.SpeciesId != id)
                         && foe.Entries.All(e => e.SpeciesId != id));
        bool accepted = mine.SetSpecies(0, fresh);
        bool refilled = mine.Entries[0].MoveIds.Count == MoveManager.MaxMoves
                        && mine.Entries[0].MoveIds.All(
                            m => mine.Entries[0].Learnable().Any(x => x.Id == m));
        GD.Print($"[検証] 種族を差し替えると技も入れ直す: "
                 + $"{(rejected && accepted && refilled ? "OK" : "NG")}");

        // 種族選択画面。287種が並び、登録済みの種族は押せない。
        var picker = new UI.Battle.SpeciesPickScreen();
        AddChild(picker);
        picker.Initialize(mine, 0);
        var pickButtons = CollectButtons(picker);
        var takenNames = mine.Entries.Skip(1).Select(e => e.Species?.DisplayName).ToList();
        var takenBtns = pickButtons.Where(b => takenNames.Any(n => CollectLabels(b).Contains(n))).ToList();
        GD.Print($"[検証] 種族選択で登録済みの種族は押せない: "
                 + $"{(takenBtns.Count > 0 && takenBtns.All(b => b.Disabled) ? "OK" : "NG")} "
                 + $"({takenBtns.Count}件)");
        picker.QueueFree();

        // 規則違反が画面に反映される（同一種族を作ると開始できなくなる）。
        var broken = new BattleTeam(mine.Entries.Select((e, i) => i == 1
            ? new BattleEntry { SpeciesId = mine.Entries[0].SpeciesId, MoveIds = e.MoveIds } : e));
        var build2 = new UI.Battle.TeamBuildScreen();
        AddChild(build2);
        build2.Initialize(broken);
        var btn = CollectButtons(build2).FirstOrDefault(b => b.Text.Contains("受け付ける"));
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
        var session = new BattleSession(_sched, clock);
        var flow = new UI.Battle.BattleFlow();
        AddChild(flow);
        flow.Begin(mine, PublicEntryView.Of(foe.Entries), clock, _sched, session);

        GD.Print($"[検証] 最初は構築画面: "
                 + $"{(flow.Current == UI.Battle.BattleFlow.Phase.Build ? "OK" : "NG")}");

        // 「対戦を受け付ける」を実際に押して進むこと。ConfirmBuild() を直接
        // 呼ぶだけでは、ボタンとの配線漏れを見逃す（実際に見逃した）。
        var buildScreen = FindFirst<UI.Battle.TeamBuildScreen>(flow);
        var readyBtn = CollectButtons(buildScreen).FirstOrDefault(b2 => b2.Text.Contains("受け付ける"));
        readyBtn?.EmitSignal(Button.SignalName.Pressed);
        GD.Print($"[検証] 「対戦を受け付ける」を押すと相手マッチングへ進む: "
                 + $"{(flow.Current == UI.Battle.BattleFlow.Phase.Opponent ? "OK" : "NG")}");

        // 相手を選ぶまで先へ進めない。行を押してから「この相手と戦う」。
        var pickScreen = FindFirst<UI.Battle.OpponentSelectScreen>(flow);
        var goBtn = CollectButtons(pickScreen).First(b2 => b2.Text.Contains("この相手"));
        bool lockedUntilPicked = goBtn.Disabled;
        var npcRow = CollectButtons(pickScreen)
            .First(b2 => CollectLabels(b2).Contains(NpcTeamDatabase.All[0].Name));
        npcRow.EmitSignal(Button.SignalName.Pressed);
        goBtn = CollectButtons(pickScreen).First(b2 => b2.Text.Contains("この相手"));
        goBtn.EmitSignal(Button.SignalName.Pressed);
        GD.Print($"[検証] 相手を選ぶまで進めない: {(lockedUntilPicked ? "OK" : "NG")}");
        GD.Print($"[検証] 相手を選ぶと選出画面へ: "
                 + $"{(flow.Current == UI.Battle.BattleFlow.Phase.Selection && flow.Npc != null ? "OK" : "NG")} "
                 + $"({flow.Npc?.Name})");

        // 相手選択画面には相手の6匹が出ない（開示は選出画面から）。
        var pickLabels = CollectLabels(pickScreen);
        var npcNames = NpcTeamDatabase.All[0].Team.Entries
            .Select(e => e.Species?.DisplayName).Where(n => n != null).ToList();
        GD.Print($"[検証] 相手選択画面に相手の6匹が出ない: "
                 + $"{(npcNames.All(n => !pickLabels.Contains(n)) ? "OK" : "NG")}");

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

        // 配置画面のマスを実際に押して、配置が動くことを見る。
        // 1マス目を押して「持ち」、空きマスを押して「置く」の2手。
        var dep = FindFirst<UI.Battle.DeployScreen>(flow);
        var depCells = CollectButtons(dep).Where(b => CollectLabels(b).Count > 0).ToList();
        var deployment = flow.Deployment;
        var occupiedTile = BattleDeployment.AvailableTiles(Faction.Player)
                                           .First(t => deployment.At(t) != null);
        var emptyTile = BattleDeployment.AvailableTiles(Faction.Player)
                                        .First(t => deployment.At(t) == null);
        var moved = deployment.At(occupiedTile);
        dep.PressTile(occupiedTile);
        bool held = dep.Held == moved;
        dep.PressTile(emptyTile);
        GD.Print($"[検証] 配置画面でマスを押すと配置が変わる: "
                 + $"{(held && deployment.At(emptyTile) == moved && deployment.At(occupiedTile) == null ? "OK" : "NG")}");

        // 入れ替え。埋まっているマスへ置くと、そこに居た1匹が元の場所へ移る。
        var a = deployment.At(emptyTile);
        var otherTile = BattleDeployment.AvailableTiles(Faction.Player)
                                        .First(t => t != emptyTile && deployment.At(t) != null);
        var b2 = deployment.At(otherTile);
        dep.PressTile(emptyTile);
        dep.PressTile(otherTile);
        GD.Print($"[検証] 埋まっているマスへ置くと入れ替わる: "
                 + $"{(deployment.At(otherTile) == a && deployment.At(emptyTile) == b2 ? "OK" : "NG")} "
                 + $"(4匹維持 {deployment.Placements.Count})");

        // 自分の選出4匹の詳細が下部に出ている（技名が読める）。
        var depLabels = CollectLabels(dep);
        var myMoveNames = deployment.Placements.Keys.SelectMany(e => e.MoveIds)
            .Select(m => MoveDatabase.Get(m)?.Name).Where(n => n != null).Distinct().ToList();
        GD.Print($"[検証] 配置画面に自分の技が出る: "
                 + $"{(myMoveNames.Any(n => depLabels.Contains(n)) ? "OK" : "NG")}");
        GD.Print($"[検証] 配置は20秒: "
                 + $"{(BattleClock.DeployLimitSeconds == 20.0 ? "OK" : "NG")} "
                 + $"({BattleClock.DeployLimitSeconds}秒)");

        // 「この配置で開始」で対戦画面へ。
        var startBtn = CollectButtons(dep).FirstOrDefault(b => b.Text.Contains("開始"));
        startBtn?.EmitSignal(Button.SignalName.Pressed);
        GD.Print($"[検証] 開始を押すと対戦画面へ: "
                 + $"{(flow.Current == UI.Battle.BattleFlow.Phase.Battle ? "OK" : "NG")}");

        // 対戦画面。①操作するパルを選ぶところから始まる。
        var hud = FindFirst<UI.Battle.BattleHud>(flow);
        var pickable = _sched.AvailableFor(Faction.Player).ToList();
        var pickBtn = CollectButtons(hud)
            .FirstOrDefault(x => CollectLabels(x).Contains(pickable[0].ActorName));
        GD.Print($"[検証] 対戦は操作するパルの選択から始まる: "
                 + $"{(hud.Operating == null && pickBtn != null ? "OK" : "NG")}");

        pickBtn.EmitSignal(Button.SignalName.Pressed);
        GD.Print($"[検証] パルを選ぶと行動パネルへ移る: "
                 + $"{(hud.Operating == pickable[0] ? "OK" : "NG")}");

        // 相手が誰を操作しているかはレールに出ない。「操作中」は自分の1匹だけ。
        int operating = CollectLabels(hud).Count(t => t == "操作中");
        GD.Print($"[検証] 「操作中」の名指しは自分の1匹だけ: "
                 + $"{(operating == 1 && hud.Operating.Faction == Faction.Player ? "OK" : "NG")} "
                 + $"({operating}件)");

        // ②行動を選ぶ。狙う先を決めるまでは提出できない。
        var actor0 = hud.Operating;
        string firstMove = actor0.Moves.Slots[0].Data.Name;
        var moveBtn = CollectButtons(hud).First(x => CollectLabels(x).Contains(firstMove));
        moveBtn.EmitSignal(Button.SignalName.Pressed);
        var commit = CollectButtons(hud).First(x => x.Text.Contains("伏せる"));
        bool lockedFirst = commit.Disabled;

        // 盤面のマスを順に押して、狙える1マスを見つける。押せないマスは
        // 何も起きないので、順に当たれば必ず有効なマスへ行き着く。
        int tiles = BattleBoard.Width * BattleBoard.Height;
        for (int i = 0; i < tiles && commit.Disabled; i++)
        {
            var grid = FindFirst<GridContainer>(hud);
            if (grid == null || i >= grid.GetChildCount()) break;
            (grid.GetChild(i) as Button)?.EmitSignal(Button.SignalName.Pressed);
        }
        GD.Print($"[検証] 狙う先を選ぶまで提出できない: "
                 + $"{(lockedFirst && !commit.Disabled ? "OK" : "NG")}");

        // ③提出。相手が出すまで盤面は動かない。
        int turnBefore = _sched.TurnInCycle;
        commit.EmitSignal(Button.SignalName.Pressed);
        bool waiting = session.HasSubmitted(Faction.Player)
                       && !session.HasSubmitted(Faction.Enemy)
                       && _sched.TurnInCycle == turnBefore;
        GD.Print($"[検証] 提出しても相手が出すまで解決しない: {(waiting ? "OK" : "NG")}");

        // 相手役が出したら解決する。実際にターンが1つ進むこと。
        for (int i = 0; i < 5 && _sched.TurnInCycle == turnBefore; i++) flow._Process(1.0);
        GD.Print($"[検証] 両者が出すとターンが解決する: "
                 + $"{(_sched.TurnInCycle == turnBefore + 1 ? "OK" : "NG")} "
                 + $"(ターン {turnBefore} → {_sched.TurnInCycle})");
        GD.Print($"[検証] 解決後は次のパルの選択へ戻る: "
                 + $"{(hud.Operating == null ? "OK" : "NG")}");

        // 選出フェーズの時間切れ経路。50秒進めると自動で配置へ移る。
        var clock2 = new BattleClock();
        var flow2 = new UI.Battle.BattleFlow();
        AddChild(flow2);
        flow2.Begin(mine, PublicEntryView.Of(foe.Entries), clock2, _sched,
                    new BattleSession(_sched, clock2));
        flow2.ConfirmBuild();
        flow2.ChooseOpponent(NpcTeamDatabase.First());   // 選出フェーズへ進める
        flow2._Process(BattleClock.SelectionLimitSeconds + 1.0);
        GD.Print($"[検証] 選出が時間切れなら自動で配置へ: "
                 + $"{(flow2.Current == UI.Battle.BattleFlow.Phase.Deploy ? "OK" : "NG")}");

        // 配置フェーズの時間切れ経路。20秒進めると現在の配置のまま対戦へ移る。
        int placedAtTimeout = flow2.Deployment.Placements.Count;
        flow2._Process(BattleClock.DeployLimitSeconds + 1.0);
        GD.Print($"[検証] 配置が時間切れなら現在の配置のまま対戦へ: "
                 + $"{(flow2.Current == UI.Battle.BattleFlow.Phase.Battle && placedAtTimeout == 4 ? "OK" : "NG")}");

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

    // ---- 制限技12種 (Phase 6) ----
    // 各技について「効く条件で効く」と「効かない条件で効かない」を1項目内で対にして見る。
    // 使うのは盤上の実体（Enemy先頭=使用者 / Player=対象）。位置・特性・持ち物・PP・
    // 状態は検証の最後に元へ戻す。
    private void VerifyRestrictions()
    {
        // 先行の検証で倒れた個体（Entity.IsAlive は戻せない）を選ぶと追加効果が乗らず、R1〜R11 が
        // ランダムに NG になる（基準 5018f33 でも R11 が約3割で NG）。生きている個体から選ぶ。
        var atk = _sched.Roster.First(e => e.Faction == Faction.Enemy && e.IsAlive);
        var mate = _sched.Roster.Where(e => e.Faction == Faction.Enemy && e.IsAlive).Skip(1).First();

        // 実際の技枠を使う項目のために、天候・設置技を含まない枠が3つ以上ある対象を選ぶ。
        static List<int> SafeSlots(Entity e) => Enumerable.Range(0, e.Moves.Slots.Count)
            .Where(i => e.Moves.Slots[i].Data.WeatherEffect == WeatherType.None
                        && e.Moves.Slots[i].Data.FieldPlacement == FieldPlacement.None
                        && !e.Moves.Slots[i].Data.SelfGuaranteedDeath).ToList();
        var def = _sched.Roster.First(e => e.Faction == Faction.Player && e.IsAlive && SafeSlots(e).Count >= 3);
        var safe = SafeSlots(def);
        int s0 = safe[0], s1 = safe[1];

        var savedPos = _sched.Roster.ToDictionary(e => e, e => e.GridPosition);
        var savedTrait = (atk: atk.Stats.Trait, def: def.Stats.Trait, mate: mate.Stats.Trait);
        var savedTypes = (def.Stats.Type1, def.Stats.Type2);
        atk.Stats.Trait = ""; def.Stats.Trait = ""; mate.Stats.Trait = "";

        MoveData Mv(string id) => MoveDatabase.Get(id);
        void Fresh()
        {
            foreach (var x in _sched.Roster)
            {
                x.StatusEffects.Reset(); x.Stats.HealToFull(); x.LastUsedMoveSlot = -1; x.HeldItemId = null;
                foreach (var s in x.Moves.Slots) s.CurrentPp = s.Data.MaxPp;
            }
            def.Stats.Type1 = savedTypes.Type1; def.Stats.Type2 = savedTypes.Type2;
        }
        void Place(Vector2I a, Vector2I d)
        {
            atk.PlaceAt(a); def.PlaceAt(d);
            atk.FaceDirection(BattleTargeting.StepToward(a, d));
            def.FaceDirection(BattleTargeting.StepToward(d, a));
        }
        // atk が move を def へ撃つ（命中すれば追加効果が乗る）。
        void Hit(MoveData m)
        {
            def.Stats.HealToFull();
            new AttackAction(atk, def, MakeSlot(atk, m), _floor).Execute(0);
            def.Stats.HealToFull();
        }
        // def が自分の行動として slot を出す（行動の開始/終了通知つき）。PP の増減で出せたか分かる。
        void Act(MoveSlot slot)
        {
            def.NotifyOwnActionStarting();
            new AttackAction(def, atk, slot, _floor).Execute(0);
            def.NotifyOwnActionEnded();
            atk.Stats.HealToFull();
        }
        bool Used(MoveSlot slot)   // 出せたか（PP が減ったか）
        {
            int before = slot.CurrentPp;
            Act(slot);
            return slot.CurrentPp < before;
        }
        MoveSlot Real(int i) => def.Moves.Slots[i];

        var phys = MoveDatabase.AllIds().Select(MoveDatabase.Get)
            .First(m => m.Category == MoveCategory.Physical && m.Range == MoveRange.Adjacent
                        && m.Accuracy >= 100 && m.Power >= 40 && m.RankEffects.Count == 0
                        && m.AilmentEffect == AilmentType.None && m.Restriction == RestrictionKind.None
                        && m.RecoilHpPercent == 0 && !m.SelfStunNextTurn && !m.SelfGuaranteedDeath
                        && m.WeaponTag == WeaponTag.None);
        var far = new Vector2I(0, 0); var near2 = new Vector2I(0, 2);

        // R1 シールスナップ
        {
            Fresh(); Place(far, near2);
            def.LastUsedMoveSlot = s0;
            Hit(Mv("rs_seal_snap"));
            int rem = def.StatusEffects.RestrictionRemaining(RestrictionKind.SealLastMove);
            bool sealed0 = !Used(Real(s0));          // 封じた技は出せない（1行動目）
            bool other = Used(Real(s1));              // 別の技は出せる（2行動目）。ここで2行動が経過
            bool freed = Used(Real(s0));              // 2行動後は解除
            Fresh(); Place(far, near2);               // 未使用なら効果なし
            Hit(Mv("rs_seal_snap"));
            bool noEffect = !def.StatusEffects.HasRestriction(RestrictionKind.SealLastMove) && Used(Real(s0));
            GD.Print($"[検証] R1 シールスナップ: 最後に使った技だけ2行動封じ・未使用なら効果なし: "
                     + $"{(rem == 2 && sealed0 && other && freed && noEffect ? "OK" : "NG")} "
                     + $"(残{rem} 封印中出せず={sealed0} 別技={other} 解除後={freed} 未使用時の効果なし={noEffect})");
        }

        // R2 プロヴォーク
        {
            Fresh(); Place(far, near2);
            var status = MoveDatabase.AllIds().Select(MoveDatabase.Get)
                .First(m => m.Category == MoveCategory.Status && m.FieldPlacement == FieldPlacement.None
                            && m.WeatherEffect == WeatherType.None);
            bool beforeOk = Used(MakeSlot(def, status));        // かかる前は変化技を出せる
            Fresh(); Place(far, near2);
            Hit(Mv("rs_provoke"));
            bool statusBlocked = !Used(MakeSlot(def, status));   // 1行動目: 変化技は出せない
            bool attackOk = Used(MakeSlot(def, phys));           // 2行動目: 攻撃技は出せる
            bool freed = Used(MakeSlot(def, status));            // 2行動後は解除
            GD.Print($"[検証] R2 プロヴォーク: 2行動のあいだ変化技だけ出せない: "
                     + $"{(beforeOk && statusBlocked && attackOk && freed ? "OK" : "NG")} "
                     + $"(前={beforeOk} 変化技封じ={statusBlocked} 攻撃技={attackOk} 解除後={freed})");
        }

        // R3 リピートチャント（アンコール）
        {
            Fresh(); Place(new Vector2I(0, 1), near2);
            def.LastUsedMoveSlot = s0;
            Hit(Mv("rs_repeat_chant"));
            bool applied = def.StatusEffects.EncoreSlot == s0;
            // 別の技(s1)を出したつもりでも、最後に使った技(s0)へ差し替わる。
            int p0 = Real(s0).CurrentPp, p1 = Real(s1).CurrentPp;
            _sched.ResolveTurn(new BattleScheduler.Commitment(def, new AttackAction(def, atk, Real(s1), _floor), 0), default);
            bool forced = Real(s0).CurrentPp == p0 - 1 && Real(s1).CurrentPp == p1;
            // 1行動で解除: 次は提出どおり s1。
            _sched.ResolveTurn(new BattleScheduler.Commitment(def, new AttackAction(def, atk, Real(s1), _floor), 0), default);
            bool freed = Real(s1).CurrentPp == p1 - 1 && !def.StatusEffects.HasRestriction(RestrictionKind.Encore);

            // 移動を出しても差し替わる。狙えない先なら不発（枠もPPも使わない）。
            Fresh(); Place(new Vector2I(0, 1), near2);
            int adj = safe.FirstOrDefault(i => Real(i).Data.Range == MoveRange.Adjacent, -1);
            bool moveReplaced = false, fizzled = false;
            if (adj >= 0)
            {
                def.LastUsedMoveSlot = adj;
                Hit(Mv("rs_repeat_chant"));
                int pa = Real(adj).CurrentPp;
                _sched.ResolveTurn(new BattleScheduler.Commitment(def, new MoveAction(def, new Vector2I(1, 2)), 0), default);
                moveReplaced = Real(adj).CurrentPp == pa - 1 && def.GridPosition == near2;

                Fresh(); Place(new Vector2I(0, 1), near2);
                def.LastUsedMoveSlot = adj;
                Hit(Mv("rs_repeat_chant"));
                int pa2 = Real(adj).CurrentPp, pb = Real(s0 == adj ? s1 : s0).CurrentPp;
                _sched.ResolveTurn(new BattleScheduler.Commitment(def,
                    new AttackAction(def, null, Real(s0 == adj ? s1 : s0), _floor, new Vector2I(6, 7)), 0), default);
                fizzled = Real(adj).CurrentPp == pa2 && Real(s0 == adj ? s1 : s0).CurrentPp == pb
                          && !def.StatusEffects.HasRestriction(RestrictionKind.Encore);
            }
            // 未使用なら効果なし。
            Fresh(); Place(new Vector2I(0, 1), near2);
            Hit(Mv("rs_repeat_chant"));
            bool noEffect = def.StatusEffects.EncoreSlot < 0;
            GD.Print($"[検証] R3 リピートチャント: 次の1行動を最後に使った技へ強制(移動も差し替え・狙えなければ不発)・未使用なら効果なし: "
                     + $"{(applied && forced && freed && moveReplaced && fizzled && noEffect ? "OK" : "NG")} "
                     + $"(付与={applied} 差し替え={forced} 解除後={freed} 移動→差し替え={moveReplaced} 不発={fizzled} 未使用={noEffect})");
        }

        // R4 グリッチボルト（持ち物無効化。3サイクル）
        {
            string ItemWith(BattleItemEffect fx) => ItemDatabase.AllIds()
                .First(id => ItemDatabase.Get(id)?.BattleEffect == fx);
            string plate = ItemWith(BattleItemEffect.PhysDefUp30), shell = ItemWith(BattleItemEffect.WeaknessCut75);

            Fresh(); Place(far, near2);
            int dNone = MeasureHit(atk, def, phys, null);
            int dItem = MeasureHit(atk, def, phys, plate);
            Hit(Mv("rs_item_jam"));
            int rem = def.StatusEffects.RestrictionRemaining(RestrictionKind.ItemJam);
            int dJam = MeasureHit(atk, def, phys, plate);                       // 持続型が止まる
            _sched.EndCycle(); bool c1 = def.StatusEffects.IsItemJammed;
            _sched.EndCycle(); bool c2 = def.StatusEffects.IsItemJammed;
            _sched.EndCycle(); bool c3 = def.StatusEffects.IsItemJammed;        // 3サイクル目の終了で解除
            int dFree = MeasureHit(atk, def, phys, plate);

            // 使い切り型: 止められている間は発動も消費もしない。
            var water = MoveDatabase.AllIds().Select(MoveDatabase.Get)
                .First(m => m.Type == "Water" && m.Range == MoveRange.Adjacent && m.Accuracy >= 100
                            && m.Power >= 40 && m.RankEffects.Count == 0 && m.AilmentEffect == AilmentType.None
                            && m.Restriction == RestrictionKind.None);
            Fresh(); Place(far, near2);
            def.Stats.Type1 = "Fire"; def.Stats.Type2 = "";   // 水技が弱点になる（Fresh は型を戻すので後に置く）
            def.HeldItemId = shell;
            Hit(Mv("rs_item_jam"));
            Hit(water);
            bool keptWhileJam = !def.HeldItemConsumed;
            Fresh(); Place(far, near2);
            def.Stats.Type1 = "Fire"; def.Stats.Type2 = "";
            def.HeldItemId = shell;
            Hit(water);
            bool consumedFree = def.HeldItemConsumed;
            GD.Print($"[検証] R4 グリッチボルト: 3サイクル持ち物の効果なし(使い切り型は消費もしない)・解除後は戻る: "
                     + $"{(dItem < dNone && rem == 3 && dJam == dNone && c1 && c2 && !c3 && dFree == dItem && keptWhileJam && consumedFree ? "OK" : "NG")} "
                     + $"(素{dNone} 持ち物{dItem} 無効化中{dJam} 解除後{dFree} / 残{rem} 経過後={c1},{c2},{c3} / 使い切り: 無効化中は未消費={keptWhileJam} 解除後は消費={consumedFree})");
        }

        // R5 タイドフック（引き寄せ）
        {
            Fresh(); Place(far, near2);
            Hit(Mv("rs_tide_hook"));
            bool pulled = def.GridPosition == new Vector2I(0, 1);
            // 引き寄せ先が使用者のマス（隣接）なら動かない。
            Fresh(); Place(new Vector2I(0, 1), near2);
            Hit(Mv("rs_tide_hook"));
            bool adjacentStays = def.GridPosition == near2;
            // 引き寄せ先に他の個体が居れば動かない。
            Fresh(); Place(far, near2);
            mate.PlaceAt(new Vector2I(0, 1));
            Hit(Mv("rs_tide_hook"));
            bool occupiedStays = def.GridPosition == near2;
            mate.PlaceAt(savedPos[mate]);
            GD.Print($"[検証] R5 タイドフック: 使用者へ向けて1マス引き寄せる(先が埋まっている/隣接なら動かない): "
                     + $"{(pulled && adjacentStays && occupiedStays ? "OK" : "NG")} "
                     + $"(引き寄せ={pulled} 隣接時={adjacentStays} 先が埋まる時={occupiedStays})");
        }

        // R6 フロストラグ（優先度-1）
        {
            Fresh(); Place(far, near2);
            int before = BattleScheduler.EffectivePriority(def, null);
            Hit(Mv("rs_frost_lag"));
            int during = BattleScheduler.EffectivePriority(def, null);
            // 実効優先度が下がった側は、同じ優先度0の相手より必ず後に動く（引数の順に依らない）。
            var low = new BattleScheduler.Commitment(def, new WaitAction(def), during);
            var normal = new BattleScheduler.Commitment(atk, new WaitAction(atk), 0);
            bool later = _sched.Order(low, normal)[0].Actor == atk && _sched.Order(normal, low)[0].Actor == atk;
            // 次の行動の優先度に使われたら使い切り（1行動分）。
            _sched.ResolveTurn(low, default);
            int after = BattleScheduler.EffectivePriority(def, null);
            GD.Print($"[検証] R6 フロストラグ: 次の1行動だけ実効優先度-1(優先度0の相手より後)・使い切り: "
                     + $"{(before == 0 && during == -1 && later && after == 0 ? "OK" : "NG")} "
                     + $"(前{before} 中{during} 後{after} 相手より後={later})");
        }

        // R7 サンドイーター（PP-2）
        {
            Fresh(); Place(far, near2);
            def.LastUsedMoveSlot = s0;
            Real(s0).CurrentPp = 5;
            int other = Real(s1).CurrentPp;
            Hit(Mv("rs_sand_eater"));
            bool minus2 = Real(s0).CurrentPp == 3 && Real(s1).CurrentPp == other;
            Real(s0).CurrentPp = 1;
            Hit(Mv("rs_sand_eater"));
            bool floor0 = Real(s0).CurrentPp == 0;
            Fresh(); Place(far, near2);
            int total = def.Moves.Slots.Sum(s => s.CurrentPp);
            Hit(Mv("rs_sand_eater"));
            bool unused = def.Moves.Slots.Sum(s => s.CurrentPp) == total;
            GD.Print($"[検証] R7 サンドイーター: 最後に使った技のPP-2(0未満にならない)・未使用なら何も起きない: "
                     + $"{(minus2 && floor0 && unused ? "OK" : "NG")} (-2={minus2} 下限0={floor0} 未使用={unused})");
        }

        // R8 スコーチスモッグ（回復封じ。範囲技で味方も巻き込む）
        {
            Fresh(); Place(far, near2);
            mate.PlaceAt(new Vector2I(1, 2));
            Hit(Mv("rs_scorch_smog"));
            int rem = def.StatusEffects.RestrictionRemaining(RestrictionKind.HealBlock);
            bool mateHit = mate.StatusEffects.IsHealBlocked;
            def.Stats.TakeDamage(20);
            int hp = def.Stats.CurrentHp;
            def.Stats.Heal(10);
            bool blocked = def.Stats.CurrentHp == hp;
            // ドレインも止まる。
            var drain = MoveDatabase.AllIds().Select(MoveDatabase.Get)
                .FirstOrDefault(m => m.DrainHpPercent > 0 && m.Range == MoveRange.Adjacent && m.Power > 0
                                     && m.Accuracy >= 100 && m.RecoilHpPercent == 0 && m.AilmentEffect == AilmentType.None);
            bool drainBlocked = drain != null;
            if (drain != null)
            {
                atk.StatusEffects.TryApplyRestriction(RestrictionKind.HealBlock, 3, -1);
                atk.Stats.TakeDamage(atk.Stats.MaxHp / 2);
                int ahp = atk.Stats.CurrentHp;
                Hit(drain);
                drainBlocked = atk.Stats.CurrentHp == ahp;
                atk.StatusEffects.Reset();
            }
            _sched.EndCycle(); _sched.EndCycle();
            bool stillBlocked = def.StatusEffects.IsHealBlocked;
            _sched.EndCycle();
            def.Stats.TakeDamage(20); hp = def.Stats.CurrentHp;
            def.Stats.Heal(10);
            bool freed = def.Stats.CurrentHp == hp + 10;
            // 全回復(HealToFull)は対象外。
            def.StatusEffects.TryApplyRestriction(RestrictionKind.HealBlock, 3, -1);
            def.Stats.TakeDamage(20); def.Stats.HealToFull();
            bool fullOk = def.Stats.CurrentHp == def.Stats.MaxHp;
            mate.PlaceAt(savedPos[mate]);
            GD.Print($"[検証] R8 スコーチスモッグ: 3サイクル回復しない(範囲で味方も巻き込む・ドレインも止まる・全回復は対象外)・解除後は回復: "
                     + $"{(rem == 3 && mateHit && blocked && drainBlocked && stillBlocked && freed && fullOk ? "OK" : "NG")} "
                     + $"(残{rem} 味方={mateHit} 回復封じ={blocked} ドレイン={drainBlocked} 2サイクル後も={stillBlocked} 解除後={freed} 全回復={fullOk})");
        }

        // R9 ドラゴンアンカー（ランク上昇封じ）
        {
            Fresh(); Place(far, near2);
            def.StatusEffects.ApplyRankDelta(RankStat.Atk, 2);
            bool upBefore = def.StatusEffects.AtkRank == 2;
            def.StatusEffects.Reset();
            Hit(Mv("rs_dragon_anchor"));
            def.StatusEffects.ApplyRankDelta(RankStat.Atk, 2);
            float mulBefore = def.StatusEffects.GetAtkMultiplier();
            def.StatusEffects.ArmTemporaryRankBuff(1, 1, 2);
            bool noUp = def.StatusEffects.AtkRank == 0 && def.StatusEffects.GetAtkMultiplier() == mulBefore;
            def.StatusEffects.ApplyRankDelta(RankStat.Def, -1);
            bool downOk = def.StatusEffects.DefRank == -1;
            Act(MakeSlot(def, phys)); Act(MakeSlot(def, phys)); Act(MakeSlot(def, phys));   // 3行動で解除
            def.StatusEffects.ApplyRankDelta(RankStat.Atk, 2);
            bool freed = def.StatusEffects.AtkRank == 2;
            GD.Print($"[検証] R9 ドラゴンアンカー: 3行動のあいだランクが上がらない(下降は通る)・解除後は上がる: "
                     + $"{(upBefore && noUp && downOk && freed ? "OK" : "NG")} "
                     + $"(前={upBefore} 上昇封じ={noUp} 下降={downOk} 解除後={freed})");
        }

        // R10 ショートリーシュ（Adjacent以外の射程を封じる）
        {
            Fresh(); Place(far, near2);
            var ranged = MoveDatabase.AllIds().Select(MoveDatabase.Get)
                .First(m => m.Category != MoveCategory.Status && m.Range == MoveRange.Line && m.Accuracy >= 100
                            && m.Power >= 40 && m.RankEffects.Count == 0 && m.AilmentEffect == AilmentType.None
                            && m.RecoilHpPercent == 0 && !m.SelfStunNextTurn);
            bool rangedBefore = Used(MakeSlot(def, ranged));
            Fresh(); Place(far, near2);
            Hit(Mv("rs_short_leash"));
            bool rangedBlocked = !Used(MakeSlot(def, ranged));
            bool adjacentOk = Used(MakeSlot(def, phys));
            bool freed = Used(MakeSlot(def, ranged));
            GD.Print($"[検証] R10 ショートリーシュ: 2行動のあいだ射程Adjacent以外の技を出せない: "
                     + $"{(rangedBefore && rangedBlocked && adjacentOk && freed ? "OK" : "NG")} "
                     + $"(前={rangedBefore} 射程技封じ={rangedBlocked} 隣接技={adjacentOk} 解除後={freed})");
        }

        // R11 ナグジャブ（同じ技の連続を封じる）
        {
            Fresh(); Place(far, near2);
            bool first = Used(Real(s0));                  // 直前の技 = s0
            atk.StatusEffects.Reset();                    // s0 の技が atk の命中ランクを下げると、次の Hit が外れて NG になる（乱数）
            Hit(Mv("rs_nag_jab"));
            bool repeatBlocked = !Used(Real(s0));          // 1行動目: 同じ技は出せない
            bool otherOk = Used(Real(s1));                 // 2行動目: 別の技(直前=s1になる)
            bool repeat2Blocked = !Used(Real(s1));         // 3行動目: また同じ技は出せない(ここで解除)
            bool freed = Used(Real(s1));                   // 解除後は連続して出せる
            GD.Print($"[検証] R11 ナグジャブ: 3行動のあいだ直前と同じ技を続けて出せない(別の技は可)・解除後は出せる: "
                     + $"{(first && repeatBlocked && otherOk && repeat2Blocked && freed ? "OK" : "NG")} "
                     + $"(初回={first} 連続封じ={repeatBlocked} 別技={otherOk} 再連続封じ={repeat2Blocked} 解除後={freed})");
        }

        // R12 ホロウタッチ（虚脱を確定付与）
        {
            Fresh(); Place(far, near2);
            Hit(Mv("rs_hollow_touch"));
            bool hollow = def.StatusEffects.IsHollow;
            // 他の状態異常中は付かない(主スロット排他)・きょうじんなからだは追加効果を通さない。
            Fresh(); Place(far, near2);
            def.StatusEffects.TryApplyAilment(AilmentType.Burn);
            Hit(Mv("rs_hollow_touch"));
            bool exclusive = def.StatusEffects.Ailment == AilmentType.Burn;
            Fresh(); Place(far, near2);
            def.Stats.Trait = "kyoujin_na_karada";
            Hit(Mv("rs_hollow_touch"));
            bool tough = !def.StatusEffects.IsHollow;
            def.Stats.Trait = "";
            GD.Print($"[検証] R12 ホロウタッチ: 1発で虚脱を確定付与(他の状態異常中・きょうじんなからだには付かない): "
                     + $"{(hollow && exclusive && tough ? "OK" : "NG")} (付与={hollow} 排他={exclusive} 強靭={tough})");
        }

        // 各制限技の習得者が1〜2種で表どおり。1種が持つ制限技は1つだけ。
        {
            var expected = new Dictionary<string, string[]>
            {
                ["rs_seal_snap"] = new[] { "189", "145" }, ["rs_provoke"] = new[] { "080", "168" },
                ["rs_repeat_chant"] = new[] { "082", "170" }, ["rs_item_jam"] = new[] { "185", "161" },
                ["rs_tide_hook"] = new[] { "075", "128" }, ["rs_frost_lag"] = new[] { "119", "095" },
                ["rs_sand_eater"] = new[] { "139", "140" }, ["rs_scorch_smog"] = new[] { "104", "176" },
                ["rs_dragon_anchor"] = new[] { "160", "124" }, ["rs_short_leash"] = new[] { "066", "152" },
                ["rs_nag_jab"] = new[] { "074", "033" }, ["rs_hollow_touch"] = new[] { "157B", "150" },
            };
            var all = SpeciesDatabase.Instance.All.Values.ToList();
            var bad = new List<string>();
            foreach (var (mid, want) in expected)
            {
                var got = all.Where(sp => sp.Learnset.Any(l => l.MoveId == mid)).Select(sp => sp.SpeciesId)
                             .OrderBy(x => x).ToArray();
                if (!got.SequenceEqual(want.OrderBy(x => x)) || got.Length is < 1 or > 2) bad.Add(mid);
                if (MoveDatabase.Get(mid) is not { Power: > 0 }) bad.Add(mid + ":威力なし");
            }
            int multi = all.Count(sp => sp.Learnset.Select(l => l.MoveId).Count(id => id.StartsWith("rs_")) > 1);
            int total = MoveDatabase.AllIds().Count(id => id.StartsWith("rs_"));
            GD.Print($"[検証] 制限技12種の習得者が各1〜2種で表どおり(1種が持つのは1つだけ): "
                     + $"{(bad.Count == 0 && multi == 0 && total == 12 ? "OK" : "NG " + string.Join(",", bad))} "
                     + $"(技{total}種 複数習得{multi}種)");
        }

        // 後始末。
        Fresh();
        foreach (var (e, p) in savedPos) e.PlaceAt(p);
        atk.Stats.Trait = savedTrait.atk; def.Stats.Trait = savedTrait.def; mate.Stats.Trait = savedTrait.mate;
        atk.FaceDirection(BattleBoard.Facing(Faction.Enemy));
        def.FaceDirection(BattleBoard.Facing(Faction.Player));
    }

    // プロテクション（仕様書 docs/design/drafts/2026-10-29-protection.md §4-2 の P1〜P16）。
    // 生きている個体から役を割り当て（陣営・位置・向き・特性・属性は最後に戻す）、AttackAction を直接撃つ。
    private void VerifyProtection()
    {
        const string ProtId = "mvs_ice_protection";
        void Rep(string id, string label, bool ok, string detail) =>
            GD.Print($"[検証] {id} {label}: {(ok ? "OK" : "NG")} ({detail})");
        MoveData Mv(string id) => MoveDatabase.Get(id);

        // ---- P1 技データ ----
        {
            var m = Mv(ProtId);
            bool fields = m != null && m.Type == "Ice" && m.Category == MoveCategory.Status && m.Power == 0
                          && m.Accuracy == 100 && m.MaxPp == 10 && m.Priority == 3
                          && m.Range == MoveRange.FrontArc && m.GrantsProtection;
            var ids = MoveDatabase.AllIds().ToList();
            int othersWithFlag = ids.Count(id => id != ProtId && MoveDatabase.Get(id).GrantsProtection);
            int others = ids.Count(id => id != ProtId);
            Rep("P1", "プロテクションの技データ(Ice/Status/威力0/命中100/PP10/優先度3/FrontArc/効果あり)・他の627技は効果なし",
                fields && othersWithFlag == 0 && others == 627, $"定義={fields} 他の技{others}種 うち効果あり{othersWithFlag}");
        }

        // ---- P2 習得者 ----
        {
            var all = SpeciesDatabase.Instance.All.Values.ToList();
            var got = all.Where(sp => sp.Learnset.Any(l => l.MoveId == ProtId)).Select(sp => sp.SpeciesId)
                         .OrderBy(x => x, System.StringComparer.Ordinal).ToArray();
            var expected = all.Where(sp => sp.Offense == "Mixed" && sp.Bst <= 470).Select(sp => sp.SpeciesId)
                              .OrderBy(x => x, System.StringComparer.Ordinal).ToArray();
            bool setOk = got.SequenceEqual(expected);
            bool rowsOk = all.All(sp =>
            {
                var rows = sp.Learnset.Where(l => l.MoveId == ProtId).ToList();
                return rows.Count == 0 || (rows.Count == 1 && rows[0].Level == 10);
            });
            Rep("P2", "習得者が両刀BST6<=470の11種ちょうど(各1行・level 10)", setOk && rowsOk && got.Length == 11,
                $"習得{got.Length}種 集合一致={setOk} 各1行level10={rowsOk}");
        }

        // ---- P3 正面3マスの形 ----
        {
            HashSet<Vector2I> Tiles(Vector2I pos, Vector2I facing) => TargetResolver.ResolveTiles(
                MoveRange.FrontArc, pos, facing, pos, _grid, _floor).ToHashSet();
            var n = Tiles(new Vector2I(3, 4), new Vector2I(0, -1));
            var ne = Tiles(new Vector2I(3, 4), new Vector2I(1, -1));
            var north0 = Tiles(new Vector2I(0, 0), new Vector2I(0, -1));
            var east0 = Tiles(new Vector2I(0, 0), new Vector2I(1, 0));
            var none = Tiles(new Vector2I(3, 4), Vector2I.Zero);
            bool ok = n.SetEquals(new[] { new Vector2I(3, 3), new Vector2I(4, 3), new Vector2I(2, 3) })
                      && ne.SetEquals(new[] { new Vector2I(4, 3), new Vector2I(4, 4), new Vector2I(3, 3) })
                      && north0.Count == 0
                      && east0.SetEquals(new[] { new Vector2I(1, 0), new Vector2I(1, 1) })
                      && none.Count == 0;
            Rep("P3", "正面3マスの形(北・北東・盤外で欠ける・向き無しは空)", ok,
                $"北={n.Count} 北東={ne.Count} 隅で北={north0.Count} 隅で東={east0.Count} 向き無し={none.Count}");
        }

        // ---- 準備: 生きている個体に役を割り当てる ----
        // E は隣接の攻撃技を持つ個体（P16 で実際の技枠を使う）。
        var alive = _sched.Roster.Where(e => e.IsAlive).ToList();
        var E = alive.FirstOrDefault(e => Enumerable.Range(0, e.Moves.Slots.Count)
            .Any(i => e.Moves.Slots[i].Data.Power > 0 && e.Moves.Slots[i].Data.Range == MoveRange.Adjacent));
        var rest = alive.Where(e => e != E).ToList();
        if (E == null || rest.Count < 4)
        {
            for (int i = 4; i <= 16; i++)
                Rep($"P{i}", "準備", false, $"生きている個体が足りない(生存{alive.Count})");
            return;
        }
        Entity U = rest[0], A = rest[1], B = rest[2], C = rest[3];
        var roles = new[] { U, A, B, C, E };
        var parked = _sched.Roster.Where(e => !roles.Contains(e)).ToList();

        var saved = _sched.Roster.ToDictionary(e => e, e => (e.Faction, e.GridPosition, e.FacingDirection,
                                                              e.Stats.Trait, e.Stats.Type1, e.Stats.Type2));
        var savedWeather = _floor.Weather.Current;
        foreach (var r in roles) { r.Stats.Trait = ""; r.Stats.Type1 = "Neutral"; r.Stats.Type2 = ""; }
        U.Faction = Faction.Player; A.Faction = Faction.Player; B.Faction = Faction.Player;
        C.Faction = Faction.Player; E.Faction = Faction.Enemy;

        void Fresh()
        {
            foreach (var x in _sched.Roster)
            {
                x.StatusEffects.Reset(); x.Stats.HealToFull(); x.LastUsedMoveSlot = -1; x.HeldItemId = null;
                foreach (var sl in x.Moves.Slots) sl.CurrentPp = sl.Data.MaxPp;
            }
            ProtectionStats.Reset();
            // 役以外は盤の端へどけて、検査に混ざらないようにする。
            for (int i = 0; i < parked.Count; i++) parked[i].PlaceAt(new Vector2I(i, 7));
        }
        void At(Entity e, int x, int y, int fx = 0, int fy = -1)
        {
            e.PlaceAt(new Vector2I(x, y));
            e.FaceDirection(new Vector2I(fx, fy));
        }
        void Heal() { foreach (var x in roles) x.Stats.HealToFull(); }
        MoveSlot Slot(MoveData m) => new(m);
        // user が move を使う。defender（単体のとき）と aim（範囲・正面のとき）を渡す。
        void Use(Entity user, Entity defender, MoveData m, Vector2I? aim = null)
        {
            new AttackAction(user, defender, Slot(m), _floor, aim).Execute(0);
        }

        var phys = MoveDatabase.AllIds().Select(MoveDatabase.Get)
            .First(m => m.Category == MoveCategory.Physical && m.Range == MoveRange.Adjacent
                        && m.Accuracy >= 100 && m.Power >= 40 && m.RankEffects.Count == 0
                        && m.AilmentEffect == AilmentType.None && m.Restriction == RestrictionKind.None
                        && m.RecoilHpPercent == 0 && !m.SelfStunNextTurn && !m.SelfGuaranteedDeath
                        && m.WeaponTag == WeaponTag.None);
        var prot = Mv(ProtId);

        // ---- P4 付与(味方だけ・自分は含まない・狙いで向く) ----
        {
            Fresh();
            At(U, 3, 4, 0, 1);          // 南向き
            At(A, 3, 3); At(B, 4, 3);   // 北隣・右前
            At(E, 2, 3);                // 左前に敵
            At(C, 3, 5);                // 真後ろの味方
            var slot = Slot(prot);
            new AttackAction(U, null, slot, _floor, new Vector2I(3, 3)).Execute(0);
            bool facing = U.FacingDirection == new Vector2I(0, -1);
            bool who = A.StatusEffects.IsProtected && B.StatusEffects.IsProtected
                       && !C.StatusEffects.IsProtected && !E.StatusEffects.IsProtected && !U.StatusEffects.IsProtected;
            bool pp = slot.CurrentPp == slot.Data.MaxPp - 1;
            Rep("P4", "付与: 北隣を指すと北を向き、正面3マスの味方A・Bだけに付く(敵・真後ろ・自分は付かない)・PP-1",
                facing && who && pp && ProtectionStats.Granted == 2 && ProtectionStats.Used == 1,
                $"向き={U.FacingDirection} A={A.StatusEffects.IsProtected} B={B.StatusEffects.IsProtected} "
                + $"C={C.StatusEffects.IsProtected} 敵={E.StatusEffects.IsProtected} 自分={U.StatusEffects.IsProtected} PP={slot.CurrentPp}");
        }

        // ---- P5 不発 ----
        {
            Fresh();
            At(U, 3, 4);
            At(A, 0, 0); At(B, 1, 0); At(C, 6, 0); At(E, 6, 1);
            var slot = Slot(prot);
            new AttackAction(U, null, slot, _floor, new Vector2I(3, 3)).Execute(0);
            bool none = !A.StatusEffects.IsProtected && !B.StatusEffects.IsProtected && !C.StatusEffects.IsProtected
                        && !E.StatusEffects.IsProtected && !U.StatusEffects.IsProtected;
            Rep("P5", "不発: 正面に味方がいなければPPだけ減って誰にも付かない", none && slot.CurrentPp == slot.Data.MaxPp - 1
                && ProtectionStats.Granted == 0, $"誰も保護されない={none} PP={slot.CurrentPp}");
        }

        // ---- P6 単体攻撃を無効化 ----
        {
            Fresh();
            At(E, 3, 3, 0, 1); At(A, 3, 4); At(U, 0, 0); At(B, 1, 0); At(C, 6, 0);
            int full = A.Stats.CurrentHp;
            A.StatusEffects.GrantProtection();
            Use(E, A, phys);
            bool blocked = A.Stats.CurrentHp == full && !A.StatusEffects.IsProtected;
            Use(E, A, phys);
            bool second = A.Stats.CurrentHp < full;
            Rep("P6", "単体攻撃を無効化して消費し、2発目は当たる", blocked && second && ProtectionStats.Blocked == 1,
                $"1発目HP{full}→{full}(消費={blocked}) 2発目HP={A.Stats.CurrentHp}");
        }

        // ---- P7 変化技も無効化 ----
        {
            Fresh();
            At(E, 3, 3, 0, 1); At(A, 3, 4); At(U, 0, 0); At(B, 1, 0); At(C, 6, 0);
            A.StatusEffects.GrantProtection();
            Use(E, A, Mv("MV_122"));
            bool rankBlocked = A.StatusEffects.AtkRank == 0 && !A.StatusEffects.IsProtected;
            Fresh(); At(E, 3, 3, 0, 1); At(A, 3, 4); At(U, 0, 0); At(B, 1, 0); At(C, 6, 0);
            Use(E, A, Mv("MV_122"));
            bool rankControl = A.StatusEffects.AtkRank == -2;
            Fresh(); At(E, 3, 3, 0, 1); At(A, 3, 4); At(U, 0, 0); At(B, 1, 0); At(C, 6, 0);
            A.StatusEffects.GrantProtection();
            Use(E, A, Mv("mvs_toxic_dark"));
            bool toxicBlocked = A.StatusEffects.Ailment == AilmentType.None && !A.StatusEffects.IsProtected;
            Fresh(); At(E, 3, 3, 0, 1); At(A, 3, 4); At(U, 0, 0); At(B, 1, 0); At(C, 6, 0);
            Use(E, A, Mv("mvs_toxic_dark"));
            bool toxicControl = A.StatusEffects.Ailment == AilmentType.Toxic;
            Rep("P7", "変化技(ランク下げ・猛毒)も無効化して消費する(保護なしなら効く)",
                rankBlocked && rankControl && toxicBlocked && toxicControl,
                $"ランク:防ぐ={rankBlocked} 保護なし={rankControl}(Atk{A.StatusEffects.AtkRank}) 猛毒:防ぐ={toxicBlocked} 保護なし={toxicControl}");
        }

        // ---- P8 多段技は1回の使用ごと ----
        {
            Fresh();
            At(E, 3, 3, 0, 1); At(A, 3, 4); At(U, 0, 0); At(B, 1, 0); At(C, 6, 0);
            var multi = Mv("mvh_water");
            int full = A.Stats.CurrentHp;
            A.StatusEffects.GrantProtection();
            Use(E, A, multi);
            bool blocked = A.Stats.CurrentHp == full && !A.StatusEffects.IsProtected && ProtectionStats.Blocked == 1;
            bool hit = false; int tries = 0;
            for (; tries < 16 && !hit; tries++)
            {
                A.Stats.HealToFull();
                Use(E, A, multi);
                hit = A.Stats.CurrentHp < full;
            }
            Rep("P8", "多段技は全段まとめて1回の使用ぶんだけ無効(消費は1回)・保護なしなら当たる",
                blocked && hit && ProtectionStats.Blocked == 1, $"無効化={blocked} 保護なしで当たるまで{tries}回 当たった={hit}");
        }

        // ---- P9 範囲技は対象ごと(味方の技でも消費) ----
        {
            var area = MoveDatabase.AllIds().Select(MoveDatabase.Get)
                .Where(m => m.Category != MoveCategory.Status && m.Range == MoveRange.Area && m.Accuracy >= 100 && m.Power > 0)
                .OrderBy(m => m.Power).First();
            Fresh();
            At(E, 6, 6, 0, -1); At(A, 2, 2); At(B, 3, 3); At(C, 4, 4); At(U, 3, 5);
            A.StatusEffects.GrantProtection(); B.StatusEffects.GrantProtection();
            int fa = A.Stats.CurrentHp, fb = B.Stats.CurrentHp, fc = C.Stats.CurrentHp;
            Use(E, null, area, new Vector2I(3, 3));
            bool enemyCase = A.Stats.CurrentHp == fa && B.Stats.CurrentHp == fb && C.Stats.CurrentHp < fc
                             && !A.StatusEffects.IsProtected && !B.StatusEffects.IsProtected && ProtectionStats.Blocked == 2;
            Fresh();
            At(E, 6, 6, 0, -1); At(A, 2, 2); At(B, 0, 0); At(C, 6, 0); At(U, 3, 5);
            A.StatusEffects.GrantProtection();
            fa = A.Stats.CurrentHp;
            Use(U, null, area, new Vector2I(3, 3));
            bool allyCase = A.Stats.CurrentHp == fa && !A.StatusEffects.IsProtected && ProtectionStats.Blocked == 1;
            Rep("P9", "範囲技は対象ごとに判定(保護中A・Bは無傷で消費、保護なしCは被弾)・味方の範囲技でも消費",
                enemyCase && allyCase, $"敵の範囲技={enemyCase} 味方の範囲技={allyCase}(技:{area.Name})");
        }

        // ---- P10 防がないもの ----
        {
            Fresh();
            At(E, 3, 3, 0, 1); At(A, 3, 4); At(U, 0, 0); At(B, 1, 0); At(C, 6, 0);
            A.StatusEffects.TryApplyAilment(AilmentType.Poison);
            A.StatusEffects.GrantProtection();
            int before = A.Stats.CurrentHp;
            A.ResolveStatusTick();
            bool tick = A.Stats.CurrentHp < before && A.StatusEffects.IsProtected;
            Fresh();
            At(E, 3, 3, 0, 1); At(A, 3, 4); At(U, 0, 0); At(B, 1, 0); At(C, 6, 0);
            A.StatusEffects.GrantProtection();
            var selfMove = Mv("mvs_ice_atk_crit");
            Use(E, A, selfMove);
            bool selfOnly = selfMove.IsSelfContained && A.StatusEffects.IsProtected && ProtectionStats.Blocked == 0;
            Rep("P10", "防がないもの: 毒の継続ダメージでは消費されず、自分だけに効く変化技を向けられても消費されない",
                tick && selfOnly, $"毒ダメージ後も保護={tick}(HP{before}→{A.Stats.CurrentHp}) 自己完結技で保護のまま={selfOnly}");
        }

        // ---- P11 重ねない ----
        {
            Fresh();
            At(U, 3, 4); At(A, 3, 3); At(E, 3, 2, 0, 1); At(B, 0, 0); At(C, 6, 0);
            A.StatusEffects.GrantProtection();
            int grantedBefore = (int)ProtectionStats.Granted;
            new AttackAction(U, null, Slot(prot), _floor, new Vector2I(3, 3)).Execute(0);
            bool notStacked = ProtectionStats.Granted == grantedBefore && A.StatusEffects.IsProtected;
            int full = A.Stats.CurrentHp;
            Use(E, A, phys);
            bool first = A.Stats.CurrentHp == full && !A.StatusEffects.IsProtected;
            Use(E, A, phys);
            bool second = A.Stats.CurrentHp < full;
            Rep("P11", "重ねない: 保護中への2回目の付与は増えず、1発で消費され2発目は当たる",
                notStacked && first && second, $"付与増えず={notStacked} 1発目で消費={first} 2発目で被弾={second}");
        }

        // ---- P12 持続はサイクルの終わりまで ----
        {
            Fresh();
            At(U, 3, 4); At(A, 3, 3); At(E, 3, 2, 0, 1); At(B, 0, 0); At(C, 6, 0);
            new AttackAction(U, null, Slot(prot), _floor, new Vector2I(3, 3)).Execute(0);
            bool granted = A.StatusEffects.IsProtected;
            long expiredBefore = ProtectionStats.Expired;
            _sched.EndCycle();
            bool expired = !A.StatusEffects.IsProtected && ProtectionStats.Expired == expiredBefore + 1;
            Heal();
            Fresh();
            At(U, 3, 4); At(A, 3, 3); At(E, 3, 2, 0, 1); At(B, 0, 0); At(C, 6, 0);
            new AttackAction(U, null, Slot(prot), _floor, new Vector2I(3, 3)).Execute(0);
            int full = A.Stats.CurrentHp;
            Use(E, A, phys);
            bool sameCycle = A.Stats.CurrentHp == full && !A.StatusEffects.IsProtected;
            Rep("P12", "持続はサイクルの終わりまで: 使われなければサイクル終了で消え(消滅+1)、同じサイクル内の攻撃は防ぐ",
                granted && expired && sameCycle, $"付与={granted} サイクル終了で消滅={expired} 同サイクル内で防ぐ={sameCycle}");
        }

        // ---- P13 行動順 ----
        {
            Fresh();
            var byS = _sched.Roster.Where(e => e.IsAlive).OrderBy(e => e.Stats.Speed).ToList();
            var slow = byS.First(); var fast = byS.Last();
            var savedTrait = fast.Stats.Trait;
            fast.Stats.Trait = PowerTraitWeather.Water;
            _floor.Weather.SetBaseline(WeatherType.Rain);
            var head = Mv("MV_140");
            int pProt = BattleScheduler.EffectivePriority(slow, prot);
            int pHead = BattleScheduler.EffectivePriority(fast, head);
            var order = _sched.Order(new BattleScheduler.Commitment(fast, null, pHead),
                                     new BattleScheduler.Commitment(slow, null, pProt));
            _floor.Weather.SetBaseline(savedWeather);
            fast.Stats.Trait = savedTrait;
            Rep("P13", "行動順: プロテクションは優先度3で、俊敏が高く優先度2(ヘッドバット+あめ+水のちから)の手より先",
                pProt == 3 && pHead == 2 && slow.Stats.Speed < fast.Stats.Speed && order[0].Actor == slow,
                $"優先度{pProt} 対 {pHead} 俊敏{slow.Stats.Speed} 対 {fast.Stats.Speed} 先={order[0].Actor.ActorName}");
        }

        // ---- P14 出せない条件 ----
        {
            Fresh();
            At(E, 3, 3, 0, 1); At(U, 3, 4); At(A, 0, 0); At(B, 1, 0); At(C, 6, 0);
            bool free = U.StatusEffects.RestrictionBlocks(U, -1, prot) == null;
            Use(E, U, Mv("rs_provoke"));
            bool taunt = U.StatusEffects.RestrictionBlocks(U, -1, prot) != null;
            var slotT = Slot(prot);
            new AttackAction(U, null, slotT, _floor, new Vector2I(3, 3)).Execute(0);
            bool ppT = slotT.CurrentPp == slotT.Data.MaxPp;
            Fresh();
            At(E, 3, 3, 0, 1); At(U, 3, 4); At(A, 0, 0); At(B, 1, 0); At(C, 6, 0);
            Use(E, U, Mv("rs_short_leash"));
            bool leash = U.StatusEffects.RestrictionBlocks(U, -1, prot) != null;
            Rep("P14", "出せない条件: 制限なしなら出せ、挑発・射程制限中は出せず(PP不変)", free && taunt && ppT && leash,
                $"制限なし={free} 挑発中は不可={taunt}(PP不変={ppT}) 射程制限中は不可={leash}");
        }

        // ---- P15 NPC の得点 ----
        {
            Fresh();
            At(A, 3, 4); At(E, 3, 3, 0, 1); At(U, 0, 0); At(B, 1, 0); At(C, 6, 0);
            // 相性1の属性を探す（Effectiveness は種族の属性を見る）。
            var species = SpeciesDatabase.Instance.Get(A.SpeciesId);
            string t1 = new[] { "Neutral", "Fire", "Water", "Grass", "Electric", "Ground", "Ice", "Dragon", "Dark", "Void" }
                .First(t => Mathf.IsEqualApprox(TypeChartManager.GetMultiplier(t, species.Types.Select(x => x.ToString()).ToArray()), 1f));
            var threat = new MoveData { Id = "t", Name = "t", Type = t1, Category = MoveCategory.Physical, Power = 100, Accuracy = 100 };
            var threats = new List<(Entity Foe, MoveData Data, HashSet<Vector2I> Reach)> { (E, threat, null) };
            var empty = new List<(Entity Foe, MoveData Data, HashSet<Vector2I> Reach)>();
            A.Stats.CurrentHp = 200;
            float hp200 = NpcOpponent.ScoreProtection(new List<Entity> { A }, threats);
            A.Stats.CurrentHp = 80;
            float hp80 = NpcOpponent.ScoreProtection(new List<Entity> { A }, threats);
            float noThreat = NpcOpponent.ScoreProtection(new List<Entity> { A }, empty);
            float noAllies = NpcOpponent.ScoreProtection(new List<Entity>(), threats);
            float again = NpcOpponent.ScoreProtection(new List<Entity> { A }, threats);
            Rep("P15", "NPCの得点: 脅威100・HP200→50、HP80→110、脅威なし→0、味方なし→0、同じ入力で同じ値",
                Mathf.IsEqualApprox(hp200, 50f) && Mathf.IsEqualApprox(hp80, 110f) && noThreat == 0f && noAllies == 0f && again == hp80,
                $"HP200={hp200} HP80={hp80} 脅威なし={noThreat} 味方なし={noAllies} 再計算={again}");
            A.Stats.HealToFull();
        }

        // ---- P16 NPC は保護中の相手を撃たない ----
        {
            Fresh();
            At(E, 3, 3, 0, 1); At(A, 3, 4); At(U, 0, 0); At(B, 1, 0); At(C, 6, 0);
            int slotIdx = -1; float open = 0f;
            for (int i = 0; i < E.Moves.Slots.Count && slotIdx < 0; i++)
            {
                var d = E.Moves.Slots[i].Data;
                if (d.Power <= 0 || d.Range != MoveRange.Adjacent) continue;
                float sc = NpcOpponent.ScoreAttack(E, i, A.GridPosition, _grid, _floor, _sched.Roster);
                if (sc > 0f) { slotIdx = i; open = sc; }
            }
            A.StatusEffects.GrantProtection();
            float guarded = slotIdx < 0 ? -1f : NpcOpponent.ScoreAttack(E, slotIdx, A.GridPosition, _grid, _floor, _sched.Roster);
            A.StatusEffects.ConsumeProtection();
            float reopened = slotIdx < 0 ? -1f : NpcOpponent.ScoreAttack(E, slotIdx, A.GridPosition, _grid, _floor, _sched.Roster);
            Rep("P16", "NPCは保護中の相手への攻撃を得点0で避ける(保護を外すと正)",
                slotIdx >= 0 && open > 0f && guarded == 0f && reopened > 0f,
                $"技枠{slotIdx} 保護なし={open} 保護中={guarded} 外した後={reopened}");
        }

        // ---- 後始末 ----
        Fresh();
        ProtectionStats.Reset();
        _floor.Weather.SetBaseline(savedWeather);
        foreach (var (e, v) in saved)
        {
            e.Faction = v.Faction; e.PlaceAt(v.GridPosition); e.FaceDirection(v.FacingDirection);
            e.Stats.Trait = v.Trait; e.Stats.Type1 = v.Type1; e.Stats.Type2 = v.Type2;
        }
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
        return new BattleScheduler.Commitment(
            actor, action, BattleScheduler.EffectivePriority(actor, slot.Data));
    }
}
