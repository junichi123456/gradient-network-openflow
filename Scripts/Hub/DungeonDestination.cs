using System.Collections.Generic;
using MysteryDungeon.Dungeon;

namespace MysteryDungeon.Hub;

// A selectable entry in DungeonSelectUI: display name, which
// Data/dungeons.json generation rule to use, and the DungeonConfig
// (max floors / end pattern) that shapes the run. A tiny hardcoded list
// for now, mirroring HubUpgradeManager's UpgradeCosts table - a natural
// Data/*.json follow-up once there are more than a couple of dungeons.
public class DungeonDestination
{
    public string Id;
    public string DisplayName;
    public string DungeonRuleId;
    public DungeonConfig Config;
}

public static class DungeonDestinations
{
    public static readonly List<DungeonDestination> All = new()
    {
        new DungeonDestination
        {
            Id = "forest",
            DisplayName = "はじまりの森 (全3階層・脱出ポータル)",
            DungeonRuleId = "beach_cave",
            Config = new DungeonConfig { MaxFloors = 3, EndType = DungeonEndType.FreeDungeonNoBossFinalFloor },
        },
        new DungeonDestination
        {
            Id = "volcano",
            DisplayName = "灼熱の洞窟 (全5階層・ボス戦)",
            DungeonRuleId = "beach_cave",
            Config = new DungeonConfig { MaxFloors = 5, EndType = DungeonEndType.FreeDungeonBoss },
        },
    };
}
