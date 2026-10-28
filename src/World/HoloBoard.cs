using System;
using System.Collections.Generic;
using Godot;

namespace VeloGenesis.World;

/// <summary>
/// In-world signage: a dark glass panel with a glowing chamfered frame, scanlines and a slow light
/// sweep, with glowing text laid out on it. Faces local +Z.
/// </summary>
public static class HoloBoard
{
    public const float PixelSize = 0.01f;
    const float Pad = 0.45f;

    public record struct Line(string Text, int FontSize, Color Color, float Gap = 0.12f);

    /// <summary>Builds a board whose size fits the given lines (top to bottom), anchored at its bottom centre.</summary>
    public static Node3D Build(Color accent, IReadOnlyList<Line> lines, HorizontalAlignment align = HorizontalAlignment.Left, float minWidth = 0)
    {
        var font = UI.UiTheme.Font3D;
        float width = minWidth, height = Pad * 2;
        foreach (var l in lines)
        {
            width = Math.Max(width, font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, l.FontSize).X * PixelSize + Pad * 2);
            height += l.FontSize * PixelSize + l.Gap;
        }
        height -= lines.Count > 0 ? lines[^1].Gap : 0;

        var root = new Node3D();
        var mat = new ShaderMaterial { Shader = Shaders.HoloPanel };
        mat.SetShaderParameter("accent", accent);
        mat.SetShaderParameter("size", new Vector2(width, height));
        root.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(width, height) },
            MaterialOverride = mat,
            Position = new Vector3(0, height * 0.5f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        float y = height - Pad;
        foreach (var l in lines)
        {
            float h = l.FontSize * PixelSize;
            float x = align switch { HorizontalAlignment.Center => 0, HorizontalAlignment.Right => width * 0.5f - Pad, _ => -width * 0.5f + Pad };
            root.AddChild(new Label3D
            {
                Text = l.Text,
                Font = font,
                FontSize = l.FontSize,
                PixelSize = PixelSize,
                Modulate = l.Color,
                OutlineSize = 0,
                HorizontalAlignment = align,
                VerticalAlignment = VerticalAlignment.Top,
                Position = new Vector3(x, y, 0.03f),
                DoubleSided = false,
                RenderPriority = 1,
            });
            y -= h + l.Gap;
        }
        return root;
    }

    /// <summary>Board size from the panel mesh (for placing it on posts or under beams).</summary>
    public static Vector2 SizeOf(Node3D board) => ((QuadMesh)((MeshInstance3D)board.GetChild(0)).Mesh).Size;

    /// <summary>Bright text colour for glowing (bloomed) signage.</summary>
    public static Color Glow(Color c, float energy) => new(c.R * energy, c.G * energy, c.B * energy, c.A);
}
