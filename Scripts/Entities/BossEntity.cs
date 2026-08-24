using Godot;

namespace MysteryDungeon.Entities;

// FreeDungeonBoss (end pattern 4): a single, heavily-buffed enemy that
// owns the entire final floor. Inherits HostileEntity's existing
// wander/chase/attack state machine as-is - only stats and moveset are
// boss-specific, so no new AI is needed.
public partial class BossEntity : HostileEntity
{
    public override void _Ready()
    {
        ActorName = "DungeonBoss";
        Speed = 100;
        DebugColor = Colors.Purple;

        // Base 105/100/100 (フェスキー) + Type Dragon + SpriteKey 124 come
        // from species "124" (Data/species.json). The boss's real edge
        // comes from these plus FloorController's floor-based Level bump
        // on top of the Lv15 base below.
        SpeciesId = "124";
        base._Ready(); // resolves species, creates Stats + Moves + the Sprite2D visual

        Stats.SpAttack = 15;
        Stats.SpDefense = 12;
        Stats.Level = 15;

        // Fixed 3-move boss kit (FloorController still applies the same
        // floor-based level scaling as normal enemies on top of this).
        Moves.Learn("aqua_gun");
        Moves.Learn("mvn_059");
        Moves.Learn("shockwave");
    }
}
