using Godot;

namespace MysteryDungeon.Entities;

// 野生の敵。種族とレベルは FloorController が AddChild の前に与える（pal_plan の出現表から選ぶ）。
public partial class WildEntity : HostileEntity
{
    public int WildLevel { get; set; } = 5;
    protected override float WanderWaitChance => 0.2f;

    public override void _Ready()
    {
        Speed = 100;
        DebugColor = Colors.Tomato;
        base._Ready();
        ActorName = Species.SpeciesDatabase.Instance?.Get(SpeciesId)?.DisplayName ?? SpeciesId;
        Stats.Level = WildLevel;
        Stats.HealToFull();
        if (MoveKit.LearnFromLearnset(Moves, SpeciesId, WildLevel) == 0) Moves.Learn("gap_neutral_s_35");
    }
}
