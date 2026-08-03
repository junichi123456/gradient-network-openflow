namespace MysteryDungeon.Dungeon;

// Floor-wide weather. Applies uniformly to every actor on the floor -
// there is no per-faction or per-entity weather, so anything that reads
// this only needs the FloorController, never the actor's allegiance.
//
// None is the default everywhere on purpose: an entity with no
// FloorController (a bare test entity, the Hub) resolves to None, and
// every weather hook is written so that None is a strict no-op. That is
// what keeps the Phase 16 damage benchmarks (70/70/pw20 -> 7,
// 75/75/pw35 -> 13) and the existing accuracy maths bit-identical.
public enum WeatherType
{
    None,
    Sunny,      // はれ
    Rain,       // あめ
    Snow,       // ゆき
    Sandstorm,  // すなあらし
    Gale,       // きょうふう
    Fog,        // きり
}

public static class WeatherTypeNames
{
    // Display names for the HUD/log. Kept next to the enum rather than in
    // the UI layer so every surface spells a weather the same way.
    public static string Japanese(WeatherType weather) => weather switch
    {
        WeatherType.Sunny => "はれ",
        WeatherType.Rain => "あめ",
        WeatherType.Snow => "ゆき",
        WeatherType.Sandstorm => "すなあらし",
        WeatherType.Gale => "きょうふう",
        WeatherType.Fog => "きり",
        _ => "なし",
    };
}
