using System.Collections.Generic;
using MysteryDungeon.Combat;

namespace MysteryDungeon.Species;

// Phase 20: the SPECIES-CONSTANT half of a pal, the single source of
// truth for everything that's identical across every individual of one
// species. Individual/progression values (Level, CurrentExp, CurrentHp,
// the IV-like Breakthrough* stats, IsDowned) deliberately live OUT of
// here - on the entity / PartyMemberRecord - so a member is fully
// reconstructable from "SpeciesId + individual progress" (Phase 19).
//
// Plain data class loaded from Data/species.json (see SpeciesDatabase),
// matching this project's existing JSON-DB convention (MoveDatabase /
// ItemDatabase / TypeChartManager) rather than .tres - that keeps the
// whole DB headless-verifiable, which every phase's verification step
// depends on.
public class SpeciesData
{
    public string SpeciesId { get; set; }
    public string DisplayName { get; set; }

    // Phase 17 HAD inputs. Species constants - the per-individual
    // Breakthrough* modifiers are added on top at the entity level.
    public int BaseHP { get; set; }
    public int BaseAtk { get; set; }
    public int BaseDef { get; set; }

    // Phase 18-A input: EXP awarded for defeating this species (before
    // victim-level scaling). Preserved exactly from the old inline value.
    public int BaseExpYield { get; set; } = 55;

    // Phase 15 visuals: 8-direction texture folder base name
    // (Assets/Sprites/{SpriteKey}/) and the render scale multiplier.
    public string SpriteKey { get; set; } = "";
    public float DefaultVisualScale { get; set; } = 1.0f;

    // 1-2 elements. Consumed by damage only as strings today (via
    // EntityStats.Type1/Type2 -> TypeChartManager); kept here as typed
    // Element so the DB is the one place a species' typing is declared.
    public List<Element> Types { get; set; } = new();

    // Definition-only this phase (no learning system consumes it yet -
    // that's Phase B). moveId is a forward-declared string; existence
    // against the Move DB is validated later, once that DB is wired.
    public List<LearnsetEntry> Learnset { get; set; } = new();

    // Definition-only this phase. null = this species does not evolve.
    // ToSpeciesId existence IS validated at startup (see SpeciesDatabase).
    public EvolutionData Evolution { get; set; }

    // trait_catalog_v2: Trait is single/required BY DESIGN (every species
    // ultimately has exactly one - a species-specific Unique trait, or a
    // Template fallback if it has none of its own), but stage 2's data
    // model lands ahead of stage 9's actual 287-species assignment, so
    // "" (unassigned) is a valid transitional value - SpeciesDatabase.
    // Validate() only checks a NON-EMPTY Trait resolves against
    // TraitDatabase, it does not yet require every species to have one.
    public string Trait { get; set; } = "";

    // Ecology is a separate, OPTIONAL slot (independent of Trait - a
    // species can hold both, or neither). null/"" = no ecology.
    public string Ecology { get; set; } = "";

    // 対戦の構築ルールが読む唯一のタグ:伝説。7種のみ true
    // （ベイントール/セイントール/グレイシャル/グレイシャドウ/
    // ネプティオス/グランジーラ/ジェッドラン）。この特性を持つパルは
    // 1つの構築に1体までしか入れられない（BattleTeam.Validate）。
    public bool IsLegendary { get; set; }
}

public class LearnsetEntry
{
    public int Level { get; set; }
    public string MoveId { get; set; }
}

public class EvolutionData
{
    public string ToSpeciesId { get; set; }
    public int Level { get; set; }
}
