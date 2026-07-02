namespace MysteryDungeon.Grid;

// Which non-Wall terrain hazards a mover can stand on - computed from
// an entity's PartnerSkill/Type via EntityStats.GetMovementProfile().
// Deliberately a single flat enum rather than a bitmask: a dual
// Fire+Water/Ice entity can't be expressed precisely today, but no such
// entity exists yet, so this is an accepted simplification (see Phase 9
// design notes) rather than an oversight.
public enum MovementProfile
{
    Normal,
    Hover,          // Water/Lava/Chasm all walkable (PartnerSkill.Hover or .Glide)
    FireImmune,     // Lava walkable (Fire-type)
    WaterIceImmune, // Water walkable (Water- or Ice-type)
}
