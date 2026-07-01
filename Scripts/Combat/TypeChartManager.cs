using Godot;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MysteryDungeon.Combat;

internal class TypeChartJson
{
    [JsonPropertyName("types")] public List<string> Types { get; set; }
    [JsonPropertyName("matrix")] public List<List<float>> Matrix { get; set; }
}

// JSON-driven attack-type -> defender-type damage multiplier lookup.
// Load() is called once at startup (TestScene._Ready()); GetMultiplier()
// is a pure data lookup. Only a small illustrative set of types is
// populated in Data/type_chart.json for now - adding the remaining
// types later is a JSON-only change, no code changes needed.
public static class TypeChartManager
{
    private static readonly Dictionary<string, int> _typeIndex = new();
    private static List<List<float>> _matrix = new();
    private static bool _loaded;

    public static void Load(string resPath = "res://Data/type_chart.json")
    {
        _typeIndex.Clear();
        _matrix = new List<List<float>>();
        _loaded = false;

        if (!FileAccess.FileExists(resPath))
        {
            GD.PushWarning($"TypeChartManager: {resPath} not found, all multipliers default to 1.0.");
            return;
        }

        using var file = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
        string text = file.GetAsText();

        TypeChartJson data;
        try
        {
            data = JsonSerializer.Deserialize<TypeChartJson>(text);
        }
        catch (JsonException e)
        {
            GD.PushError($"TypeChartManager: failed to parse {resPath}: {e.Message}");
            return;
        }

        if (data?.Types == null || data.Matrix == null) return;

        for (int i = 0; i < data.Types.Count; i++)
            _typeIndex[data.Types[i]] = i;
        _matrix = data.Matrix;
        _loaded = true;

        GD.Print($"[TypeChart] Loaded {data.Types.Count} types from {resPath}.");
    }

    // Combined multiplier across a defender's types (empty/null/unknown
    // entries are treated as neutral 1.0x, so single-typed defenders -
    // just pass "" for the second type - work without special-casing).
    public static float GetMultiplier(string attackType, params string[] defenderTypes)
    {
        float result = 1f;
        foreach (var defenderType in defenderTypes)
            result *= GetSingleMultiplier(attackType, defenderType);
        return result;
    }

    private static float GetSingleMultiplier(string attackType, string defenderType)
    {
        if (!_loaded || string.IsNullOrEmpty(defenderType)) return 1f;
        if (!_typeIndex.TryGetValue(attackType, out int atkIdx)) return 1f;
        if (!_typeIndex.TryGetValue(defenderType, out int defIdx)) return 1f;
        if (atkIdx >= _matrix.Count || defIdx >= _matrix[atkIdx].Count) return 1f;
        return _matrix[atkIdx][defIdx];
    }
}
