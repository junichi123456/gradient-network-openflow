using MysteryDungeon.Combat;

namespace MysteryDungeon.Entities;

// Runtime instance of a learned move: the immutable MoveData plus this
// particular entity's current PP for it.
public class MoveSlot
{
    public MoveData Data { get; }
    public int CurrentPp { get; set; }

    public MoveSlot(MoveData data)
    {
        Data = data;
        CurrentPp = data.MaxPp;
    }
}
