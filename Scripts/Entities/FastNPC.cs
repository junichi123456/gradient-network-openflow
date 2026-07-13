using Godot;

namespace MysteryDungeon.Entities;

// Speed 200 (double speed): the TurnScheduler calls DecideAction() twice
// per player turn. Always tries to act (no idle chance) - chases via
// HostileEntity's shared AI/A* logic once it spots the player.
public partial class FastNPC : HostileEntity
{
    public override void _Ready()
    {
        ActorName = "FastNPC";
        Speed = 200;
        DebugColor = Colors.OrangeRed;

        // Base 60/75/50 (speedy glass cannon) + Type Electric come from
        // species "FastNPC" (Data/species.json). No SpriteKey yet
        // (placeholder). Its real threat is Speed 200 acting twice per
        // player turn.
        SpeciesId = "FastNPC";
        base._Ready(); // resolves species, creates Stats + Moves + the Sprite2D visual

        Stats.SpAttack = 10;
        Stats.SpDefense = 4;
        Stats.Level = 12;

        Moves.Learn("spark");
    }
}
