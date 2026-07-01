namespace MysteryDungeon.Turn;

// Anything the TurnScheduler can grant energy to and ask to act.
// The Player does not implement autonomous DecideAction() usage:
// its action is supplied externally via TurnManager.SubmitPlayerAction.
public interface ITurnActor
{
    string ActorName { get; }
    int Speed { get; }
    bool IsAlive { get; }

    IAction DecideAction();
}
