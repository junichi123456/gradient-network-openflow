using Godot;
using System.Collections.Generic;
using MysteryDungeon.Combat;
using MysteryDungeon.Entities;

namespace MysteryDungeon.Hub;

// Persistent meta-progression state: facility levels, the banked
// material pool spent on upgrades, and the player inventory snapshot
// used to hand data across the Dungeon<->Hub scene boundary. Registered
// as a project.godot autoload, so it survives GetTree().
// ChangeSceneToFile() (autoloads live outside the scene tree that gets
// swapped) - see FloorController.HandleDungeonCleared for the
// dungeon-side handoff and TestScene._Ready for the hub-side restore.
public partial class HubUpgradeManager : Node
{
    [Signal] public delegate void FacilityUpgradedEventHandler(string facilityId, int newLevel);

    public static HubUpgradeManager Instance { get; private set; }

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
}
