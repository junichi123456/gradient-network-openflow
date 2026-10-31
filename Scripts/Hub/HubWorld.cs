using System;
using Godot;
using MysteryDungeon.Visuals;

namespace MysteryDungeon.Hub;

// hub_map.json から十字型4画面の世界（床・壁・カメラ・据え置きの設備）と、広場の施設ノードを組み立てる。
// 仮素材（色付きの四角＋名札）。ノードの組み立てだけで、進行の判断は HubScene が持つ。
public static class HubWorld
{
    public static readonly Vector2 PlayerHalf = new(14, 14);   // HubPlayer の CollisionShape（28x28）の半分

    public static Color Hex(string s) => Color.FromHtml(s);

    public static Label Tag(string text, Vector2 pos)
    {
        var l = new Label { Text = text, Position = pos, ZIndex = 5 };
        l.AddThemeColorOverride("font_color", Colors.White);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        l.AddThemeConstantOverride("outline_size", 4);
        return l;
    }

    // 床・壁・見えない外壁・カメラ（可動域 = 4画面の外接矩形）。
    public static void BuildStatic(Node2D root, HubMapDef map, HubGeometry geo, HubPlayer player)
    {
        var floors = new Node2D { Name = "Floors", ZIndex = -10 };
        root.AddChild(floors);
        var tints = new[] { "#2b3a2b", "#3a3a2b", "#3a2b2b", "#2b2b3a" };
        for (int i = 0; i < map.Areas.Count; i++)
        {
            var a = map.Areas[i]; var r = a.Bounds;
            floors.AddChild(new ColorRect { Color = Color.FromHtml(tints[i % tints.Length]), Position = r.Position, Size = r.Size, MouseFilter = Control.MouseFilterEnum.Ignore });
            floors.AddChild(Tag(a.Name, r.Position + new Vector2(16, 8)));
        }

        var walls = new StaticBody2D { Name = "Walls" };
        root.AddChild(walls);
        foreach (var w in geo.Walls)
            walls.AddChild(new CollisionShape2D { Position = w.GetCenter(), Shape = new RectangleShape2D { Size = w.Size } });
        // 壁の見た目（細い暗色の帯）。
        foreach (var w in geo.Walls)
            floors.AddChild(new ColorRect { Color = new Color(0.08f, 0.08f, 0.08f), Position = w.Position, Size = w.Size, MouseFilter = Control.MouseFilterEnum.Ignore });

        var cam = new Camera2D
        {
            Name = "Camera2D",
            LimitLeft = (int)geo.Bounds.Position.X,
            LimitTop = (int)geo.Bounds.Position.Y,
            LimitRight = (int)geo.Bounds.End.X,
            LimitBottom = (int)geo.Bounds.End.Y,
        };
        player.AddChild(cam);
    }

    public static RectangleShape2D Box(float w, float h) => new() { Size = new Vector2(w, h) };

    // 据え置きの設備（寝床・作業台・パルボックス・ダンジョンゲート・住民NPC・対戦広場の入口）。
    public static void BuildFixtures(Node2D root, HubMapDef map, Action<HubFixtureDef> onBattle, Func<bool> blocked, Action<HubFixtureDef> afterUpgrade)
    {
        foreach (var f in map.Fixtures)
        {
            Node2D node;
            switch (f.Kind)
            {
                case "upgrade":
                    var fac = new HubFacility { FacilityId = f.FacilityId, DebugColor = Hex(f.Color) };
                    if (f.Save) fac.AfterAccept = () => afterUpgrade?.Invoke(f);
                    fac.AddChild(new CollisionShape2D { Shape = Box(36, 36) });
                    node = fac; break;
                case "pal_box":
                    var box = new HubPalBoxTrigger { PartySetupUIPath = "../UILayer/PartySetupUI", DebugColor = Hex(f.Color) };
                    box.AddChild(new CollisionShape2D { Shape = Box(36, 36) });
                    node = box; break;
                case "gate":
                    var gate = new HubDungeonGateTrigger { DungeonSelectUIPath = "../UILayer/DungeonSelectUI", DebugColor = Hex(f.Color) };
                    gate.AddChild(new CollisionShape2D { Shape = Box(36, 36) });
                    node = gate; break;
                case "resident":
                    node = new HubPalNpc { RequiredFacilityId = f.RequiredFacility, RequiredLevel = f.RequiredLevel };
                    break;
                case "battle":
                    var spot = new HubSpot { Hint = "Enterで対戦広場へ入る。", Blocked = blocked, OnInteract = () => onBattle?.Invoke(f) };
                    spot.AddChild(new CollisionShape2D { Shape = Box(80, 80) });
                    spot.AddChild(new Sprite2D { Texture = SpriteTextureLibrary.GetTexture("", Hex(f.Color), 48), Centered = true });
                    node = spot; break;
                default:
                    GD.PushWarning($"[Hub] 不明な fixture kind '{f.Kind}'");
                    continue;
            }
            node.Name = f.Id; node.Position = f.Pos;
            root.AddChild(node);
            if (f.Name != "") node.AddChild(Tag(f.Name, new Vector2(-60, -52)));
        }
    }

    // 広場の施設1つ。建物（通れない）＋範囲に入って Enter で開く地点＋立ち位置のNPC。
    public static Node2D BuildPlazaFacility(HubFacilityDef f, Action<HubFacilityDef> onUse, Func<bool> blocked)
    {
        var root = new Node2D { Name = "Plaza_" + f.Id, Position = f.Pos };
        var body = new StaticBody2D { Name = "Building" };
        body.AddChild(new CollisionShape2D { Shape = Box(140, 70) });
        body.AddChild(new ColorRect { Color = Hex(f.Color), Position = new Vector2(-70, -35), Size = new Vector2(140, 70), MouseFilter = Control.MouseFilterEnum.Ignore });
        root.AddChild(body);
        root.AddChild(Tag(f.Name, new Vector2(-70, -62)));

        var spot = new HubSpot { Name = "Spot", Hint = $"Enterで{f.Name}へ。", Blocked = blocked, OnInteract = () => onUse?.Invoke(f) };
        spot.AddChild(new CollisionShape2D { Shape = Box(240, 200), Position = new Vector2(0, 40) });
        root.AddChild(spot);

        foreach (var n in f.Npc)
        {
            var npc = new Node2D { Name = "Npc_" + n.Id, Position = new Vector2(n.Offset[0], n.Offset[1]) };
            var tex = SpriteTextureLibrary.GetTexture("", Colors.Gold, 24);
            npc.AddChild(new Sprite2D { Texture = tex, Centered = true, Offset = new Vector2(0, -tex.GetHeight() / 2f) });
            root.AddChild(npc);
        }
        return root;
    }
}
