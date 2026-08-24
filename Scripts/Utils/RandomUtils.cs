using Godot;
using System.Collections.Generic;

namespace MysteryDungeon.Utils;

public static class RandomUtils
{
    public static readonly Vector2I[] Neighbors4 =
    {
        new(0, -1), new(0, 1), new(-1, 0), new(1, 0),
    };

    // Fisher-Yates shuffle of the 4 cardinal directions, used by NPC AI
    // to try candidate moves in random order.
    public static List<Vector2I> ShuffledNeighbors4()
    {
        var list = new List<Vector2I>(Neighbors4);
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = (int)(GD.Randi() % (uint)(i + 1));
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
