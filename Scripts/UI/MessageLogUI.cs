using Godot;
using System.Collections.Generic;

namespace MysteryDungeon.UI;

// Bottom-of-screen scrolling text log (PMD Explorers of Sky style) - see
// MessageLogger for the publish side. Self-contained: subscribes
// directly to the MessageLogger autoload in _Ready(), no Initialize()
// wiring needed from the composition root, unlike HUD/MinimapUI/MenuUI.
public partial class MessageLogUI : Control
{
    private const int MaxLines = 6;
    private const float BoxWidth = 760f;
    private const float BoxHeight = 110f;

    private VBoxContainer _list;
    private readonly List<(string Text, Color Color)> _lines = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        var viewportSize = GetViewport().GetVisibleRect().Size;
        Position = new Vector2((viewportSize.X - BoxWidth) / 2f, viewportSize.Y - BoxHeight - 10f);

        var bg = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.55f),
            Size = new Vector2(BoxWidth, BoxHeight),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(bg);

        _list = new VBoxContainer { Position = new Vector2(10, 6) };
        AddChild(_list);

        if (MessageLogger.Instance != null)
            MessageLogger.Instance.MessageLogged += OnMessageLogged;
    }

    private void OnMessageLogged(string message, Color color)
    {
        _lines.Add((message, color));
        while (_lines.Count > MaxLines)
            _lines.RemoveAt(0);

        Refresh();
    }

    private void Refresh()
    {
        foreach (Node child in _list.GetChildren())
            child.QueueFree();

        foreach (var (text, color) in _lines)
            _list.AddChild(new Label { Text = text, Modulate = color });
    }
}
