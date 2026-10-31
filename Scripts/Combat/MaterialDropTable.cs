using Godot;
using System.Collections.Generic;
using MysteryDungeon.Dungeon;

namespace MysteryDungeon.Combat;

// Rolled once per Player-side kill (AttackAction/ThrowItemAction). On
// success, drops one random Material-type item on the ground via
// FloorController.DropItem - deliberately NOT added straight into
// MaterialInventory, so the player still has to walk over and pick it
// up themselves (Phase 11 spec: "プレイヤーが自ら拾う体験を担保").
public static class MaterialDropTable
{
    // Mutable (not const) so verification/tuning code can override it;
    // gameplay always uses the default 35%.
    public static float DropChance = 0.35f;

    public static void TryDrop(FloorController floorController, Vector2I pos, string defenderName)
    {
        if (floorController == null) return;
        if (GD.Randf() >= DropChance) return;

        var materialIds = new List<string>();
        foreach (var id in ItemDatabase.AllIds())
            if (ItemDatabase.Get(id)?.Type == ItemType.Material)
                materialIds.Add(id);

        if (materialIds.Count == 0) return;

        int idx = Mathf.Min((int)(GD.Randf() * materialIds.Count), materialIds.Count - 1);
        var materialId = materialIds[idx];
        var data = ItemDatabase.Get(materialId);

        GD.Print($"[Loot] {defenderName} dropped {data.Name}!");
        floorController.DropItem(pos, materialId);
    }
}
