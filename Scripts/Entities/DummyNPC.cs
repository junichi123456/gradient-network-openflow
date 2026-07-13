using Godot;

namespace MysteryDungeon.Entities;

// Speed 100 (normal). Wanders with a 20% idle chance until it spots the
// player (HostileEntity's shared AI), then chases via A*.
public partial class DummyNPC : HostileEntity
{
    protected override float WanderWaitChance => 0.2f;

    public override void _Ready()
    {
        ActorName = "DummyNPC";
        Speed = 100;
        DebugColor = Colors.SkyBlue;

        // Base 60/60/60 (Chikipi's floor tier) + SpriteKey 003 (@0.05
        // scale) + Type Neutral all come from species "DummyNPC"
        // (Data/species.json).
        SpeciesId = "DummyNPC";
        base._Ready(); // resolves species, creates Stats + Moves + the Sprite2D visual

        Stats.SpAttack = 6;
        Stats.SpDefense = 6;
        Stats.Level = 8;

        Moves.Learn("power_shot");
    }
}
