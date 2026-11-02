using Godot;

namespace MysteryDungeon.Entities;

// FreeDungeonBoss / StoryBoss / StoryMultiEnemyBattle のボス1体。HostileEntity の
// 徘徊・追跡・攻撃をそのまま使い、種族・レベル・係数だけを BossSpec で差し替える。
// Spec が null のときは従来の固定ボス（種族124・Lv15）。FloorController が AddChild の前に Spec を入れる。
public partial class BossEntity : HostileEntity
{
    public BossSpec Spec { get; set; }

    public override void _Ready()
    {
        ActorName = "DungeonBoss";
        Speed = 100;
        DebugColor = Colors.Purple;

        if (Spec == null)
        {
            SpeciesId = "124";
            base._Ready();
            Stats.Level = 15;
            Moves.Learn("aqua_gun");
            Moves.Learn("mvn_059");
            Moves.Learn("shockwave");
            return;
        }

        SpeciesId = Spec.SpeciesId;
        base._Ready(); // 種族の能力値・見た目・属性を DB から取る
        ActorName = Species.SpeciesDatabase.Instance?.Get(SpeciesId)?.DisplayName ?? SpeciesId;

        if (Spec.HpMult != 1.0) Stats.BaseMaxHp = (int)System.Math.Round(Stats.BaseMaxHp * Spec.HpMult);
        if (Spec.AtkMult != 1.0)
        {
            Stats.BaseAtk = (int)System.Math.Round(Stats.BaseAtk * Spec.AtkMult);
            Stats.BaseSpAtk = (int)System.Math.Round(Stats.BaseSpAtk * Spec.AtkMult);
        }
        Stats.Level = Spec.Level;
        Stats.HealToFull();
        if (MoveKit.LearnFromLearnset(Moves, SpeciesId, Spec.Level) == 0) Moves.Learn("gap_neutral_s_35");
    }
}
