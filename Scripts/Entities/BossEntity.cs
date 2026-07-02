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

        // 5x HP / 1.5x Attack relative to a nominal 20 HP / 10 Attack
        // baseline (roughly DummyNPC's tier), per the Phase 8 spec.
        Stats.MaxHp = 100;
        Stats.CurrentHp = 100;
        Stats.Attack = 15;
        Stats.Defense = 12;
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
