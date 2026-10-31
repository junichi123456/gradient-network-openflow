using Godot;

namespace MysteryDungeon.UI.Battle;

// HPバー。実HPの下に「直前の値」を残す二層構造にしてある。
//
// 静止した画面からでも「さっき何点入ったか」が読めるのが狙いで、
// 対戦中いちばん見る場所なのでここに手をかけている。格闘ゲームやRPGで
// 定着した見せ方。
//
//   下層(ghost) … 直前のHP。被弾後しばらく残ってから追いつく
//   上層(now)   … 現在のHP。残量で色が変わる
public partial class HpBar : Control
{
    // ghost が追いつき始めるまでの間。ここで「減った」と気づかせる。
    private const double LingerSeconds = 0.35;
    // 追いつく速さ（1秒あたりの割合）。速すぎると量が読めない。
    private const float CatchUpPerSecond = 0.9f;

    private float _now = 1f;
    private float _ghost = 1f;
    private double _linger;

    public int CornerRadius { get; set; } = 3;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 7);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    // 現在HPを与える。減少時のみ ghost を遅らせる（回復は即座に追従させる。
    // 回復で赤い帯が残ると「減った」と誤読されるため）。
    public void SetHp(int current, int max)
    {
        float ratio = max <= 0 ? 0f : Mathf.Clamp((float)current / max, 0f, 1f);
        if (ratio < _now)
        {
            _linger = LingerSeconds;      // 減った: ghost は据え置いて差を見せる
        }
        else
        {
            _ghost = ratio;               // 回復・初期化: 即追従
            _linger = 0;
        }
        _now = ratio;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_ghost <= _now) return;

        if (_linger > 0) { _linger -= delta; QueueRedraw(); return; }

        _ghost = Mathf.Max(_now, _ghost - CatchUpPerSecond * (float)delta);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var full = new Rect2(Vector2.Zero, Size);
        DrawRect(full, BattleTheme.Sunk);

        if (_ghost > 0f)
            DrawRect(new Rect2(0, 0, Size.X * _ghost, Size.Y),
                     new Color(BattleTheme.Crit, 0.5f));

        if (_now > 0f)
            DrawRect(new Rect2(0, 0, Size.X * _now, Size.Y),
                     BattleTheme.HpColor(_now));
    }
}
