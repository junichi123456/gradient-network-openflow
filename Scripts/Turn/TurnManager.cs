using Godot;

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

        playerAction.Execute(TurnCount);

        _scheduler.Tick(TurnCount);

        EmitSignal(SignalName.TurnEnded, TurnCount);
        CurrentState = TurnState.WaitingForPlayerInput;
    }
}
