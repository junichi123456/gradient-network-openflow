namespace MysteryDungeon.Combat;

public enum MoveCategory
{
    Physical,
    Special,
    Status,
}

// Not yet consumed by AttackAction's simplified damage formula - reserved
// for the real targeting/AoE logic a future phase will add.
public enum MoveRange
{
    Adjacent,
    Line,
    Room,
}

// Immutable move definition, loaded once by MoveDatabase from
// Data/moves.json. Runtime state (current PP) lives in MoveSlot instead.
public class MoveData
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }
    public MoveCategory Category { get; set; }
    public int Power { get; set; }
    public int Accuracy { get; set; }
    public int MaxPp { get; set; }
    public MoveRange Range { get; set; }
}
