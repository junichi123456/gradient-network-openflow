using MysteryDungeon.Combat;

namespace MysteryDungeon.Entities;

// Runtime instance of a learned move: the immutable MoveData plus this
// particular entity's current PP for it.
public class MoveSlot
{
    public MoveData Data { get; }
    public int CurrentPp { get; set; }

    // AI-only permission flag (see MoveManager.GetFirstAutoUsableMove).
    // Manual selection (Player bump-attack / MenuUI) ignores this - it
    // only lets a move be "reserved" out of AllyEntity's/HostileEntity's
    // automatic pick.
    public bool IsAutoUseAllowed { get; set; } = true;

    public MoveSlot(MoveData data)
    {
        Data = data;
        CurrentPp = data.MaxPp;
    }
}
