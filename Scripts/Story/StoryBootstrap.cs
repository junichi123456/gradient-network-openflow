using Godot;
using System.Collections.Generic;
using System.Linq;
using MysteryDungeon.Species;

namespace MysteryDungeon.Story;

// autoload（SpeciesDatabase の後）。Data/story/*.json をロードして検証し、参照切れを PushError する。
public partial class StoryBootstrap : Node
{
    public const string Dir = "res://Data/story/";

    public override void _Ready()
    {
        var errors = new List<string>();
        StoryDatabase.Instance = LoadFromRes(errors);
        foreach (var e in errors) GD.PushError($"[StoryDatabase] {e}");
        GD.Print(errors.Count == 0
            ? $"[StoryDatabase] Loaded: charts={StoryDatabase.Instance.Charts.Count}, events={StoryDatabase.Instance.Events.Count}."
            : $"[StoryDatabase] FAILED with {errors.Count} problem(s).");
    }

    public static StoryDatabase LoadFromRes(List<string> errors)
    {
        string Read(string rel)
        {
            if (!FileAccess.FileExists(Dir + rel)) return null;
            using var f = FileAccess.Open(Dir + rel, FileAccess.ModeFlags.Read);
            return f.GetAsText();
        }
        var files = DirAccess.GetFilesAt(Dir + "events").Where(f => f.EndsWith(".json")).ToList();
        var db = StoryDatabase.Load(Read, files, errors);
        var rules = new HashSet<string>(RuleIds());
        errors.AddRange(db.Validate(
            id => SpeciesDatabase.Instance?.Get(id) != null, id => rules.Contains(id),
            new EventInterpreter(db, new StoryState(), null).OpNames.ToList()));
        return db;
    }

    private static IEnumerable<string> RuleIds()
    {
        using var f = FileAccess.Open("res://Data/dungeons.json", FileAccess.ModeFlags.Read);
        using var doc = System.Text.Json.JsonDocument.Parse(f.GetAsText());
        foreach (var d in doc.RootElement.EnumerateArray()) yield return d.GetProperty("id").GetString();
    }
}
