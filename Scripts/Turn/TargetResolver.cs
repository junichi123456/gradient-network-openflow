using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Dungeon;
using MysteryDungeon.Entities;
using MysteryDungeon.Grid;

namespace MysteryDungeon.Turn;

// Move-consumption phase: turns a MoveRange shape into the concrete list
// of actors an AoE move hits. Pure/immediate, no side effects - callers
// (AttackAction's multi-target loop) apply damage/status themselves.
//
// Friendly-fire is on by default (confirmed §9-2): every actor EXCEPT the
// user is a candidate, allies included, and downed actors are already
// absent from FloorController.AllActors(). Room is the one exception - see
// Resolve.
public static class TargetResolver
{
    // Adjacent is the single-target/bump path (handled by AttackAction
    // directly, never routed here). Everything else collects a tile set
    // and returns the live actors standing on it.
    //
    // aimTile centres an Area blast and is the corridor fallback for a
    // Room move - AttackAction passes the primary defender's tile (or the
    // tile the user faces when there's no primary defender).
    public static List<Entity> Resolve(MoveRange range, Entity user, Vector2I aimTile, GridManager grid, FloorController floor)
    {
        var candidates = floor.AllActors().Where(e => e != user).ToList();

        // Room alone spares the user's own side. Area/Line/TwoTile/FullFloor
        // keep the project's default friendly fire.
        if (range == MoveRange.Room)
            candidates = candidates.Where(e => e.Faction != user.Faction).ToList();

        // FullFloor ignores geometry entirely - everyone but the user.
        if (range == MoveRange.FullFloor)
            return candidates;

        var tiles = ResolveTiles(range, user.GridPosition, user.FacingDirection, aimTile, grid, floor);

        // Line and TwoTile do not pierce: the shot stops at the first actor it
        // meets and only that actor is hit. CastRay already returns its tiles
        // in travel order, so the first match down the list is the first thing
        // the shot reaches.
        //
        // They part ways on what an ally in the path does. Neither ever damages
        // one, but the reason differs:
        //
        //   Line     - an ally BLOCKS. The shot is called off the moment the
        //              first actor in the lane turns out to be friendly, so
        //              nothing at all is hit, not even the enemy behind them.
        //   TwoTile  - an ally is stepped over as if it were not there, so
        //              ally-then-enemy hits the enemy behind. Still no piercing
        //              between enemies: enemy-then-enemy hits only the near
        //              one, and two allies leave nothing to hit.
        //
        // Either way an empty list comes back and AttackAction reports the move
        // as connecting with nothing.
        if (range == MoveRange.Line || range == MoveRange.TwoTile)
        {
            bool allyAborts = range == MoveRange.Line;
            foreach (var tile in tiles)
                foreach (var actor in candidates)
                {
                    if (actor.GridPosition != tile) continue;
                    if (actor.Faction == user.Faction)
                    {
                        if (allyAborts) return new List<Entity>();   // Line: called off
                        continue;                                    // TwoTile: stepped over
                    }
                    return new List<Entity> { actor };
                }
            return new List<Entity>();
        }

        var tileSet = new HashSet<Vector2I>(tiles);
        return candidates.Where(e => tileSet.Contains(e.GridPosition)).ToList();
    }

    // The tile set a shape covers. Pure geometry - exposed for testing.
    public static List<Vector2I> ResolveTiles(MoveRange range, Vector2I userPos, Vector2I facing, Vector2I aimTile, GridManager grid, FloorController floor)
    {
        var tiles = new List<Vector2I>();

        switch (range)
        {
            case MoveRange.Line:
                CastRay(userPos, facing, int.MaxValue, grid, tiles);
                break;

            case MoveRange.TwoTile:
                CastRay(userPos, facing, 2, grid, tiles);
                break;

            case MoveRange.Area: // 3x3 centred on the impact tile
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        var t = aimTile + new Vector2I(dx, dy);
                        if (grid.InBounds(t)) tiles.Add(t);
                    }
                break;

            case MoveRange.Room:
                var bounds = floor.GetRoomBoundsAt(userPos);
                if (bounds == null)
                {
                    tiles.Add(aimTile); // corridor -> single-target fallback (§4-1)
                }
                else
                {
                    var r = bounds.Value;
                    for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
                        for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
                            tiles.Add(new Vector2I(x, y));
                }
                break;
        }

        return tiles;
    }

    // Steps from userPos along `facing`, adding each tile until it leaves
    // bounds or hits a Wall (the projectile stops AT the wall - the wall
    // tile itself isn't added). maxSteps caps the length (TwoTile = 2).
    private static void CastRay(Vector2I userPos, Vector2I facing, int maxSteps, GridManager grid, List<Vector2I> tiles)
    {
        if (facing == Vector2I.Zero) return;

        var pos = userPos;
        for (int step = 0; step < maxSteps; step++)
        {
            pos += facing;
            if (!grid.InBounds(pos)) break;
            if (grid.GetTile(pos).Terrain == TerrainType.Wall) break; // stops at the wall
            tiles.Add(pos);
        }
    }
}
