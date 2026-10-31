using System;
using System.Collections.Generic;
using System.Linq;
using MysteryDungeon.Dungeon;
using MysteryDungeon.Entities;
using MysteryDungeon.Story;

namespace MysteryDungeon.Hub;

// ダンジョン選択に出る1項目: 表示名、Data/dungeons.json の生成ルールID、周回を形づくる DungeonConfig。
public class DungeonDestination
{
    public string Id;
    public string DisplayName;
    public string DungeonRuleId;
    public DungeonConfig Config;
    // 空でなければ、スキップした補完のチャートID。入る直前に StartChart する。
    public string SupplementChart = "";
}

// Data/story/dungeons.json から組み立てる（StoryState の解放状況で絞る）。
public static class DungeonDestinations
{
    // StoryDatabase が無い（単体起動・テスト）ときの代わり。
    private static List<DungeonDestination> Fallback() => new()
    {
        new DungeonDestination
        {
            Id = "free", DisplayName = "ビーチのどうくつ (練習・全3階層・脱出ポータル)", DungeonRuleId = "beach_cave",
            Config = new DungeonConfig { MaxFloors = 3, EndType = DungeonEndType.FreeDungeonNoBossFinalFloor },
        },
    };

    public static List<DungeonDestination> Current()
    {
        var db = StoryDatabase.Instance;
        var hub = HubUpgradeManager.Instance;
        if (db == null || hub == null || hub.Story.Protagonist == "") return Fallback();
        var flow = new StoryFlow(db, hub.Story, new EventInterpreter(db, hub.Story, null));
        return flow.AvailableDungeons().Select(d =>
        {
            var dest = From(db, d, hub.Story);
            if (flow.IsSkippedSupplementDungeon(d)) { dest.SupplementChart = d.Chart; dest.DisplayName = "[補完] " + dest.DisplayName; }
            return dest;
        }).ToList();
    }

    public static DungeonDestination From(StoryDatabase db, DungeonDef d, StoryState st = null)
    {
        var endType = Enum.Parse<DungeonEndType>(d.EndType);
        string tag = endType switch
        {
            DungeonEndType.FreeDungeonNoBossFinalFloor => "脱出ポータル",
            DungeonEndType.StoryMultiEnemyBattle => "群れ戦",
            _ => "ボス戦",
        };
        return new DungeonDestination
        {
            Id = d.Id,
            DisplayName = $"{d.Name} (全{d.Floors}階層・{tag})",
            DungeonRuleId = d.RuleId,
            Config = ConfigFor(db, d, st),
        };
    }

    public static DungeonConfig ConfigFor(StoryDatabase db, DungeonDef d, StoryState st = null)
    {
        int bossLv = db.BossLevelFor(d);
        return new DungeonConfig
        {
            MaxFloors = d.Floors,
            EndType = Enum.Parse<DungeonEndType>(d.EndType),
            StoryDungeonId = d.Id,
            Boss = d.Boss == null ? null : Spec(d.Boss, bossLv),
            WildPool = db.WildPool(d.Id),
            WildLevel = Math.Max(2, bossLv + db.LevelCurve.WildOffset),
            // 戦闘後共闘: ボスを倒した直後の次のダンジョンだけ、仲間が同行する。
            CoopGuests = st != null && st.CoopDungeon == d.Id && st.CoopSpecies != "" ? new List<string> { st.CoopSpecies } : new(),
        };
    }

    // レベルはデータに書かれた値を優先（0 = 曲線の値）。群れの追加メンバーも同様。
    private static BossSpec Spec(BossSpecDef b, int curveLevel) => new()
    {
        SpeciesId = b.SpeciesId,
        Level = b.Level > 0 ? b.Level : curveLevel,
        HpMult = b.HpMult,
        AtkMult = b.AtkMult,
        DefeatOk = b.DefeatOk,
        Group = b.Group.Select(g => Spec(g, curveLevel)).ToList(),
    };
}
