using Godot;
using MysteryDungeon.Grid;

namespace MysteryDungeon.Dungeon;

// Pure field-of-view policy: decides what "currently visible" means for
// the player's position (whole room vs. a 3x3 window), then delegates
// the actual tile mutation to GridManager's low-level primitives. Only
// depends on GridManager - it never needs the per-floor Room list, since
// Tile.RoomId alone is enough to answer "which room is this".
public static class FovManager
{
    private const int CorridorRadius = 1; // 3x3 window while in a corridor

    public static void UpdateVisibility(GridManager grid, Vector2I playerPos)
    {
        grid.ClearVisibility();

        int roomId = grid.GetRoomId(playerPos);
        if (roomId >= 0)
            grid.RevealRoom(roomId);
        else
            grid.RevealAround(playerPos, CorridorRadius);

        grid.RefreshTileMap();
    }
}
