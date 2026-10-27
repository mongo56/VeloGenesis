using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VeloGenesis.Ride;
using VeloGenesis.World;

namespace VeloGenesis.UI;

/// <summary>Elevation profile of the road ahead, coloured by gradient.</summary>
public partial class ProfileGraph : Control
{
    public List<Vector2> Points = new();
    public float ForkAt = -1;
    public float Span = 2000f;

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.02f, 0.01f, 0.07f, 0.6f));
        if (Points.Count < 2) return;
        float minE = float.MaxValue, maxE = float.MinValue;
        foreach (var p in Points) { minE = Math.Min(minE, p.Y); maxE = Math.Max(maxE, p.Y); }
        float range = Math.Max(maxE - minE, 30f);
        float mid = (maxE + minE) * 0.5f;
        minE = mid - range * 0.6f;
        maxE = mid + range * 0.6f;

        Vector2 Map(Vector2 p) => new(p.X / Span * size.X, size.Y - (p.Y - minE) / (maxE - minE) * size.Y);

        for (int i = 0; i < Points.Count - 1; i++)
        {
            var a = Points[i];
            var b = Points[i + 1];
            float grade = (b.Y - a.Y) / Math.Max(b.X - a.X, 0.01f) * 100f;
            var col = GradeColor(grade);
            var pa = Map(a);
            var pb = Map(b);
            DrawPolygon(new[] { pa, pb, new Vector2(pb.X, size.Y), new Vector2(pa.X, size.Y) }, new[] { new Color(col, 0.35f), new Color(col, 0.35f), new Color(col, 0.05f), new Color(col, 0.05f) });
            DrawLine(pa, pb, col, 2.5f, true);
        }

        var font = UiTheme.Font;
        for (float d = 500; d < Span; d += 500)
        {
            float x = d / Span * size.X;
            DrawLine(new Vector2(x, 0), new Vector2(x, size.Y), new Color(1, 1, 1, 0.08f), 1);
            DrawString(font, new Vector2(x + 4, size.Y - 6), $"{d / 1000f:0.0}", HorizontalAlignment.Left, -1, 15, new Color(UiTheme.Dim, 0.8f));
        }
        if (ForkAt >= 0 && ForkAt < Span)
        {
            float x = ForkAt / Span * size.X;
            DrawLine(new Vector2(x, 0), new Vector2(x, size.Y), UiTheme.Magenta, 2);
            DrawString(font, new Vector2(x + 5, 18), "FORK", HorizontalAlignment.Left, -1, 16, UiTheme.Magenta);
        }
        var rider = Map(Points[0]);
        DrawCircle(rider, 6, UiTheme.Text);
        DrawString(font, new Vector2(size.X - 150, 20), $"{maxE - minE:0} m range", HorizontalAlignment.Left, -1, 14, new Color(UiTheme.Dim, 0.6f));
    }

    public static Color GradeColor(float g) => g switch
    {
        < -2f => new Color(0.2f, 0.7f, 1f),
        < 2f => new Color(0.4f, 1f, 0.5f),
        < 5f => new Color(1f, 0.85f, 0.2f),
        < 8f => new Color(1f, 0.5f, 0.15f),
        _ => new Color(1f, 0.2f, 0.35f),
    };
}

/// <summary>Bar chart of a workout with a progress cursor.</summary>
public partial class WorkoutGraph : Control
{
    public Workout Workout;
    public float Elapsed = -1;
    public int Ftp = 220;

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.02f, 0.01f, 0.07f, 0.6f));
        if (Workout == null || Workout.Intervals.Count == 0) return;
        float total = Workout.TotalDuration;
        const float maxPct = 1.4f;
        float t = 0;
        foreach (var iv in Workout.Intervals)
        {
            float x0 = t / total * size.X;
            float x1 = (t + iv.Duration) / total * size.X;
            float y0 = size.Y - Math.Min(iv.StartPct / maxPct, 1f) * size.Y;
            float y1 = size.Y - Math.Min(iv.EndPct / maxPct, 1f) * size.Y;
            var col = UiTheme.ZoneColor((iv.StartPct + iv.EndPct) * 0.5f);
            bool done = Elapsed >= t + iv.Duration;
            var fill = new Color(col, done ? 0.25f : 0.65f);
            DrawPolygon(new[] { new Vector2(x0, y0), new Vector2(x1, y1), new Vector2(x1, size.Y), new Vector2(x0, size.Y) }, new[] { fill, fill, fill, fill });
            DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), col, 2, true);
            t += iv.Duration;
        }
        float ftpY = size.Y - 1f / maxPct * size.Y;
        DrawDashedLine(new Vector2(0, ftpY), new Vector2(size.X, ftpY), new Color(1, 1, 1, 0.35f), 1, 6);
        DrawString(UiTheme.Font, new Vector2(4, ftpY - 4), $"FTP {Ftp} W", HorizontalAlignment.Left, -1, 14, new Color(1, 1, 1, 0.6f));
        if (Elapsed >= 0)
        {
            float x = Math.Min(Elapsed / total, 1f) * size.X;
            DrawLine(new Vector2(x, 0), new Vector2(x, size.Y), UiTheme.Text, 2);
        }
    }
}

/// <summary>Seven coloured segments with the current power zone lit up.</summary>
public partial class ZoneBar : Control
{
    public int Zone = 1;

    public override void _Draw()
    {
        var size = Size;
        const float gap = 3f;
        float w = (size.X - gap * 6) / 7f;
        for (int i = 0; i < 7; i++)
        {
            bool cur = i + 1 == Zone;
            var col = PowerZones.Colors[i];
            float h = cur ? size.Y : size.Y * 0.55f;
            var rect = new Rect2(i * (w + gap), size.Y - h, w, h);
            DrawRect(rect, cur ? col : new Color(col, 0.28f));
            if (cur) DrawRect(rect, new Color(1, 1, 1, 0.8f), false, 1.5f);
        }
    }
}

/// <summary>Horizontal bars of time spent in each power zone.</summary>
public partial class ZoneChart : Control
{
    public int[] Seconds = new int[7];

    public override void _Draw()
    {
        var size = Size;
        int total = Math.Max(1, Seconds.Sum());
        int max = Math.Max(1, Seconds.Max());
        float rowH = size.Y / 7f;
        var font = UiTheme.Font;
        for (int i = 0; i < 7; i++)
        {
            float y = i * rowH;
            var col = PowerZones.Colors[i];
            DrawString(font, new Vector2(0, y + rowH * 0.75f), $"Z{i + 1}  {PowerZones.Names[i]}", HorizontalAlignment.Left, -1, 17, col);
            float x0 = 230f, avail = size.X - x0 - 130f;
            DrawRect(new Rect2(x0, y + rowH * 0.2f, avail, rowH * 0.6f), new Color(1, 1, 1, 0.06f));
            DrawRect(new Rect2(x0, y + rowH * 0.2f, avail * Seconds[i] / max, rowH * 0.6f), new Color(col, 0.85f));
            DrawString(font, new Vector2(x0 + avail + 10, y + rowH * 0.75f), $"{UiTheme.Time(Seconds[i])}  {Seconds[i] * 100 / total}%", HorizontalAlignment.Left, -1, 17, UiTheme.Text);
        }
    }
}

/// <summary>Heading-up map of where you've been and the branches ahead.</summary>
public partial class Minimap : Control
{
    public WorldRoot World;
    public float Radius = 900f;
    public Vector3? Ghost;

    public override void _Draw()
    {
        var size = Size;
        var c = size / 2;
        float r = Math.Min(size.X, size.Y) / 2f;
        DrawCircle(c, r, new Color(0.02f, 0.01f, 0.07f, 0.65f));
        DrawArc(c, r - 1, 0, Mathf.Tau, 64, new Color(UiTheme.Cyan, 0.5f), 2, true);
        if (World?.Current == null) return;

        var me = new Vector2(World.RiderPosition.X, World.RiderPosition.Z);
        float h = World.RiderHeading;
        float scale = r / Radius;
        Vector2 Map(Vector2 p)
        {
            var d = (p - me) * scale;
            // Rotate so the rider's heading points up the screen.
            float cs = MathF.Cos(-h), sn = MathF.Sin(-h);
            return c + new Vector2(d.X * cs - d.Y * sn, d.X * sn + d.Y * cs);
        }
        void Poly(List<Vector2> pts, Color col, float w)
        {
            if (pts.Count < 2) return;
            var mapped = new Vector2[pts.Count];
            for (int i = 0; i < pts.Count; i++) mapped[i] = Map(pts[i]);
            for (int i = 0; i < mapped.Length - 1; i++)
                if ((mapped[i] - c).Length() < r - 2 && (mapped[i + 1] - c).Length() < r - 2)
                    DrawLine(mapped[i], mapped[i + 1], col, w, true);
        }

        Poly(World.Breadcrumbs, new Color(UiTheme.Magenta, 0.8f), 3);
        var cur = World.Current;
        Poly(WorldRoot.Outline(cur, World.DistInSeg, cur.Length, 20f), new Color(cur.Primary, 0.9f), 3);
        if (cur.Children != null)
            for (int i = 0; i < 2; i++)
            {
                var k = cur.Children[i];
                bool sel = i == World.Selection;
                Poly(WorldRoot.Outline(k, 0, 1500, 25f), new Color(k.Primary, sel ? 0.95f : 0.3f), sel ? 3 : 2);
            }
        if (Ghost is Vector3 gp) DrawCircle(Map(new Vector2(gp.X, gp.Z)), 5, new Color(0.55f, 0.95f, 1f));
        DrawColoredPolygon(new[] { c + new Vector2(0, -9), c + new Vector2(6, 7), c + new Vector2(-6, 7) }, UiTheme.Text);
    }
}
