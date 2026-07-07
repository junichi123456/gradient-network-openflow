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

        // TEMP: visual smoke test for the real chiqipi (Paldex #003)
        // sprite set (Assets/Sprites/003/) - source art is 960x800, so
        // VisualScale brings it down to roughly tile-sized. Remove/
        // reassign once a real species database picks its own sprite.
        SpriteId = "003";
        VisualScale = 0.05f;

        base._Ready(); // creates Stats + Moves + the Sprite2D visual

        Stats.MaxHp = 20;
        Stats.CurrentHp = 20;
        Stats.Attack = 8;
        Stats.Defense = 6;
        Stats.SpAttack = 6;
        Stats.SpDefense = 6;
        Stats.Type1 = "Neutral";
        Stats.Level = 8;

        Moves.Learn("power_shot");
    }
}
