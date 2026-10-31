namespace MysteryDungeon.Dungeon;

// Dungeon-level meta rules, distinct from DungeonRule (which only
// governs a single floor's BSP generation parameters and is loaded from
// Data/dungeons.json). FloorController.Initialize() takes one of these
// and consults it once _floorNumber reaches MaxFloors to decide how the
// run resolves (see DungeonEndType / GenerateFinalFloor).
public class DungeonConfig
{
    public int MaxFloors { get; set; } = 5;
    public DungeonEndType EndType { get; set; } = DungeonEndType.FreeDungeonBoss;

    // Room-contained Water/Lava pools (see DungeonGenerator's drunkard's-
    // walk blob generation) are a per-dungeon biome choice, not a
    // universal default - off unless a specific dungeon opts in.
    public bool GenerateWaterPools { get; set; } = false;
    public bool GenerateLavaPools { get; set; } = false;

    // ---- シナリオ（Data/story/dungeons.json 由来。無いときは従来どおり） ----
    // 物語側のダンジョンID。クリア時にハコニワへ結果を渡す。空 = 物語と無関係の自由探索。
    public string StoryDungeonId { get; set; } = "";
    // 最終階のボス。null のとき BossEntity は従来の固定ボス。
    public Entities.BossSpec Boss { get; set; }
    // 野生の出現種（pal_plan 由来）。空のとき従来の固定の敵（DummyNPC/FastNPC）。
    public System.Collections.Generic.List<string> WildPool { get; set; } = new();
    // 野生の基準レベル（階が進むと FloorController が上げる）。
    public int WildLevel { get; set; } = 5;
    // 最初に生成する階（ヘッドレス検証で最終階へ直行するため。既定 1）。
    public int StartFloor { get; set; } = 1;
}
