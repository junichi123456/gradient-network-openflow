using MysteryDungeon.UI;

namespace MysteryDungeon.Dungeon;

// Mutable weather for one floor. A plain class owned by FloorController,
// exactly like FieldManager - no Node, no autoload, so a bare test
// entity simply has no access to one and reads WeatherType.None.
//
// Two sources feed it, per the confirmed design:
//   - the dungeon's own definition (DungeonRule.Weather), applied at
//     floor generation. This one is PERMANENT for the floor: Remaining
//     stays at Endless and never ticks down.
//   - a move or trait, via Set(type, turns). This one is temporary and
//     expires; when it does, the floor falls back to the dungeon's own
//     weather rather than to None, so a Rain move used in a permanently
//     Sunny dungeon eventually gives the sun back.
public class WeatherState
{
    // Sentinel for "does not expire" - the dungeon-defined baseline.
    public const int Endless = -1;

    public WeatherType Current { get; private set; } = WeatherType.None;
    public int Remaining { get; private set; } = Endless;

    // The dungeon's own weather, restored whenever a temporary one ends.
    private WeatherType _baseline = WeatherType.None;

    public bool IsTemporary => Remaining != Endless;

    // Called at floor generation with the dungeon definition's weather.
    // Also clears any temporary weather carried in from the previous
    // floor - weather does not survive a staircase.
    public void SetBaseline(WeatherType weather)
    {
        _baseline = weather;
        Current = weather;
        Remaining = Endless;
    }

    // Move/trait-driven weather. turns <= 0 is treated as "no change"
    // rather than as an instant expiry, so a mis-authored move can't
    // silently wipe the dungeon's weather for zero turns.
    public void Set(WeatherType weather, int turns)
    {
        if (turns <= 0) return;

        bool changed = Current != weather;
        Current = weather;
        Remaining = turns;
        if (changed)
            MessageLogger.Log($"The weather turned to {WeatherTypeNames.Japanese(weather)}!", MessageLogger.ProgressionColor);
    }

    // One tick per game turn (FloorController.OnTurnEnded), NOT per
    // action-cycle - a fast actor taking two actions in one turn must not
    // burn two turns of weather.
    public void AdvanceTurn()
    {
        if (Remaining == Endless) return;

        Remaining--;
        if (Remaining > 0) return;

        Current = _baseline;
        Remaining = Endless;
        MessageLogger.Log(
            _baseline == WeatherType.None
                ? "The weather returned to normal."
                : $"The weather returned to {WeatherTypeNames.Japanese(_baseline)}.",
            MessageLogger.ProgressionColor);
    }

    public void Reset()
    {
        _baseline = WeatherType.None;
        Current = WeatherType.None;
        Remaining = Endless;
    }
}
