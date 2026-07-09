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

        base._Ready(); // creates Stats + Moves + the debug ColorRect visual

        // Upper-mid-tier species values (100-100-95; cf. the 60-60-60
        // floor and Shadowbeak's 140-140-135 ceiling) - the boss's real
        // edge comes from these plus FloorController's floor-based Level
        // bump on top of the Lv15 base below.
        Stats.BaseMaxHp = 100;
        Stats.BaseAtk = 100;
        Stats.BaseDef = 95;
        Stats.SpAttack = 15;
        Stats.SpDefense = 12;
        Stats.Type1 = "Dragon";
        Stats.Level = 15;

        // Fixed 3-move boss kit (FloorController still applies the same
        // floor-based level scaling as normal enemies on top of this).
        Moves.Learn("aqua_gun");
        Moves.Learn("wind_cutter");
        Moves.Learn("spark");
    }
}
