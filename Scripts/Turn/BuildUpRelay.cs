using MysteryDungeon.Entities;

namespace MysteryDungeon.Turn;

// ビルドアップ (trait_catalog_v2 stage 9 §1, ボルゼクス): "このパルの次に
// 行動する味方1体は、その行動を完了するまでの間 被ダメージ-25%".
//
// Two properties make this unlike every other buff in the project:
//
//  1. The RECIPIENT is not known when the buff is created. The scheduler is
//     energy-based (TurnScheduler), so "who acts next" only becomes true at
//     the moment they actually act - it cannot be predicted at arm time
//     without duplicating the energy model. So the holder arms a pending
//     TOKEN scoped to their faction, and the next same-faction actor to
//     begin an action claims it.
//
//  2. The lifetime is one ACTION, not a number of turns. 攻守一体's
//     countdown expires on action-cycles elapsed; this expires when the
//     recipient's single action finishes. Hence the explicit
//     claim-before / release-after bracket around the Execute call, rather
//     than a tick-driven decrement.
//
// Both turn entry points (TurnScheduler.Tick for NPCs,
// TurnManager.SubmitPlayerAction for the player) run the same bracket, so
// the player is as eligible to receive it as any ally.
//
// State is a single pending faction rather than a per-entity flag because
// the token exists in the gap between "holder acted" and "someone claimed
// it", when it belongs to nobody. Reset() is called on floor transition
// (FloorController) so a token armed on the last floor cannot leak.
public static class BuildUpRelay
{
    private static Faction? _pending;

    // Called after a ビルドアップ holder finishes acting.
    public static void Arm(Faction faction) => _pending = faction;

    public static void Reset() => _pending = null;

    // Claims the pending token for `actor` if one is waiting for their
    // faction and they are not the holder that armed it. Returns true when
    // the shield was applied, so the caller knows to release it afterwards.
    public static bool TryClaim(Entity actor)
    {
        if (actor == null || _pending == null || actor.Faction != _pending.Value) return false;

        // The holder's own next action must not consume the token it just
        // armed - the effect targets "次に行動する味方", someone else.
        if (actor.Stats.Trait == "build_up") return false;

        _pending = null;
        actor.StatusEffects.SetBuildUpShield(true);
        return true;
    }

    public static void Release(Entity actor) => actor?.StatusEffects.SetBuildUpShield(false);
}
