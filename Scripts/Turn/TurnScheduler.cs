using Godot;
using System.Collections.Generic;

namespace MysteryDungeon.Turn;

// Energy-accumulation speed model:
// every Tick(), each registered actor gains energy equal to its Speed.
// Whenever an actor's energy reaches ActionThreshold (100) it acts and
// loses 100 energy, looping while it still has >= 100 (so Speed=200
// acts twice per tick, Speed=100 acts once, Speed=50 would act once
// every two ticks). The Player is NOT registered here - it acts
// directly through TurnManager.SubmitPlayerAction on player input.
public class TurnScheduler
{
    public const int ActionThreshold = 100;

    private readonly List<ITurnActor> _actors = new();
    private readonly Dictionary<ITurnActor, int> _energy = new();

    public void Register(ITurnActor actor)
    {
        if (_actors.Contains(actor)) return;
        _actors.Add(actor);
        _energy[actor] = 0;
    }

    public void Unregister(ITurnActor actor)
    {
        _actors.Remove(actor);
        _energy.Remove(actor);
    }

    public void Tick(int turnNumber)
    {
        // Snapshot so an actor dying mid-tick can't corrupt iteration.
        foreach (var actor in new List<ITurnActor>(_actors))
        {
            if (!actor.IsAlive)
            {
                Unregister(actor);
                continue;
            }

            int before = _energy[actor];
            _energy[actor] = before + actor.Speed;
            GD.Print($"[Turn {turnNumber}] {actor.ActorName} energy {before} + {actor.Speed}(speed) = {_energy[actor]}");

            while (_energy[actor] >= ActionThreshold)
            {
                _energy[actor] -= ActionThreshold;
                GD.Print($"[Turn {turnNumber}] {actor.ActorName} reaches {ActionThreshold} energy -> acts (remaining {_energy[actor]})");

                if (!actor.IsAlive) break;

                IAction action = actor.DecideAction();
                action?.Execute(turnNumber);
            }
        }
    }
}
