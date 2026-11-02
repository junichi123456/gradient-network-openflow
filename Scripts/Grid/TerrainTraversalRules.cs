namespace MysteryDungeon.Grid;

// Single source of truth for "can a mover with this MovementProfile
// stand on this terrain" - shared by EntityStats.CanTraverse (per-step
// movement validation) and AStarPathfinder (pathfinding grid
// construction) so the two can never drift apart.
public static class TerrainTraversalRules
{
    public static bool IsWalkable(TerrainType terrain, MovementProfile profile)
    {
        return terrain switch
        {
            TerrainType.Wall => false,
            TerrainType.Floor => true,
            TerrainType.Water => profile is MovementProfile.Hover or MovementProfile.WaterIceImmune or MovementProfile.FireWaterImmune,
            TerrainType.Lava => profile is MovementProfile.Hover or MovementProfile.FireImmune or MovementProfile.FireWaterImmune,
            TerrainType.Chasm => profile is MovementProfile.Hover,
            _ => false,
        };
    }
}
