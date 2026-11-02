namespace MysteryDungeon.Turn;

public interface IAction
{
    ITurnActor Actor { get; }

    void Execute(int turnNumber);
}
