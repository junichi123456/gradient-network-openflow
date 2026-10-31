using System.Collections.Generic;
using MysteryDungeon.Entities;

namespace MysteryDungeon.Battle;

// 対戦の決着。全滅による勝敗と、時間切れの引き分けを1つの型で表す。
public enum BattleOutcome
{
    Undecided,
    PlayerWin,
    EnemyWin,
    Draw,        // 対戦時間の20分が尽き、両陣営に生存者がいる
}

// フェーズの制限時間。実時間で進むが、経過は外から Advance() で与える。
// Godot の時間を直接読まないので、20分の対戦を待たずに検証できる。
public sealed class PhaseTimer
{
    public double LimitSeconds { get; }
    public double Elapsed { get; private set; }

    public PhaseTimer(double limitSeconds) => LimitSeconds = limitSeconds;

    public double Remaining => System.Math.Max(0.0, LimitSeconds - Elapsed);
    public bool Expired => Elapsed >= LimitSeconds;

    public void Advance(double seconds) => Elapsed += System.Math.Max(0.0, seconds);
    public void Reset() => Elapsed = 0.0;
}

// 対戦全体の時計。選出50秒・対戦20分を持ち、時間切れ時の既定挙動
// （自動選出／引き分け）を決める。
public sealed class BattleClock
{
    public const double SelectionLimitSeconds = 50.0;
    public const double DeployLimitSeconds = 20.0;
    public const double MatchLimitSeconds = 20.0 * 60.0;   // 20分

    public PhaseTimer Selection { get; } = new(SelectionLimitSeconds);
    public PhaseTimer Deploy { get; } = new(DeployLimitSeconds);
    public PhaseTimer Match { get; } = new(MatchLimitSeconds);

    private readonly Dictionary<Faction, IReadOnlyList<BattleEntry>> _submitted = new();

    // ---- 選出フェーズ ----

    // 手動で選出を確定する。登録6匹の中からちょうど4匹でなければ拒否する。
    public bool SubmitSelection(Faction faction, BattleTeam team, IReadOnlyList<BattleEntry> selection)
    {
        if (SelectionClosed) return false;                 // 締め切り後は受け付けない
        if (team.ValidateSelection(selection).Count > 0) return false;
        _submitted[faction] = selection;
        return true;
    }

    public bool HasSubmitted(Faction faction) => _submitted.ContainsKey(faction);

    // 両者の選出が揃ったか、50秒が尽きたら次のフェーズへ進む。
    public bool SelectionClosed =>
        Selection.Expired
        || (_submitted.ContainsKey(Faction.Player) && _submitted.ContainsKey(Faction.Enemy));

    // その陣営の確定選出。未提出のまま時間切れになった側は、登録順の昇順で
    // 自動選出される。
    public IReadOnlyList<BattleEntry> ResolveSelection(Faction faction, BattleTeam team) =>
        _submitted.TryGetValue(faction, out var s) ? s : team.AutoSelect();

    // ---- 配置フェーズ ----

    // 20秒。選出と違い、締め切り時点で必ず有効な配置が存在する
    // （BattleDeployment.Default が4匹すべてを置いた状態から始まるので、
    // 何も触らなければ既定配置のまま開始する）。よって「自動で決める」
    // 処理は要らず、締め切りの判定だけを持つ。
    public bool DeployClosed => Deploy.Expired;

    // ---- 対戦フェーズ ----

    // 現在の決着。全滅が最優先で、どちらも全滅していなければ20分の判定に移る。
    // 20分が尽きるまでは Undecided のまま。
    public BattleOutcome Resolve(BattleScheduler scheduler)
    {
        var winner = scheduler.Winner();
        if (winner == Faction.Player) return BattleOutcome.PlayerWin;
        if (winner == Faction.Enemy) return BattleOutcome.EnemyWin;

        // Winner() が null を返すのは「両者生存」と「相打ちで両者全滅」の
        // 2通り。後者はどちらの勝ちでもないので引き分けにする。
        if (!scheduler.AnyAlive(Faction.Player) && !scheduler.AnyAlive(Faction.Enemy))
            return BattleOutcome.Draw;

        return Match.Expired ? BattleOutcome.Draw : BattleOutcome.Undecided;
    }
}
