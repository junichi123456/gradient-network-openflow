using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MysteryDungeon.Combat;

internal class RankEffectJson
{
    [JsonPropertyName("stat")] public string Stat { get; set; } = "None";
    [JsonPropertyName("delta")] public int Delta { get; set; }
    [JsonPropertyName("target")] public string Target { get; set; } = "Self";
    [JsonPropertyName("chance")] public float Chance { get; set; } = 1.0f;
}

internal class MoveJson
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; }
    [JsonPropertyName("power")] public int Power { get; set; }
    [JsonPropertyName("accuracy")] public int Accuracy { get; set; }
    [JsonPropertyName("max_pp")] public int MaxPp { get; set; }
    [JsonPropertyName("priority")] public int Priority { get; set; }
    [JsonPropertyName("range")] public string Range { get; set; }

    // Phase 21: all optional - a missing key leaves these C# defaults in
    // place, so the existing 96 non-status moves.json entries need no
    // edits (only poison_fog gets the new keys).
    [JsonPropertyName("rank_effect_stat")] public string RankEffectStat { get; set; } = "None";
    [JsonPropertyName("rank_effect_delta")] public int RankEffectDelta { get; set; }
    [JsonPropertyName("rank_effect_target")] public string RankEffectTarget { get; set; } = "Self";

    // Optional multi-rank form. Present only on moves that change more than
    // one rank; everything else keeps the single rank_effect_* fields.
    [JsonPropertyName("rank_effects")] public List<RankEffectJson> RankEffects { get; set; }
    [JsonPropertyName("ailment_effect")] public string AilmentEffect { get; set; } = "None";
    [JsonPropertyName("ailment_chance")] public int AilmentChance { get; set; } = 100;
    [JsonPropertyName("ailment_target")] public string AilmentTarget { get; set; } = "Enemy";
    [JsonPropertyName("is_contact")] public bool IsContact { get; set; }
    [JsonPropertyName("is_guaranteed_hit")] public bool IsGuaranteedHit { get; set; }
    [JsonPropertyName("guaranteed_crit")] public bool GuaranteedCrit { get; set; }

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

    // trait_catalog_v2 stage 2-b (optional; unset on every current move).
    [JsonPropertyName("weapon_tag")] public string WeaponTag { get; set; } = "None";

    // Trap-move kit (optional; only the 7 trap moves set these).
    [JsonPropertyName("field_effect")] public string FieldEffect { get; set; } = "None";
    [JsonPropertyName("field_placement")] public string FieldPlacement { get; set; } = "None";

    // Weather kit (optional; only the weather-setting moves set these).
    [JsonPropertyName("weather_effect")] public string WeatherEffect { get; set; } = "None";
    [JsonPropertyName("weather_turns")] public int WeatherTurns { get; set; }

    // Multi-hit kit (optional; only the multi-hit moves set these).
    [JsonPropertyName("multi_hit")] public string MultiHit { get; set; } = "None";
    [JsonPropertyName("multi_hit_count")] public int MultiHitCount { get; set; }
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

            var data = new MoveData
            {
                Id = entry.Id,
                Name = entry.Name,
                Type = entry.Type,
                Category = Enum.TryParse<MoveCategory>(entry.Category, out var category) ? category : MoveCategory.Physical,
                Power = entry.Power,
                Accuracy = entry.Accuracy,
                MaxPp = entry.MaxPp,
                Priority = entry.Priority,
                Range = Enum.TryParse<MoveRange>(entry.Range, out var range) ? range : MoveRange.Adjacent,
                RankEffectStat = Enum.TryParse<RankStat>(entry.RankEffectStat, out var rankStat) ? rankStat : RankStat.None,
                RankEffectDelta = entry.RankEffectDelta,
                RankEffectTarget = Enum.TryParse<StatusTarget>(entry.RankEffectTarget, out var rankTarget) ? rankTarget : StatusTarget.Self,
                AilmentEffect = Enum.TryParse<AilmentType>(entry.AilmentEffect, out var ailment) ? ailment : AilmentType.None,
                AilmentChance = entry.AilmentChance,
                AilmentTarget = Enum.TryParse<StatusTarget>(entry.AilmentTarget, out var ailmentTarget) ? ailmentTarget : StatusTarget.Enemy,
                IsContact = entry.IsContact,
                IsGuaranteedHit = entry.IsGuaranteedHit,
                GuaranteedCrit = entry.GuaranteedCrit,
                CritRankBonus = entry.CritRankBonus,
                RankEffectChance = entry.RankEffectChance,
                RecoilHpPercent = entry.RecoilHpPercent,
                SelfStunNextTurn = entry.SelfStunNextTurn,
                DragonMultiplier = entry.DragonMultiplier,
                DrainHpPercent = entry.DrainHpPercent,
                SelfGuaranteedDeath = entry.SelfGuaranteedDeath,
                WeaponTag = Enum.TryParse<WeaponTag>(entry.WeaponTag, out var weaponTag) ? weaponTag : WeaponTag.None,
                FieldEffect = Enum.TryParse<Dungeon.FieldType>(entry.FieldEffect, out var fieldEffect) ? fieldEffect : Dungeon.FieldType.None,
                FieldPlacement = Enum.TryParse<Dungeon.FieldPlacement>(entry.FieldPlacement, out var fieldPlacement) ? fieldPlacement : Dungeon.FieldPlacement.None,
                WeatherEffect = Enum.TryParse<Dungeon.WeatherType>(entry.WeatherEffect, out var weatherEffect) ? weatherEffect : Dungeon.WeatherType.None,
                WeatherTurns = entry.WeatherTurns,
                MultiHit = Enum.TryParse<MultiHitMode>(entry.MultiHit, out var multiHit) ? multiHit : MultiHitMode.None,
                MultiHitCount = entry.MultiHitCount,
            };

            // Normalise both authored shapes into one list. "rank_effects"
            // wins when present; otherwise the legacy single slot is wrapped
            // (and dropped entirely when it declares no real change, so
            // RankEffects is empty for the ~570 moves that change no rank).
            data.RankEffects = BuildRankEffects(entry, data);

            _moves[entry.Id] = data;
        }

        GD.Print($"[MoveDatabase] Loaded {_moves.Count} moves from {resPath}.");
    }

    // Every loaded move id. Used by tooling/verification that needs to sweep
    // the whole pool rather than look up one move.
    private static IReadOnlyList<RankEffect> BuildRankEffects(MoveJson entry, MoveData data)
    {
        if (entry.RankEffects is { Count: > 0 })
        {
            var list = new List<RankEffect>();
            foreach (var r in entry.RankEffects)
            {
                var effect = new RankEffect
                {
                    Stat = Enum.TryParse<RankStat>(r.Stat, out var stat) ? stat : RankStat.None,
                    Delta = r.Delta,
                    Target = Enum.TryParse<StatusTarget>(r.Target, out var target) ? target : StatusTarget.Self,
                    Chance = r.Chance,
                };
                if (effect.IsActive) list.Add(effect);
            }
            return list;
        }

        if (data.RankEffectStat == RankStat.None || data.RankEffectDelta == 0)
            return Array.Empty<RankEffect>();

        return new[]
        {
            new RankEffect
            {
                Stat = data.RankEffectStat,
                Delta = data.RankEffectDelta,
                Target = data.RankEffectTarget,
                Chance = data.RankEffectChance,
            },
        };
    }

    public static List<string> AllIds()
    {
        if (!_loaded) Load();
        return new List<string>(_moves.Keys);
    }

    public static MoveData Get(string moveId)
    {
        if (!_loaded) Load();
        return moveId != null && _moves.TryGetValue(moveId, out var data) ? data : null;
    }
}
