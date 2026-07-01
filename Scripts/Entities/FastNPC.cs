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
        base._Ready(); // creates Stats + the debug ColorRect visual

        Stats.MaxHp = 14;
        Stats.CurrentHp = 14;
        Stats.Attack = 6;
        Stats.Defense = 4;
        Stats.SpAttack = 10;
        Stats.SpDefense = 4;
        Stats.Type1 = "Electric";
    }
}
