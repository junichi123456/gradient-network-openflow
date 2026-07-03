using Godot;

namespace MysteryDungeon.UI;

// Global pub/sub for player-facing narrative text (combat results, level
// ups, dungeon events). A thin facade over one signal so call sites deep
// in Turn/Entities/Dungeon classes don't need an on-screen UI reference
// threaded through every constructor just to post a line - they call the
// static Log() the same way they used to call GD.Print(). Registered as
// a project.godot autoload; MessageLogUI (dungeon-only) subscribes to
// MessageLogged and renders the on-screen log. The Hub scene has no
// MessageLogUI, so anything logged there simply has no on-screen
// listener - GD.Print remains the right tool for pure console/debug
// output and is untouched by this.
public partial class MessageLogger : Node
{
    [Signal] public delegate void MessageLoggedEventHandler(string message, Color color);

    public static MessageLogger Instance { get; private set; }

    public static readonly Color NeutralColor = Colors.White;
    public static readonly Color EffectiveColor = new(1f, 0.55f, 0.15f);   // super effective - orange
    public static readonly Color IneffectiveColor = new(0.6f, 0.6f, 0.6f); // not very effective / miss / whiff - gray
    public static readonly Color FaintColor = new(1f, 0.35f, 0.35f);       // fainted / death / danger - red
    public static readonly Color ProgressionColor = new(1f, 0.9f, 0.3f);   // EXP / level up / recruit / clear - gold
    public static readonly Color HealColor = new(0.4f, 0.9f, 0.4f);        // HP/Belly restore - green

    public override void _Ready() => Instance = this;

    public static void Log(string message, Color? color = null)
    {
        GD.Print($"[Log] {message}"); // keep the console trail every earlier phase relied on
        Instance?.EmitSignal(SignalName.MessageLogged, message, color ?? NeutralColor);
    }
}
