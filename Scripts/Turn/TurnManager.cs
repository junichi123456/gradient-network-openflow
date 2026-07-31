using Godot;
using MysteryDungeon.UI;

namespace MysteryDungeon.Turn;

// Orchestrates the synchronous turn queue: waits for the player to
// submit an action, executes it, then lets the TurnScheduler resolve
// every registered NPC's energy/actions for that same turn before
// returning to WaitingForPlayerInput.
public partial class TurnManager : Node
{
    [Signal] public delegate void TurnStartedEventHandler(int turnNumber);
    [Signal] public delegate void TurnEndedEventHandler(int turnNumber);

    public TurnState CurrentState { get; private set; } = TurnState.WaitingForPlayerInput;
    public int TurnCount { get; private set; } = 0;

    private readonly TurnScheduler _scheduler = new();

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
        }
        actor.ResolveStatusTick();

        _scheduler.Tick(TurnCount);

        EmitSignal(SignalName.TurnEnded, TurnCount);
        CurrentState = TurnState.WaitingForPlayerInput;
    }
}
