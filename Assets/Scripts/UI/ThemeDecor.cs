using UnityEngine;
using UnityEngine.UIElements;

// Code-drawn theme decorations shared by every screen (no image assets): flat fills and outlines only.

// Snow piled on the top edge of a panel or sign, with uneven lobes hanging down.
[UxmlElement]
public sealed partial class SnowCap : VisualElement
{
    private static readonly Color Snow = Color.white;
    private static readonly Color Outline = new Color32(127, 181, 217, 255);
    private static readonly float[] LobeWidths = { 34, 22, 40, 26, 30, 20, 38 };
    private static readonly float[] LobeDepths = { 12, 20, 9, 16, 22, 8, 14 };

    public SnowCap()
    {
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    private void Draw(MeshGenerationContext context)
    {
        float w = contentRect.width, h = contentRect.height;
        if (w < 40 || h < 20)
            return;
        const float inset = 3;
        float baseY = h * 0.48f;
        var painter = context.painter2D;
        painter.BeginPath();
        painter.MoveTo(new Vector2(inset, baseY));
        // Lumpy top.
        painter.BezierCurveTo(new Vector2(-2, 6), new Vector2(w * 0.2f, -2), new Vector2(w * 0.4f, 5));
        painter.BezierCurveTo(new Vector2(w * 0.55f, 10), new Vector2(w * 0.7f, -3), new Vector2(w * 0.85f, 4));
        painter.BezierCurveTo(new Vector2(w * 0.95f, 8), new Vector2(w + 2, 10), new Vector2(w - inset, baseY));
        // Hanging lobes, right to left.
        float x = w - inset;
        for (int i = 0; x > inset; i++)
        {
            float next = x - LobeWidths[i % LobeWidths.Length];
            if (next - inset < 14)
                next = inset;
            float depth = Mathf.Min(LobeDepths[i % LobeDepths.Length], h - baseY - inset);
            painter.BezierCurveTo(new Vector2(x, baseY + depth * 1.33f), new Vector2(next, baseY + depth * 1.33f),
                new Vector2(next, baseY));
            x = next;
        }
        painter.ClosePath();
        painter.fillColor = Snow;
        painter.Fill();
        painter.strokeColor = Outline;
        painter.lineWidth = 2.5f;
        painter.lineJoin = LineJoin.Round;
        painter.Stroke();
    }
}

// Small penguin face wearing a beanie in the player's color; marks who wrote a message.
public sealed class PenguinBadge : VisualElement
{
    private static readonly Color Body = new Color32(43, 52, 66, 255);
    private static readonly Color Beak = new Color32(245, 154, 35, 255);
    private static readonly Color HatOutline = new Color32(29, 43, 58, 255);
    private readonly Color hat;

    public PenguinBadge(Color hatColor)
    {
        hat = hatColor;
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    private void Draw(MeshGenerationContext context)
    {
        // Drawn on a 32-unit grid, scaled to the element.
        float s = Mathf.Min(contentRect.width, contentRect.height) / 32f;
        if (s <= 0)
            return;
        var painter = context.painter2D;
        Vector2 P(float x, float y) => new Vector2(x * s, y * s);
        void Disc(float x, float y, float radius, Color color)
        {
            painter.BeginPath();
            painter.Arc(P(x, y), radius * s, Angle.Degrees(0), Angle.Degrees(360));
            painter.ClosePath();
            painter.fillColor = color;
            painter.Fill();
        }

        Disc(16, 19, 12, Body);
        Disc(16, 22.5f, 8, Color.white);
        Disc(12.6f, 19, 1.7f, Body);
        Disc(19.4f, 19, 1.7f, Body);
        painter.BeginPath();
        painter.MoveTo(P(13.2f, 22));
        painter.LineTo(P(18.8f, 22));
        painter.LineTo(P(16, 25.6f));
        painter.ClosePath();
        painter.fillColor = Beak;
        painter.Fill();

        // Beanie: dome, brim, pompom.
        painter.strokeColor = HatOutline;
        painter.lineWidth = 1.4f * s;
        painter.lineJoin = LineJoin.Round;
        painter.BeginPath();
        painter.Arc(P(16, 14.5f), 11 * s, Angle.Degrees(180), Angle.Degrees(360));
        painter.ClosePath();
        painter.fillColor = hat;
        painter.Fill();
        painter.Stroke();
        painter.BeginPath();
        painter.MoveTo(P(4, 12.6f));
        painter.LineTo(P(28, 12.6f));
        painter.LineTo(P(28, 16.4f));
        painter.LineTo(P(4, 16.4f));
        painter.ClosePath();
        painter.Fill();
        painter.Stroke();
        painter.BeginPath();
        painter.Arc(P(16, 3.4f), 2.8f * s, Angle.Degrees(0), Angle.Degrees(360));
        painter.ClosePath();
        painter.Fill();
        painter.Stroke();
    }
}

// Full-screen menu backdrop: snowfall over two rows of snow hills. The sky is the USS background.
[UxmlElement]
public sealed partial class SnowBackdrop : VisualElement
{
    private static readonly Color FarHills = new Color32(169, 214, 242, 255);
    private static readonly Color NearHills = new Color32(240, 249, 254, 255);
    private static readonly Color Flake = new Color32(255, 255, 255, 230);

    public SnowBackdrop()
    {
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    private void Draw(MeshGenerationContext context)
    {
        float w = contentRect.width, h = contentRect.height;
        if (w < 1 || h < 1)
            return;
        var painter = context.painter2D;

        // Fixed seed: the same snowfall every time, so nothing shifts between screens.
        var random = new System.Random(7);
        int flakes = Mathf.Clamp(Mathf.RoundToInt(w * h / 22000f), 20, 120);
        painter.fillColor = Flake;
        for (int i = 0; i < flakes; i++)
        {
            var center = new Vector2((float)random.NextDouble() * w, (float)random.NextDouble() * h * 0.8f);
            painter.BeginPath();
            painter.Arc(center, 1.5f + (float)random.NextDouble() * 2.5f, Angle.Degrees(0), Angle.Degrees(360));
            painter.ClosePath();
            painter.Fill();
        }

        // Each row is overlapping round mounds over a filled band.
        Mound(painter, w * 0.16f, w * 0.30f, h * 0.57f, h, FarHills);
        Mound(painter, w * 0.52f, w * 0.34f, h * 0.61f, h, FarHills);
        Mound(painter, w * 0.86f, w * 0.30f, h * 0.56f, h, FarHills);
        Band(painter, w, h, h * 0.68f, FarHills);
        Mound(painter, w * 0.26f, w * 0.42f, h * 0.73f, h, NearHills);
        Mound(painter, w * 0.78f, w * 0.44f, h * 0.76f, h, NearHills);
        Band(painter, w, h, h * 0.84f, NearHills);
    }

    // Parabolic mound: peak at (centerX, top), reaching the bottom of the screen halfWidth to each side.
    // Sampled as line segments; large arcs tessellate too coarsely to look round.
    private static void Mound(Painter2D painter, float centerX, float halfWidth, float top, float bottom, Color color)
    {
        const int Segments = 64;
        painter.BeginPath();
        painter.MoveTo(new Vector2(centerX - halfWidth, bottom));
        for (int i = 1; i < Segments; i++)
        {
            float t = i * 2f / Segments - 1f;
            painter.LineTo(new Vector2(centerX + halfWidth * t, bottom - (bottom - top) * (1f - t * t)));
        }
        painter.LineTo(new Vector2(centerX + halfWidth, bottom));
        painter.ClosePath();
        painter.fillColor = color;
        painter.Fill();
    }

    private static void Band(Painter2D painter, float w, float h, float top, Color color)
    {
        painter.BeginPath();
        painter.MoveTo(new Vector2(0, top));
        painter.LineTo(new Vector2(w, top));
        painter.LineTo(new Vector2(w, h));
        painter.LineTo(new Vector2(0, h));
        painter.ClosePath();
        painter.fillColor = color;
        painter.Fill();
    }
}
