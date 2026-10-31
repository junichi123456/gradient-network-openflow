using Godot;
using System.Collections.Generic;
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

    // --- セーブ用スナップショット（Pending* は保存しない） ---

    public SaveData ExportState()
    {
        var data = new SaveData { Story = Story };
        foreach (var (k, v) in _facilityLevels) data.Hub.FacilityLevels[k] = v;
        foreach (var (k, v) in _materialBank) data.Hub.MaterialBank[k] = v;
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
        PartyManager.Restore(data.Party.Protagonist, data.Party.Partner, data.Party.Roster, data.Party.Active);
        _inventorySnapshot.Clear();
        foreach (var e in data.Inventory) _inventorySnapshot.Add((e.Id, e.N));
    }

    public void Save() => SaveService.Save(ExportState());

    // 新しいゲーム用に物語状態を差し替える。
    public void ResetStory() => Story = new StoryState();
}
