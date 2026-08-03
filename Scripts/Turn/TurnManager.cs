using Godot;
using MysteryDungeon.UI;

namespace MysteryDungeon.Turn;

// Orchestrates the synchronous turn queue: waits for the player to
// submit an action, executes it, then lets the TurnScheduler resolve
// every registered NPC's energy/actions for that same turn before
// returning to WaitingForPlayerInput.
//
// ふわふわ/ゆきすべり put one seam in that otherwise straight line: after
// the player ATTACKS, the turn can stay open for a single follow-up step
// (AwaitingFollowUpMove) before the NPCs are ticked. Everything after the
// player's action - the status tick, the NPC tick, the TurnEnded signal -
// is shared by both paths via FinishTurn, so the follow-up can never end
// up half-resolving a turn.
public partial class TurnManager : Node
{
    [Signal] public delegate void TurnStartedEventHandler(int turnNumber);
    [Signal] public delegate void TurnEndedEventHandler(int turnNumber);

    public TurnState CurrentState { get; private set; } = TurnState.WaitingForPlayerInput;
    public int TurnCount { get; private set; } = 0;

    private readonly TurnScheduler _scheduler = new();

    // The actor owed a follow-up step while CurrentState is
    // AwaitingFollowUpMove; null in every other state.
    private ITurnActor _followUpActor;

    public void RegisterActor(ITurnActor actor) => _scheduler.Register(actor);

    public void UnregisterActor(ITurnActor actor) => _scheduler.Unregister(actor);

    public void SubmitPlayerAction(IAction playerAction)
    {
        if (CurrentState != TurnState.WaitingForPlayerInput) return;

        CurrentState = TurnState.ProcessingTurn;
        TurnCount++;
        EmitSignal(SignalName.TurnStarted, TurnCount);
        GD.Print($"--- [Turn {TurnCount}] player action submitted ---");

        // Phase 21: the exact same before/filter/after sequence
        // TurnScheduler.Tick runs for NPCs (see there for the full
        // rationale) - Player._UnhandledInput needs no freeze/stun/
        // paralyze-awareness of its own, since every submitted action
        // funnels through here.
        var actor = playerAction.Actor;
        if (actor.IsActionLocked())
        {
            GD.Print($"[Turn {TurnCount}] {actor.ActorName} cannot act this turn (frozen/stunned).");
            MessageLogger.Log($"{actor.ActorName} can't move!", MessageLogger.IneffectiveColor);
        }
        else
        {
            // Same ビルドアップ bracket TurnScheduler.Tick runs for NPCs, so
            // the player can both receive and arm the shield (see BuildUpRelay).
            var entity = actor as Entities.Entity;
            bool shielded = BuildUpRelay.TryClaim(entity);

            var action = actor.FilterActionForStatus(playerAction);
            action.Execute(TurnCount);

            if (shielded) BuildUpRelay.Release(entity);
            if (entity != null && entity.Stats.Trait == "build_up")
                BuildUpRelay.Arm(entity.Faction);

            // ふわふわ/ゆきすべり: hold the turn open for one more input.
            // The status tick and the NPC tick are deliberately NOT run
            // yet - the follow-up step belongs to this same turn.
            if (action is AttackAction { PerformedAttack: true }
                && entity != null && entity.CanFollowUpMoveAfterAttack())
            {
                _followUpActor = actor;
                CurrentState = TurnState.AwaitingFollowUpMove;
                MessageLogger.Log(
                    $"{actor.ActorName} can slip away! (a direction key to step, wait key to stay)",
                    MessageLogger.ProgressionColor);
                return;
            }
        }

        FinishTurn(actor);
    }

    // Second half of a turn held open by AwaitingFollowUpMove. A null
    // action means the player declined the step; either way the turn then
    // finishes exactly as a normal one does.
    public void SubmitFollowUpMove(IAction followUpAction)
    {
        if (CurrentState != TurnState.AwaitingFollowUpMove) return;

        var actor = _followUpActor;
        _followUpActor = null;
        CurrentState = TurnState.ProcessingTurn;

        followUpAction?.Execute(TurnCount);
        FinishTurn(actor);
    }

    // Everything that closes a turn, shared by both entry points above.
    private void FinishTurn(ITurnActor actor)
    {
        actor.ResolveStatusTick();

        _scheduler.Tick(TurnCount);

        EmitSignal(SignalName.TurnEnded, TurnCount);
        CurrentState = TurnState.WaitingForPlayerInput;
    }
}
