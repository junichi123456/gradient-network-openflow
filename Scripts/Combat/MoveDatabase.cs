using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MysteryDungeon.Combat;

internal class MoveJson
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; }
    [JsonPropertyName("power")] public int Power { get; set; }
    [JsonPropertyName("accuracy")] public int Accuracy { get; set; }
    [JsonPropertyName("max_pp")] public int MaxPp { get; set; }
    [JsonPropertyName("range")] public string Range { get; set; }

    // Phase 21: all optional - a missing key leaves these C# defaults in
    // place, so the existing 96 non-status moves.json entries need no
    // edits (only poison_fog gets the new keys).
    [JsonPropertyName("rank_effect_stat")] public string RankEffectStat { get; set; } = "None";
    [JsonPropertyName("rank_effect_delta")] public int RankEffectDelta { get; set; }
    [JsonPropertyName("rank_effect_target")] public string RankEffectTarget { get; set; } = "Self";
    [JsonPropertyName("ailment_effect")] public string AilmentEffect { get; set; } = "None";
    [JsonPropertyName("ailment_chance")] public int AilmentChance { get; set; } = 100;
    [JsonPropertyName("ailment_target")] public string AilmentTarget { get; set; } = "Enemy";
    [JsonPropertyName("is_contact")] public bool IsContact { get; set; }
    [JsonPropertyName("is_guaranteed_hit")] public bool IsGuaranteedHit { get; set; }

    // 300-move import additions (all optional; missing keys keep the C#
    // defaults, so the existing minimal entries need no new keys).
    [JsonPropertyName("crit_rank_bonus")] public int CritRankBonus { get; set; }
    [JsonPropertyName("rank_effect_chance")] public float RankEffectChance { get; set; } = 1.0f;
    [JsonPropertyName("recoil_hp_percent")] public int RecoilHpPercent { get; set; }
    [JsonPropertyName("self_stun_next_turn")] public bool SelfStunNextTurn { get; set; }

    // 400-move import additions (all optional; missing keys keep defaults).
    [JsonPropertyName("dragon_multiplier")] public float DragonMultiplier { get; set; } = 1.0f;
    [JsonPropertyName("drain_hp_percent")] public int DrainHpPercent { get; set; }
    [JsonPropertyName("self_guaranteed_death")] public bool SelfGuaranteedDeath { get; set; }
}

// JSON-driven move definitions. DungeonScene._Ready() calls Load()
// explicitly for clarity/logging, but Get() also lazy-loads on first
// use - Godot runs child nodes' _Ready() (e.g. Player learning its
// starting moves) before the scene root's _Ready(), so anything that
// only loaded there would still be empty when entities ask for moves.
public static class MoveDatabase
{
    private static readonly Dictionary<string, MoveData> _moves = new();
    private static bool _loaded;

    public static void Load(string resPath = "res://Data/moves.json")
    {
        _loaded = true;
        _moves.Clear();

        if (!FileAccess.FileExists(resPath))
        {
            GD.PushWarning($"MoveDatabase: {resPath} not found, no moves available.");
            return;
        }

        using var file = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
        string text = file.GetAsText();

        List<MoveJson> entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<MoveJson>>(text);
        }
        catch (JsonException e)
        {
            GD.PushError($"MoveDatabase: failed to parse {resPath}: {e.Message}");
            return;
        }

        if (entries == null) return;

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Id)) continue;

            _moves[entry.Id] = new MoveData
            {
                Id = entry.Id,
                Name = entry.Name,
                Type = entry.Type,
                Category = Enum.TryParse<MoveCategory>(entry.Category, out var category) ? category : MoveCategory.Physical,
                Power = entry.Power,
                Accuracy = entry.Accuracy,
                MaxPp = entry.MaxPp,
                Range = Enum.TryParse<MoveRange>(entry.Range, out var range) ? range : MoveRange.Adjacent,
                RankEffectStat = Enum.TryParse<RankStat>(entry.RankEffectStat, out var rankStat) ? rankStat : RankStat.None,
                RankEffectDelta = entry.RankEffectDelta,
                RankEffectTarget = Enum.TryParse<StatusTarget>(entry.RankEffectTarget, out var rankTarget) ? rankTarget : StatusTarget.Self,
                AilmentEffect = Enum.TryParse<AilmentType>(entry.AilmentEffect, out var ailment) ? ailment : AilmentType.None,
                AilmentChance = entry.AilmentChance,
                AilmentTarget = Enum.TryParse<StatusTarget>(entry.AilmentTarget, out var ailmentTarget) ? ailmentTarget : StatusTarget.Enemy,
                IsContact = entry.IsContact,
                IsGuaranteedHit = entry.IsGuaranteedHit,
                CritRankBonus = entry.CritRankBonus,
                RankEffectChance = entry.RankEffectChance,
                RecoilHpPercent = entry.RecoilHpPercent,
                SelfStunNextTurn = entry.SelfStunNextTurn,
                DragonMultiplier = entry.DragonMultiplier,
                DrainHpPercent = entry.DrainHpPercent,
                SelfGuaranteedDeath = entry.SelfGuaranteedDeath,
            };
        }

        GD.Print($"[MoveDatabase] Loaded {_moves.Count} moves from {resPath}.");
    }

    public static MoveData Get(string moveId)
    {
        if (!_loaded) Load();
        return moveId != null && _moves.TryGetValue(moveId, out var data) ? data : null;
    }
}
