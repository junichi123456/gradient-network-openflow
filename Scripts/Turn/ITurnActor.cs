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

    // Phase 21: before-action check (Freeze/Stun) - true means this whole
    // action-cycle is skipped (see StatusEffectManager.TryConsumeActionLock).
    // Shared verbatim by TurnScheduler.Tick (NPCs/allies) and
    // TurnManager.SubmitPlayerAction (the player), so freeze/stun/paralyze
    // need no player-specific code anywhere in Player.cs's input handler.
    bool IsActionLocked();

    // Paralyze-only: swaps a chosen MoveAction/SwapAction for a WaitAction,
    // leaving AttackAction (and anything else) untouched. Called on the
    // already-decided action, after IsActionLocked() found the actor free
    // to act.
    IAction FilterActionForStatus(IAction action);

    // After-action hook: DoT damage (poison/toxic/burn), Paralyze's own
    // clear check, and rank decay - called once per action-cycle for
    // every actor, whether or not that cycle's action was skipped.
    void ResolveStatusTick();
}
