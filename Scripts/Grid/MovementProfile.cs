namespace MysteryDungeon.Grid;

// Which non-Wall terrain hazards a mover can stand on - computed from an
// entity's PartnerSkill/Ecology/Type via EntityStats.GetMovementProfile().
//
// Deliberately a small flat enum rather than a bitmask: AStarPathfinder
// caches one AStarGrid2D per profile (see its _cache), so the value set
// doubles as that cache's key space and is worth keeping minimal. It is
// nonetheless CLOSED over everything the game can actually produce - the
// four hazard combinations reachable from a Type/PartnerSkill/Ecology
// mix are Floor-only, Lava, Water, Lava+Water, and all-three, and each
// has a value below.
//
// FireWaterImmune closes what used to be an accepted gap (the old note
// here read "a dual Fire+Water/Ice entity can't be expressed precisely
// today, but no such entity exists yet"). trait_catalog_v2 stage 4 made
// it reachable two more ways - ecology 放熱器官 on a Water/Ice species,
// or 潜航 on a Fire species - and stage 9 assigns ecology across all 287
// species, so leaving it unrepresentable would have silently dropped one
// of the two immunities at assignment time.
public enum MovementProfile
{
    Normal,
    Hover,           // Water/Lava/Chasm all walkable (PartnerSkill.Hover/.Glide, or ecology 飛行)
    FireImmune,      // Lava walkable (Fire-type, or ecology 放熱器官)
    WaterIceImmune,  // Water walkable (Water-/Ice-type, or ecology 潜航)
    FireWaterImmune, // Lava AND Water walkable - both of the above at once, but not Chasm
}
