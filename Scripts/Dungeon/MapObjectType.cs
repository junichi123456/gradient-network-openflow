namespace MysteryDungeon.Dungeon;

public enum MapObjectType
{
    None,
    Stairs,
    Item,
    Trap,

    // Placeholder for DungeonEndType.FreeDungeonNoBossFinalFloor (end
    // pattern 5): stepping onto this object clears the dungeon in place
    // of a boss fight. Not yet placed by any generator - see
    // FloorController.GenerateFinalFloor's FreeDungeonNoBossFinalFloor
    // branch.
    EscapePortal,
}
