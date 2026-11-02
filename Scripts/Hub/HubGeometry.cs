using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace MysteryDungeon.Hub;

// 十字型4画面の壁の組み立て（純粋な幾何。ノードは作らない）。
// 各画面の辺を集め、2つの画面が共有する辺は1本にまとめる。通路（passages）で結ばれた2画面の共有辺は、
// 中央に通路の幅だけ隙間をあける。それ以外の辺はすべて壁。壁は辺の線をはさんで厚み Thickness。
public class HubGeometry
{
    public readonly List<Rect2> Walls = new();
    public Rect2 Bounds;
    private readonly HubMapDef _map;

    public HubGeometry(HubMapDef map)
    {
        _map = map;
        float t = map.WallThickness;
        Bounds = map.Areas.Select(a => a.Bounds).Aggregate((a, b) => a.Merge(b));

        // key: (horizontal?, 固定座標, 始点, 終点) → この辺を持つ画面
        var edges = new Dictionary<(bool H, float C, float A, float B), List<string>>();
        void Add(bool h, float c, float a, float b, string id)
        {
            var k = (h, c, a, b);
            if (!edges.TryGetValue(k, out var l)) edges[k] = l = new List<string>();
            l.Add(id);
        }
        foreach (var a in map.Areas)
        {
            var r = a.Bounds;
            Add(true, r.Position.Y, r.Position.X, r.End.X, a.Id);
            Add(true, r.End.Y, r.Position.X, r.End.X, a.Id);
            Add(false, r.Position.X, r.Position.Y, r.End.Y, a.Id);
            Add(false, r.End.X, r.Position.Y, r.End.Y, a.Id);
        }
        foreach (var (k, owners) in edges)
        {
            float gap = 0;
            foreach (var p in map.Passages)
                if (owners.Contains(p.A) && owners.Contains(p.B)) gap = p.Width;
            float mid = (k.A + k.B) / 2f;
            var spans = gap > 0
                ? new[] { (k.A - t / 2, mid - gap / 2), (mid + gap / 2, k.B + t / 2) }
                : new[] { (k.A - t / 2, k.B + t / 2) };
            foreach (var (s, e) in spans)
                Walls.Add(k.H ? new Rect2(s, k.C - t / 2, e - s, t) : new Rect2(k.C - t / 2, s, t, e - s));
        }
    }

    public string AreaAt(Vector2 p) => _map.Areas.FirstOrDefault(a => a.Bounds.HasPoint(p))?.Id ?? "";

    public bool Blocked(Vector2 center, Vector2 half)
    {
        var box = new Rect2(center - half, half * 2);
        return Walls.Any(w => w.Intersects(box));
    }

    // 軸ごとに1pxずつ進める簡易移動（壁に当たったらその軸は止まる）。ノードの物理と同じ矩形で判定する。
    public Vector2 Slide(Vector2 pos, Vector2 delta, Vector2 half)
    {
        var p = pos;
        for (int axis = 0; axis < 2; axis++)
        {
            float d = axis == 0 ? delta.X : delta.Y;
            int steps = (int)Math.Ceiling(Math.Abs(d));
            for (int i = 0; i < steps; i++)
            {
                var q = p; float s = Math.Sign(d);
                if (axis == 0) q.X += s; else q.Y += s;
                if (Blocked(q, half)) break;
                p = q;
            }
        }
        return p;
    }
}
