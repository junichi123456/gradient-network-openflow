using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;
using MysteryDungeon.Species;
using MysteryDungeon.Visuals;

namespace MysteryDungeon.Story;

// 下部メッセージ窓＋名前欄＋スプライト枠。文字送り / 決定で全文表示 / オート送り（Aキー）/ ログ（Lキー）。
// IStoryHost を実装し、EventInterpreter から await で呼ばれる。立ち絵が無い間は種族スプライトを使う。
public partial class StoryDialogueUI : CanvasLayer, IStoryHost
{
    public const double CharsPerSecond = 40.0;
    public const double AutoAdvanceDelay = 1.2;

    private Control _root;
    private PanelContainer _window;
    private Label _nameLabel;
    private RichTextLabel _text;
    private TextureRect _sprite;
    private ColorRect _fade;
    private VBoxContainer _choiceBox;
    private PanelContainer _logPanel;
    private RichTextLabel _logText;
    private HBoxContainer _nameInputBox;
    private LineEdit _nameEdit;

    private TaskCompletionSource _advance;
    private double _typed;
    private bool _typing;
    private double _autoTimer;
    public bool AutoMode { get; set; }
    private readonly List<string> _log = new();

    public override void _Ready()
    {
        Layer = 50;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        _sprite = new TextureRect { CustomMinimumSize = new Vector2(160, 160), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Position = new Vector2(40, 120), Size = new Vector2(160, 160) };
        _root.AddChild(_sprite);

        _window = new PanelContainer();
        _window.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _window.OffsetTop = -150; _window.OffsetLeft = 16; _window.OffsetRight = -16; _window.OffsetBottom = -12;
        _root.AddChild(_window);
        var v = new VBoxContainer();
        _window.AddChild(v);
        _nameLabel = new Label(); v.AddChild(_nameLabel);
        _text = new RichTextLabel { BbcodeEnabled = false, FitContent = false, CustomMinimumSize = new Vector2(0, 90),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        v.AddChild(_text);

        _choiceBox = new VBoxContainer { Position = new Vector2(300, 140) };
        _root.AddChild(_choiceBox);

        _nameInputBox = new HBoxContainer { Position = new Vector2(300, 200), Visible = false };
        _nameEdit = new LineEdit { CustomMinimumSize = new Vector2(220, 0) };
        var ok = new Button { Text = "決定" };
        _nameInputBox.AddChild(_nameEdit); _nameInputBox.AddChild(ok);
        _root.AddChild(_nameInputBox);

        _logPanel = new PanelContainer { Visible = false };
        _logPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _logText = new RichTextLabel { ScrollFollowing = true };
        _logPanel.AddChild(_logText);
        _root.AddChild(_logPanel);

        _fade = new ColorRect { Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_fade);

        _root.Visible = false;
        _window.Visible = false;
        _root.GuiInput += OnGuiInput;
    }

    // --- 入力 ---

    private void OnGuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Confirm();
    }

    public override void _Input(InputEvent e)
    {
        if (!_root.Visible) return;
        if (e.IsActionPressed("ui_accept")) { Confirm(); GetViewport().SetInputAsHandled(); }
        else if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.A }) { AutoMode = !AutoMode; GetViewport().SetInputAsHandled(); }
        else if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.L }) { ToggleLog(); GetViewport().SetInputAsHandled(); }
    }

    private void Confirm()
    {
        if (_logPanel.Visible) return;
        if (_typing) { _typed = _text.GetTotalCharacterCount(); _text.VisibleCharacters = -1; _typing = false; return; }
        _advance?.TrySetResult();
    }

    private void ToggleLog()
    {
        _logPanel.Visible = !_logPanel.Visible;
        if (_logPanel.Visible) _logText.Text = string.Join("\n", _log);
    }

    public override void _Process(double delta)
    {
        if (_typing)
        {
            _typed += delta * CharsPerSecond;
            int total = _text.GetTotalCharacterCount();
            if (_typed >= total) { _typing = false; _text.VisibleCharacters = -1; }
            else _text.VisibleCharacters = (int)_typed;
        }
        else if (AutoMode && _advance != null && !_advance.Task.IsCompleted && _window.Visible)
        {
            _autoTimer += delta;
            if (_autoTimer >= AutoAdvanceDelay) _advance.TrySetResult();
        }
    }

    // --- IStoryHost ---

    private async Task Show(string name, string speciesId, string text)
    {
        _root.Visible = true; _window.Visible = true;
        _nameLabel.Text = name; _nameLabel.Visible = name != "";
        _sprite.Texture = SpriteFor(speciesId); _sprite.Visible = speciesId != "";
        _text.Text = text; _text.VisibleCharacters = 0; _typed = 0; _typing = true; _autoTimer = 0;
        _log.Add(name != "" ? $"{name}「{text}」" : text);
        _advance = new TaskCompletionSource();
        await _advance.Task;
        _window.Visible = false;
    }

    private static Texture2D SpriteFor(string speciesId)
    {
        if (string.IsNullOrEmpty(speciesId)) return null;
        string key = SpeciesDatabase.Instance?.Get(speciesId)?.SpriteKey ?? speciesId;
        return SpriteTextureLibrary.GetTexture(key, Vector2I.Down, Colors.Gray, 96);
    }

    public Task Say(SayLine line) => Show(line.DisplayName, line.SpeciesId, line.Text);
    public Task Narration(string text) => Show("", "", text);

    public async Task<int> Choice(IReadOnlyList<string> options)
    {
        _root.Visible = true;
        var tcs = new TaskCompletionSource<int>();
        foreach (Node c in _choiceBox.GetChildren()) c.QueueFree();
        for (int i = 0; i < options.Count; i++)
        {
            int idx = i;
            var b = new Button { Text = options[i] };
            b.Pressed += () => tcs.TrySetResult(idx);
            _choiceBox.AddChild(b);
            _log.Add("▶ " + options[i]);
        }
        (_choiceBox.GetChild(0) as Button)?.GrabFocus();
        int r = await tcs.Task;
        foreach (Node c in _choiceBox.GetChildren()) c.QueueFree();
        return r;
    }

    public async Task<string> NameInput(string defaultName, int maxLength)
    {
        _root.Visible = true;
        _nameEdit.Text = defaultName; _nameEdit.MaxLength = maxLength;
        var tcs = new TaskCompletionSource<string>();
        void Done() => tcs.TrySetResult(_nameEdit.Text);
        var ok = (Button)_nameInputBox.GetChild(1);
        ok.Pressed += Done; _nameEdit.TextSubmitted += _ => Done();
        _nameInputBox.Visible = true; _nameEdit.GrabFocus();
        string s = await tcs.Task;
        ok.Pressed -= Done;
        _nameInputBox.Visible = false;
        return s;
    }

    public async Task Fade(string mode, double seconds)
    {
        _root.Visible = true;
        var tw = CreateTween();
        tw.TweenProperty(_fade, "color:a", mode == "out" ? 1.0 : 0.0, seconds);
        await ToSignal(tw, Tween.SignalName.Finished);
    }

    public async Task Wait(double seconds)
        => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    public void Close() { _root.Visible = false; }
}
