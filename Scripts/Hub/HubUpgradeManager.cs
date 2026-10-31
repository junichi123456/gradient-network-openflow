using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;
using MysteryDungeon.Dungeon;
using MysteryDungeon.Story;

namespace MysteryDungeon.Hub;

// Persistent meta-progression state: facility levels, the banked
// material pool spent on upgrades, the party roster (PartyManager - see
// its own doc comment for why it lives here and not on FloorController),
// and the player inventory snapshot / pending dungeon selection used to
// hand data across the Dungeon<->Hub scene boundary. Registered as a
// project.godot autoload, so it survives GetTree().ChangeSceneToFile()
// (autoloads live outside the scene tree that gets swapped) - see
// FloorController.HandleDungeonCleared for the dungeon-side handoff and
// DungeonScene._Ready / DungeonSelectUI for the hub-side round trip.
public partial class HubUpgradeManager : Node
{
    [Signal] public delegate void FacilityUpgradedEventHandler(string facilityId, int newLevel);

    public static HubUpgradeManager Instance { get; private set; }

    public PartyManager PartyManager { get; } = new();

    // 物語の進行状態（セーブ対象）。
    public StoryState Story { get; private set; } = new();
    // HubScene がセーブ読込 / 新規開始を一度だけ行うためのフラグ（シーン再入場で上書きしない）。
    public bool StoryInitialized { get; set; }

    // Set by DungeonSelectUI right before ChangeSceneToFile; read (and
    // consumed - see DungeonScene._Ready) by the next dungeon scene. Null
    // means "no Hub selection was made" - DungeonScene falls back to its
    // own [Export] defaults, so it stays launchable standalone.
    public DungeonConfig PendingDungeonConfig { get; set; }
    public string PendingDungeonId { get; set; }
    // 物語のダンジョンから戻った直後の結果。HubScene が StoryFlow に渡して消す（セーブ対象外）。
    public DungeonResult PendingResult { get; set; }

    // シナリオのアイテム付与。素材は素材バンクへ、それ以外は次回の持ち物へ。
    public void GrantItem(string itemId, int n)
    {
        var data = ItemDatabase.Get(itemId);
        if (data == null) { GD.PushWarning($"[Hub] give_item: 不明なアイテム '{itemId}'"); return; }
        if (data.Type == ItemType.Material) _materialBank[itemId] = _materialBank.GetValueOrDefault(itemId) + n;
        else _inventorySnapshot.Add((itemId, n));
    }

    // Tiny hardcoded cost table for now (facilityId -> (materialId,
    // cost per level, linear)). Data/facilities.json is a natural
    // follow-up once there are more than a couple of facilities.
    private static readonly Dictionary<string, (string MaterialId, int CostPerLevel)> UpgradeCosts = new()
    {
        ["pal_workbench"] = ("wood", 5),
        ["pal_bed"] = ("pal_fluids", 3),
    };

    private readonly Dictionary<string, int> _facilityLevels = new();
    private readonly Dictionary<string, int> _materialBank = new();
    private readonly List<(string ItemId, int Quantity)> _inventorySnapshot = new();

    public IReadOnlyDictionary<string, int> FacilityLevels => _facilityLevels;
    public IReadOnlyDictionary<string, int> MaterialBank => _materialBank;

    public override void _Ready() => Instance = this;

    public int GetFacilityLevel(string facilityId) => _facilityLevels.GetValueOrDefault(facilityId, 1);

    // Sweeps every slot out of the player's Material Inventory into the
    // persistent bank (materials never stay in the carried inventory
    // between runs - only their banked totals matter for upgrades).
    public void DepositMaterials(MaterialInventory materialInventory)
    {
        foreach (var (itemId, quantity) in materialInventory.DrainAll())
        {
            _materialBank[itemId] = _materialBank.GetValueOrDefault(itemId) + quantity;
            var data = ItemDatabase.Get(itemId);
            GD.Print($"[Hub] Deposited {quantity}x {data?.Name ?? itemId} (bank total: {_materialBank[itemId]}).");
        }
    }

    public bool UpgradeFacility(string facilityId)
    {
        if (!UpgradeCosts.TryGetValue(facilityId, out var cost))
        {
            GD.PushWarning($"HubUpgradeManager: unknown facility '{facilityId}'.");
            return false;
        }

        int currentLevel = GetFacilityLevel(facilityId);
        int required = cost.CostPerLevel * currentLevel;
        int have = _materialBank.GetValueOrDefault(cost.MaterialId);

        if (have < required)
        {
            GD.Print($"[Hub] Not enough {cost.MaterialId} to upgrade {facilityId} ({have}/{required}).");
            return false;
        }

        _materialBank[cost.MaterialId] = have - required;
        int newLevel = currentLevel + 1;
        _facilityLevels[facilityId] = newLevel;

        GD.Print($"[Hub] {facilityId} upgraded to Lv.{newLevel}! (consumed {required}x {cost.MaterialId}, {_materialBank[cost.MaterialId]} remaining)");
        EmitSignal(SignalName.FacilityUpgraded, facilityId, newLevel);
        return true;
    }

    // --- Inventory carry-over across the Dungeon <-> Hub scene boundary ---
    // (Material items never appear here - they're drained separately by
    // DepositMaterials before this runs.)

    public void SaveInventory(InventoryManager inventory)
    {
        _inventorySnapshot.Clear();
        foreach (var slot in inventory.Slots)
            _inventorySnapshot.Add((slot.Data.Id, slot.Quantity));

        GD.Print($"[Hub] Saved {_inventorySnapshot.Count} inventory slot(s) for the trip back.");
    }

    public void RestoreInventory(InventoryManager inventory)
    {
        foreach (var (itemId, quantity) in _inventorySnapshot)
            for (int i = 0; i < quantity; i++)
                inventory.AddItem(itemId);
    }

    // --- ポイント・倉庫・売買（ハコニワの広場） ---

    public readonly record struct TradeResult(bool Ok, string Message);

    // 通貨「ポイント」。ダンジョンクリアと売却で増え、購入で減る。
    public int Points { get; private set; }
    public void AddPoints(int n) { if (n > 0) Points += n; }

    // 倉庫在庫（itemId → 数）。素材は預けない（MaterialBank の運用のまま）。
    private readonly Dictionary<string, int> _storage = new();
    public IReadOnlyDictionary<string, int> Storage => _storage;
    public int StoredCount(string id) => _storage.GetValueOrDefault(id);

    // 対戦広場から戻ったときの出現位置の合図（セーブしない）。空 = 通常の出現位置。
    public string ReturnSpawn { get; set; } = "";

    // 次のダンジョンへ持っていく持ち物（素材以外）。
    public Dictionary<string, int> Carried()
    {
        var d = new Dictionary<string, int>();
        foreach (var (id, n) in _inventorySnapshot) d[id] = d.GetValueOrDefault(id) + n;
        return d;
    }
    public int CarriedCount(string id) => Carried().GetValueOrDefault(id);

    // 持ち物の上限・スタック規則は InventoryManager に従う（実物を一時的に作って試す）。
    public bool CanCarry(string id, int n)
    {
        var inv = new InventoryManager();
        try
        {
            RestoreInventory(inv);
            for (int i = 0; i < n; i++) if (!inv.AddItem(id)) return false;
            return true;
        }
        finally { inv.Free(); }
    }

    private void SnapshotAdd(string id, int n) => _inventorySnapshot.Add((id, n));

    private bool SnapshotRemove(string id, int n)
    {
        if (CarriedCount(id) < n) return false;
        var merged = Carried(); merged[id] -= n;
        _inventorySnapshot.Clear();
        foreach (var (k, v) in merged) if (v > 0) _inventorySnapshot.Add((k, v));
        return true;
    }

    // 店の品揃え: food = 食品、nonfood = 食品以外の取引可、medicine = 薬、"" = 取引可すべて。素材（Price 0）はどれにも入らない。
    public static bool MatchesFilter(string filter, ItemData d)
        => d != null && d.Tradable && filter switch
        {
            "food" => d.IsFood,
            "nonfood" => !d.IsFood,
            "medicine" => d.IsMedicine,
            _ => true,
        };

    public TradeResult Deposit(string id, int n)
    {
        var d = ItemDatabase.Get(id);
        if (d == null || n <= 0) return new(false, "預けられません。");
        if (d.Type == ItemType.Material) return new(false, "素材は預けられません。");
        if (!SnapshotRemove(id, n)) return new(false, "持ち物が足りません。");
        _storage[id] = _storage.GetValueOrDefault(id) + n;
        return new(true, $"{d.Name}を{n}個預けた。");
    }

    public TradeResult Withdraw(string id, int n)
    {
        var d = ItemDatabase.Get(id);
        if (d == null || n <= 0) return new(false, "引き出せません。");
        if (StoredCount(id) < n) return new(false, "倉庫に足りません。");
        if (!CanCarry(id, n)) return new(false, "持ち物がいっぱいで、持てません。");
        SnapshotAdd(id, n);
        if ((_storage[id] -= n) <= 0) _storage.Remove(id);
        return new(true, $"{d.Name}を{n}個引き出した。");
    }

    public TradeResult Buy(string filter, string id, int n)
    {
        var d = ItemDatabase.Get(id);
        if (!MatchesFilter(filter, d) || n <= 0) return new(false, "ここでは買えません。");
        int cost = d.Price * n;
        if (Points < cost) return new(false, $"ポイントが足りません。({cost}必要)");
        if (!CanCarry(id, n)) return new(false, "持ち物がいっぱいで、持てません。");
        Points -= cost;
        SnapshotAdd(id, n);
        return new(true, $"{d.Name}を{n}個買った。(-{cost})");
    }

    public TradeResult Sell(string filter, string id, int n, RewardsDef rewards)
    {
        var d = ItemDatabase.Get(id);
        if (!MatchesFilter(filter, d) || n <= 0) return new(false, "ここでは売れません。");
        if (!SnapshotRemove(id, n)) return new(false, "持ち物が足りません。");
        int gain = rewards.SellPrice(d) * n;
        Points += gain;
        return new(true, $"{d.Name}を{n}個売った。(+{gain})");
    }

    // 研究所の分析に要る「不思議なかけら」を持っているか（渡しても消費しない）。
    public const string ShardId = "mysterious_shard";
    public bool HasShard => _materialBank.GetValueOrDefault(ShardId) >= 1;
    public void AddMaterial(string id, int n) => _materialBank[id] = _materialBank.GetValueOrDefault(id) + n;

    // --- セーブ用スナップショット（Pending* は保存しない） ---

    public SaveData ExportState()
    {
        var data = new SaveData { Story = Story };
        foreach (var (k, v) in _facilityLevels) data.Hub.FacilityLevels[k] = v;
        foreach (var (k, v) in _materialBank) data.Hub.MaterialBank[k] = v;
        data.Hub.Points = Points;
        foreach (var (k, v) in _storage) data.Hub.Storage[k] = v;
        data.Party.Protagonist = PartyManager.Protagonist;
        data.Party.Partner = PartyManager.Partner;
        data.Party.Roster.AddRange(PartyManager.RecruitedRoster);
        data.Party.Active.AddRange(PartyManager.ActiveParty);
        foreach (var (id, n) in _inventorySnapshot) data.Inventory.Add(new InventoryEntry { Id = id, N = n });
        return data;
    }

    public void ImportState(SaveData data)
    {
        Story = data.Story ?? new StoryState();
        _facilityLevels.Clear(); foreach (var (k, v) in data.Hub.FacilityLevels) _facilityLevels[k] = v;
        _materialBank.Clear(); foreach (var (k, v) in data.Hub.MaterialBank) _materialBank[k] = v;
        Points = Math.Max(0, data.Hub.Points);
        _storage.Clear(); foreach (var (k, v) in data.Hub.Storage) if (v > 0) _storage[k] = v;
        PartyManager.Restore(data.Party.Protagonist, data.Party.Partner, data.Party.Roster, data.Party.Active);
        _inventorySnapshot.Clear();
        foreach (var e in data.Inventory) _inventorySnapshot.Add((e.Id, e.N));
    }

    public void Save() => SaveService.Save(ExportState());

    // 新しいゲーム用に物語状態を差し替える。
    public void ResetStory() => Story = new StoryState();
}
