namespace MysteryDungeon.Dungeon;

// How a dungeon's final floor (DungeonConfig.MaxFloors) resolves. Only
// FreeDungeonBoss is actually implemented as of Phase 8 - the other four
// are wired into FloorController.GenerateFinalFloor()'s branch so the
// architecture is in place, but each just logs "not implemented yet"
// until its own phase.
public enum DungeonEndType
{
    StoryBoss,                   // (1) a scripted, uniquely-tuned story boss
    StoryMultiEnemyBattle,       // (2) a scripted multi-enemy story battle
    StoryNoBossFinalFloor,       // (3) no boss - cleared by a story/event flag completing
    FreeDungeonBoss,             // (4) a normal free-dungeon boss (implemented this phase)
    FreeDungeonNoBossFinalFloor, // (5) no boss - cleared by reaching an EscapePortal object
}
