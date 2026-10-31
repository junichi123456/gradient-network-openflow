using System;
using Godot;

namespace MysteryDungeon.Hub;

// 汎用の「近づいて Enter」地点（広場の施設・対戦広場の入口）。
// 範囲に入っている間に ui_accept で OnInteract。Blocked が true の間（会話中・UI表示中）は反応しない。
public partial class HubSpot : Area2D
{
    public Action OnInteract { get; set; }
    public Func<bool> Blocked { get; set; }
    public string Hint { get; set; } = "";
    private bool _inside;

    public override void _Ready()
    {
        BodyEntered += b => { if (b is HubPlayer) { _inside = true; if (Hint != "") GD.Print($"[Hub] {Hint}"); } };
        BodyExited += b => { if (b is HubPlayer) _inside = false; };
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_inside || !@event.IsActionPressed("ui_accept")) return;
        if (Blocked != null && Blocked()) return;
        GetViewport().SetInputAsHandled();
        OnInteract?.Invoke();
    }
}
