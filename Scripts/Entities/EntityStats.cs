using Godot;
using MysteryDungeon.Grid;
using MysteryDungeon.UI;

namespace MysteryDungeon.Entities;

// Shared combat/survival stats component, attached as a child node of
// every Entity (see Entity._Ready()). Attack/Defense/SpAttack/SpDefense
// feed AttackAction's Palworld-style damage formula; SpAttack/SpDefense
// are kept as data for a possible future physical/special split but
// aren't consumed by that formula (Palworld itself doesn't split them).
public partial class EntityStats : Node
{
    [Export] public int MaxHp { get; set; } = 20;
    public int CurrentHp { get; set; }

    [Export] public int Level { get; set; } = 10;
    [Export] public int Exp { get; set; } = 0;
    [Export] public int ExpToNextLevel { get; set; } = 100;

    [Export] public int Attack { get; set; } = 10;
    [Export] public int Defense { get; set; } = 10;
    [Export] public int SpAttack { get; set; } = 10;
    [Export] public int SpDefense { get; set; } = 10;

    // Up to two types; Type2 empty means single-typed.
    [Export] public string Type1 { get; set; } = "Neutral";
    [Export] public string Type2 { get; set; } = "";

    // Terrain-traversal trait ("Pal skill"): Hover/Glide lets this
    // entity stand on any hazard tile, on top of whatever its Type1/
    // Type2 already grants (see GetMovementProfile/CanTraverse).
    [Export] public PartnerSkill PartnerSkill { get; set; } = PartnerSkill.None;

    // Hunger - meaningful for Player only. FloorController calls
    // TickBelly() once per turn on the player's Stats; NPCs simply never
    // have this called, so their Belly just sits unused at MaxBelly.
    [Export] public int MaxBelly { get; set; } = 100;
    public int Belly { get; set; }

    public bool IsAlive => CurrentHp > 0;

    // Which terrain hazards this entity can stand on. Hover/Glide (from
    // PartnerSkill) takes priority since it covers everything at once;
    // otherwise Type1/Type2 grants the matching single-hazard immunity.
    public MovementProfile GetMovementProfile()
    {
        if (PartnerSkill is PartnerSkill.Hover or PartnerSkill.Glide) return MovementProfile.Hover;
        if (Type1 == "Fire" || Type2 == "Fire") return MovementProfile.FireImmune;
        if (Type1 == "Water" || Type2 == "Water" || Type1 == "Ice" || Type2 == "Ice") return MovementProfile.WaterIceImmune;
        return MovementProfile.Normal;
    }

    public bool CanTraverse(TerrainType terrain) => TerrainTraversalRules.IsWalkable(terrain, GetMovementProfile());

    public override void _Ready()
    {
        CurrentHp = MaxHp;
        Belly = MaxBelly;
    }

    public void TakeDamage(int amount)
    {
        CurrentHp = Mathf.Max(0, CurrentHp - amount);
    }

    public void Heal(int amount)
    {
        CurrentHp = Mathf.Min(MaxHp, CurrentHp + amount);
    }

    // Decrements hunger; once it hits empty, deals 1 starvation damage
    // per turn instead of decrementing further.
    public void TickBelly()
    {
        if (Belly > 0)
        {
            Belly--;
            GD.Print($"[Belly] Belly: {Belly}/{MaxBelly}");
        }
        else
        {
            TakeDamage(1);
            GD.Print($"[Belly] Starving! Took 1 damage. HP: {CurrentHp}/{MaxHp}");
        }
    }

    // Only AttackAction's Player-kill branch calls this today, but it's
    // written generically (reads the owning Entity's name for the log)
    // so it isn't silently wrong if something else ever levels up too.
    public void AddExp(int amount)
    {
        if (amount <= 0) return;

        Exp += amount;
        while (Exp >= ExpToNextLevel)
        {
            Exp -= ExpToNextLevel;
            LevelUp();
        }
    }

    private void LevelUp()
    {
        Level++;
        MaxHp += 5;
        Attack += 2;
        Defense += 2;
        SpAttack += 2;
        SpDefense += 2;
        CurrentHp = MaxHp;
        ExpToNextLevel = Mathf.RoundToInt(ExpToNextLevel * 1.5f);

        string ownerName = (GetParent() as Entity)?.ActorName ?? Name;
        MessageLogger.Log($"{ownerName} leveled up to Lv {Level}! Max HP is now {MaxHp}.", MessageLogger.ProgressionColor);
    }
}
