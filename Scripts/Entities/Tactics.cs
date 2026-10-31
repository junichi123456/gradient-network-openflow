namespace MysteryDungeon.Entities;

// Player-assigned ally behavior mode ("さくせん" - see MenuUI's Tactics
// screen). ActFreely is AllyEntity's original always-engage AI;
// FollowOnly keeps the ally out of combat entirely, so a fragile/support
// pal never gets pulled into a fight the player didn't send it into.
public enum Tactics
{
    ActFreely,
    FollowOnly,
}
