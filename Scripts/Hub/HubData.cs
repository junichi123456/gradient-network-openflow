using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Story;

namespace MysteryDungeon.Hub;

// ハコニワの地図・施設・報酬・研究所メモのデータ（Data/story/hub_map.json / rewards.json / lab_notes.json）。
// StoryDatabase とは独立に読む（無くてもストーリー側の検証は壊れない）。

public class HubAreaDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    // [x, y, w, h]（世界座標、左上原点・Y下向き）。
    [JsonPropertyName("rect")] public float[] Rect { get; set; } = new float[4];
    public Rect2 Bounds => new(Rect[0], Rect[1], Rect[2], Rect[3]);
}

public class HubPassageDef
{
    [JsonPropertyName("a")] public string A { get; set; } = "";
    [JsonPropertyName("b")] public string B { get; set; } = "";
    [JsonPropertyName("width")] public float Width { get; set; } = 200;
}

public class HubFixtureDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    // upgrade（寝床・作業台）/ pal_box / resident（住民NPC）/ battle（西の入口）/ gate（ダンジョンゲート）
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("facility_id")] public string FacilityId { get; set; } = "";
    [JsonPropertyName("area")] public string Area { get; set; } = "";
    [JsonPropertyName("position")] public float[] Position { get; set; } = new float[2];
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("color")] public string Color { get; set; } = "#888888";
    [JsonPropertyName("save")] public bool Save { get; set; }
    [JsonPropertyName("scene")] public string Scene { get; set; } = "";
    [JsonPropertyName("return_position")] public float[] ReturnPosition { get; set; }
    [JsonPropertyName("required_facility")] public string RequiredFacility { get; set; } = "";
    [JsonPropertyName("required_level")] public int RequiredLevel { get; set; } = 1;
    public Vector2 Pos => new(Position[0], Position[1]);
}

public class HubNpcDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("offset")] public float[] Offset { get; set; } = new float[2];
}

public class HubGreetingLine
{
    [JsonPropertyName("who")] public string Who { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

public class HubFacilityDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("area")] public string Area { get; set; } = "center";
    [JsonPropertyName("position")] public float[] Position { get; set; } = new float[2];
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    // この施設が現れるチャート（完了後）。charts.json の hub_changes に同じ施設IDがある。
    [JsonPropertyName("appears_after")] public string AppearsAfter { get; set; } = "";
    [JsonPropertyName("replaces")] public string Replaces { get; set; } = "";
    [JsonPropertyName("npc")] public List<HubNpcDef> Npc { get; set; } = new();
    // storage / shop / clinic / lab
    [JsonPropertyName("ui")] public string Ui { get; set; } = "";
    // shop / clinic の品揃え: food / nonfood / medicine
    [JsonPropertyName("filter")] public string Filter { get; set; } = "";
    [JsonPropertyName("color")] public string Color { get; set; } = "#888888";
    [JsonPropertyName("greeting")] public List<HubGreetingLine> Greeting { get; set; } = new();
    public Vector2 Pos => new(Position[0], Position[1]);
}

public class HubMapDef
{
    [JsonPropertyName("currency")] public string Currency { get; set; } = "ポイント";
    [JsonPropertyName("wall_thickness")] public float WallThickness { get; set; } = 40;
    [JsonPropertyName("spawn")] public float[] Spawn { get; set; } = { 640, 360 };
    [JsonPropertyName("areas")] public List<HubAreaDef> Areas { get; set; } = new();
    [JsonPropertyName("passages")] public List<HubPassageDef> Passages { get; set; } = new();
    [JsonPropertyName("fixtures")] public List<HubFixtureDef> Fixtures { get; set; } = new();
    [JsonPropertyName("facilities")] public List<HubFacilityDef> Facilities { get; set; } = new();

    public HubAreaDef Area(string id) => Areas.FirstOrDefault(a => a.Id == id);
    public HubFacilityDef Facility(string id) => Facilities.FirstOrDefault(f => f.Id == id);

    // 完了済みチャートに応じて広場に立っている施設。置き換えられた施設（lab に対する clinic）は除く。
    public List<HubFacilityDef> VisibleFacilities(ICollection<string> completedCharts)
    {
        var shown = Facilities.Where(f => completedCharts.Contains(f.AppearsAfter)).ToList();
        var replaced = shown.Where(f => f.Replaces != "").Select(f => f.Replaces).ToHashSet();
        return shown.Where(f => !replaced.Contains(f.Id)).ToList();
    }
}

public class RewardsDef
{
    [JsonPropertyName("base")] public int Base { get; set; } = 30;
    [JsonPropertyName("per_chart")] public int PerChart { get; set; } = 10;
    // 追加枠（クリア後のダンジョン）に使うチャート番号。
    [JsonPropertyName("extra_chart")] public int ExtraChart { get; set; } = 20;
    [JsonPropertyName("sell_divisor")] public int SellDivisor { get; set; } = 2;

    // ダンジョンを勝ってクリアしたときのポイント。敗北は0。
    public int ClearReward(StoryDatabase db, DungeonDef d, bool defeated)
    {
        if (defeated || d == null) return 0;
        var c = db.Chart(d.Chart);
        int n = c == null ? 1 : c.Kind == "main" ? StoryDatabase.ChartNumber(c.Id) : c.LevelChart > 0 ? c.LevelChart : ExtraChart;
        return Base + PerChart * n;
    }

    public int SellPrice(ItemData item) => item == null || !item.Tradable ? 0 : item.Price / Math.Max(1, SellDivisor);
}

public class LabNoteDef
{
    [JsonPropertyName("after_chart")] public string AfterChart { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("lines")] public List<string> Lines { get; set; } = new();
}

public static class HubData
{
    public const string Dir = "res://Data/story/";
    private static readonly JsonSerializerOptions Opts = new();
    private static HubMapDef _map; private static RewardsDef _rewards; private static List<LabNoteDef> _notes;

    public static HubMapDef Map => _map ??= Read<HubMapDef>("hub_map.json") ?? new HubMapDef();
    public static RewardsDef Rewards => _rewards ??= Read<RewardsDef>("rewards.json") ?? new RewardsDef();
    public static List<LabNoteDef> LabNotes => _notes ??= Read<List<LabNoteDef>>("lab_notes.json") ?? new();

    public static T Read<T>(string file) where T : class
    {
        if (!Godot.FileAccess.FileExists(Dir + file)) { GD.PushError($"[HubData] {file} が無い"); return null; }
        using var f = Godot.FileAccess.Open(Dir + file, Godot.FileAccess.ModeFlags.Read);
        try { return JsonSerializer.Deserialize<T>(f.GetAsText(), Opts); }
        catch (JsonException e) { GD.PushError($"[HubData] {file}: {e.Message}"); return null; }
    }

    // 研究所で読めるメモ: 完了したチャートの段階まですべて（古い順）。
    public static List<LabNoteDef> NotesFor(ICollection<string> completedCharts)
        => LabNotes.Where(n => completedCharts.Contains(n.AfterChart)).ToList();
}
