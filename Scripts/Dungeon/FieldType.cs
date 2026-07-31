namespace MysteryDungeon.Dungeon;

// Persistent tile overlays placed by the trap-move kit. A separate layer
// from both GridManager (terrain/walkability) and DungeonObjectManager
// (one discrete pickup/stairs per tile), because a field is neither: it
// is walkable, it is not consumed on contact, several actors can stand on
// the same one, and its effect lasts for as long as they remain.
//
// Lifetime: permanent until げきりゅう clears it or the floor is left
// (FloorController.CleanupCurrentFloor). Nothing expires on a timer.
public enum FieldType
{
    None,
    Puddle,     // みずたまり  - on it: Electric taken x1.25, own Fire moves x0.75 power
    FlowerBed,  // はなばたけ  - on it: heal 9% MaxHp per action (ecology 飛行 excluded)
    ToxicMist,  // もうどくのきり - on it: Toxic while present, cleared on leaving
    ThinIce,    // うすらひ    - crossing it slides the mover onward
    Crevasse,   // クレバス    - stopping on it costs half CURRENT HP; invisible
    Fissure,    // じわれ      - identical to Crevasse, Ground-flavoured; invisible
}

// How a field-placing move chooses its tiles.
public enum FieldPlacement
{
    None,

    // 半分(端数切り捨て): of the 5x5 centred on the user, floor(25/2)=12
    // tiles, drawn at random from those that are Pal-free.
    HalfArea,

    // Exactly 4 Pal-free tiles of that same 5x5 (クレバス/じわれ).
    FourEmptyTiles,

    // げきりゅう: removes every field within radius 4 (9x9) instead of
    // placing one.
    ClearRadiusFour,
}
