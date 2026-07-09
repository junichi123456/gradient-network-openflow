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
        base._Ready(); // creates Stats + Moves + the debug ColorRect visual

        // Speedy glass cannon: above-average Atk, the legal Def floor
        // (50, cf. Depresso-tier), low HP - its real threat is Speed 200
        // acting twice per player turn, so the stats stay frail.
        Stats.BaseMaxHp = 60;
        Stats.BaseAtk = 75;
        Stats.BaseDef = 50;
        Stats.SpAttack = 10;
        Stats.SpDefense = 4;
        Stats.Type1 = "Electric";
        Stats.Level = 12;

        Moves.Learn("spark");
    }
}
