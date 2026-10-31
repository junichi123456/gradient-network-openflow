using System;
using System.IO;
using System.Text.Json;

namespace MysteryDungeon.Story;

public enum LoadStatus { Ok, NoFile, Incompatible, Corrupt }

public readonly record struct LoadResult(LoadStatus Status, SaveData Data, string Message);

// 単一スロットのセーブ。純 C#（パスは呼び出し側が渡す）。
// version が違うセーブは読まず「互換性なし」を返す（マイグレーションは作らない）。
public static class SaveService
{
    public const string DefaultPath = "user://save_slot_1.json";
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static string Serialize(SaveData data) => JsonSerializer.Serialize(data, Opts);

    public static LoadResult Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.Number
                || v.GetInt32() != SaveData.CurrentVersion)
                return new LoadResult(LoadStatus.Incompatible, null, "セーブデータの互換性がありません");
            var data = JsonSerializer.Deserialize<SaveData>(json, Opts);
            return data == null
                ? new LoadResult(LoadStatus.Corrupt, null, "セーブデータが壊れています")
                : new LoadResult(LoadStatus.Ok, data, "");
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return new LoadResult(LoadStatus.Corrupt, null, "セーブデータが壊れています: " + e.Message);
        }
    }

    // "user://" は Godot のユーザーデータ領域へ解決する。それ以外はそのまま実パス。
    public static string ResolvePath(string path)
        => path.StartsWith("user://") ? Godot.ProjectSettings.GlobalizePath(path) : path;

    public static bool Exists(string path = DefaultPath) => File.Exists(ResolvePath(path));

    public static void Save(SaveData data, string path = DefaultPath)
    {
        string real = ResolvePath(path);
        string dir = Path.GetDirectoryName(real);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = real + ".tmp";
        File.WriteAllText(tmp, Serialize(data));
        File.Move(tmp, real, true);
    }

    public static LoadResult Load(string path = DefaultPath)
    {
        string real = ResolvePath(path);
        if (!File.Exists(real)) return new LoadResult(LoadStatus.NoFile, null, "");
        return Parse(File.ReadAllText(real));
    }
}
