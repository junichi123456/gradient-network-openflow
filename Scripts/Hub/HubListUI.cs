using System;
using System.Collections.Generic;
using Godot;

namespace MysteryDungeon.Hub;

// 倉庫・店・診療所・研究所で共有する軽量な一覧UI（キーボード専用）。
// ↑↓ 項目 / ←→ 数量 / Enter 実行 / Tab か E で次のタブ・Q で前のタブ / Esc 閉じる。
// PartySetupUI / DungeonSelectUI と同じく、ラベルを作り直すだけの素朴な作り。
public partial class HubListUI : Control
{
    public record Row(string Id, string Label, int Max);

    public class Tab
    {
        public string Title = "";
        public Func<List<Row>> Rows = () => new();
        // 実行。返した文をメッセージ欄に出す（null なら何もしない）。
        public Func<Row, int, string> Act = (_, _) => null;
        public bool UseQuantity = true;
    }

    private HubPlayer _player;
    private VBoxContainer _list;
    private List<Tab> _tabs = new();
    private Func<string> _headerText = () => "";
    private int _tab, _cursor, _qty = 1;
    private List<Row> _rows = new();
    private string _msg = "";

    public bool IsOpen { get; private set; }
    public event Action Closed;

    public void Initialize(HubPlayer player)
    {
        _player = player;
        MouseFilter = MouseFilterEnum.Ignore;
        Position = new Vector2(160, 60);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(760, 0) };
        AddChild(panel);
        _list = new VBoxContainer();
        panel.AddChild(_list);
        Visible = false;
    }

    public void Open(string title, List<Tab> tabs, Func<string> headerText)
    {
        _tabs = tabs; _headerText = headerText;
        _tab = 0; _cursor = 0; _qty = 1; _msg = "";
        Name = title;
        IsOpen = true; Visible = true;
        if (_player != null) _player.InputEnabled = false;
        Reload(title);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false; Visible = false;
        if (_player != null) _player.InputEnabled = true;
        Closed?.Invoke();
    }

    private void Reload(string title = null)
    {
        _rows = _tabs[_tab].Rows();
        _cursor = Mathf.Clamp(_cursor, 0, Math.Max(0, _rows.Count - 1));
        Render(title ?? Name);
    }

    private int MaxQty() => _rows.Count == 0 ? 1 : Math.Max(1, Math.Min(99, _rows[_cursor].Max));

    public override void _UnhandledInput(InputEvent e)
    {
        if (!IsOpen) return;
        bool handled = true;
        if (e.IsActionPressed("ui_cancel")) Close();
        else if (e.IsActionPressed("ui_up") && _rows.Count > 0) { _cursor = (_cursor - 1 + _rows.Count) % _rows.Count; _qty = 1; Reload(); }
        else if (e.IsActionPressed("ui_down") && _rows.Count > 0) { _cursor = (_cursor + 1) % _rows.Count; _qty = 1; Reload(); }
        else if (e.IsActionPressed("ui_left") && _tabs[_tab].UseQuantity) { _qty = Math.Max(1, _qty - 1); Reload(); }
        else if (e.IsActionPressed("ui_right") && _tabs[_tab].UseQuantity) { _qty = Math.Min(MaxQty(), _qty + 1); Reload(); }
        else if (e is InputEventKey { Pressed: true, Echo: false } k && (k.Keycode == Key.Tab || k.Keycode == Key.E)) SwitchTab(1);
        else if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Q }) SwitchTab(-1);
        else if (e.IsActionPressed("ui_accept") && _rows.Count > 0)
        {
            var m = _tabs[_tab].Act(_rows[_cursor], Math.Min(_qty, MaxQty()));
            if (m != null) _msg = m;
            _qty = 1; Reload();
        }
        else handled = false;
        if (handled) GetViewport().SetInputAsHandled();
    }

    private void SwitchTab(int d)
    {
        _tab = (_tab + d + _tabs.Count) % _tabs.Count; _cursor = 0; _qty = 1; _msg = "";
        Reload();
    }

    private void Render(string title)
    {
        foreach (Node c in _list.GetChildren()) c.QueueFree();
        _list.AddChild(new Label { Text = $"【{title}】  {_headerText()}" });
        var tabs = new List<string>();
        for (int i = 0; i < _tabs.Count; i++) tabs.Add(i == _tab ? $"[{_tabs[i].Title}]" : $" {_tabs[i].Title} ");
        _list.AddChild(new Label { Text = string.Join("  ", tabs) + (_tabs.Count > 1 ? "   (Tab/E・Q で切替)" : "") });
        if (_rows.Count == 0) _list.AddChild(new Label { Text = "  (ありません)" });
        for (int i = 0; i < _rows.Count; i++)
        {
            string qty = i == _cursor && _tabs[_tab].UseQuantity ? $"  ◀ ×{Math.Min(_qty, MaxQty())} ▶" : "";
            _list.AddChild(new Label { Text = (i == _cursor ? "> " : "  ") + _rows[i].Label + qty });
        }
        _list.AddChild(new Label { Text = "↑↓ 項目  ←→ 数量  Enter 実行  Esc 閉じる" });
        if (_msg != "") _list.AddChild(new Label { Text = _msg, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(720, 0) });
    }
}
