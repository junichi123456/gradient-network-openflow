namespace MysteryDungeon.Turn;

public enum TurnState
{
    WaitingForPlayerInput,
    ProcessingTurn,

    // ふわふわ/ゆきすべり: the player attacked and the trait+weather pairing
    // grants a follow-up step, so the turn is deliberately left open for a
    // SECOND player input before the NPCs are ticked. Only the player needs
    // this state - an NPC's follow-up is decided and executed inline by
    // TurnScheduler with no waiting.
    AwaitingFollowUpMove,
}
