using System;
using UnityEditor;
using UnityEngine;

public static class GameLayoutChecks
{
    private const float Tolerance = 0.002f;

    public static void Run()
    {
        var layout = UnityEngine.Object.FindAnyObjectByType<GameLayout>();
        Require(layout != null, "GameLayout exists in the open scene");
        Require(!Application.isPlaying, "Run layout checks outside Play mode");
        Vector2[] boundary = DenseBoundary();
        var serialized = new SerializedObject(layout);
        var countProperty = serialized.FindProperty("playerCount");
        int original = countProperty.intValue;
        try
        {
            for (int count = 4; count <= 8; count++)
            {
                serialized.Update();
                countProperty.intValue = count;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                layout.Rebuild();
                var root = layout.transform.Find("Layout");
                Require(layout.transform.childCount == 1, "No stale generated layouts");
                Require(root.childCount == count * 2, "One igloo and one character per player");
                var iglooSpacings = new float[count];

                for (int i = 1; i <= count; i++)
                {
                    var character = root.Find("Character " + i);
                    var igloo = root.Find("Igloo " + i);
                    var label = character.GetComponentInChildren<TextMesh>();
                    Require(label.text == "Player " + i, "Automatic player name");
                    Require(label.transform.position.y > character.position.y + GameLayout.CharacterHeight,
                        "Name above character head");

                    // Measure the generated sprites in reference units, independent of the placement math.
                    // Feet: the penguin sprite's pivot (bottom-center of its visible art).
                    Vector2 foot = root.InverseTransformPoint(character.position);
                    Require(Inside(foot), "Feet stay inside the ice outline");
                    Require(character.GetComponent<SpriteRenderer>().flipX == foot.x > GameLayout.LayoutCenter.x + 0.01f,
                        $"Character {i}/{count} faces the middle");
                    var outfit = character.Find("Outfit").GetComponent<SpriteRenderer>();
                    var body = character.GetComponent<SpriteRenderer>();
                    Require(outfit.transform.localPosition == Vector3.zero && outfit.transform.localScale == Vector3.one
                        && outfit.flipX == body.flipX && outfit.sortingOrder > body.sortingOrder
                        && outfit.sortingOrder < label.GetComponent<MeshRenderer>().sortingOrder,
                        $"Outfit {i}/{count} sits exactly on its penguin, between body and name");
                    Require(outfit.color == GameLayout.PlayerColor((LobbyColor)(i - 1)),
                        $"Outfit {i}/{count} uses its player's color");
                    float footGap = Nearest(foot, boundary);
                    Require(Mathf.Abs(footGap - layout.CharacterGap) < Tolerance,
                        $"Character {i}/{count} foot gap {footGap:F4} equals {layout.CharacterGap:F4}");
                    Require(body.sprite.bounds == outfit.sprite.bounds && body.sprite.pivot == outfit.sprite.pivot,
                        "Penguin and outfit art share size and pivot");


                    // Igloo silhouette, measured through the generated transform.
                    Vector2[] iglooShape = Silhouette(root, igloo);
                    float iglooGap = float.PositiveInfinity;
                    foreach (Vector2 point in iglooShape)
                    {
                        Require(!Inside(point), $"Igloo {i}/{count} stays entirely outside the ice");
                        iglooGap = Mathf.Min(iglooGap, Nearest(point, boundary));
                    }
                    Require(Mathf.Abs(iglooGap - layout.IglooGap) < Tolerance,
                        $"Igloo {i}/{count} nearest gap {iglooGap:F4} equals {layout.IglooGap:F4}");
                    Vector2 iglooMin = iglooShape[0], iglooMax = iglooShape[0];
                    foreach (Vector2 point in iglooShape)
                    {
                        iglooMin = Vector2.Min(iglooMin, point);
                        iglooMax = Vector2.Max(iglooMax, point);
                    }
                    Vector2 footDirection = foot - GameLayout.LayoutCenter;
                    float slot = Mathf.Atan2(footDirection.x, footDirection.y) * Mathf.Rad2Deg;
                    Require(AngleError((iglooMin + iglooMax) / 2, slot) < 0.05f,
                        $"Igloo {i}/{count} sits on its character's ray");
                    var window = igloo.GetComponentInChildren<UnityEngine.Rendering.Universal.Light2D>(true);
                    Require(window != null && window.lightType == UnityEngine.Rendering.Universal.Light2D.LightType.Point,
                        $"Igloo {i}/{count} has a window light");

                    CheckHoleClearance(foot + Vector2.up * GameLayout.CharacterHeight / 2f);
                    var next = root.Find("Character " + (i % count + 1));
                    Require(Vector2.Distance(character.localPosition, next.localPosition) > 1f,
                        "Characters remain separated");
                    var nextIgloo = root.Find("Igloo " + (i % count + 1));
                    Require(Separated(iglooShape, Silhouette(root, nextIgloo), 0.05f),
                        $"Igloos {i} and {i % count + 1} of {count} do not overlap");
                    iglooSpacings[i - 1] = DenseDistance(iglooShape, Silhouette(root, nextIgloo));
                }
                float spacingMin = Mathf.Min(iglooSpacings), spacingMax = Mathf.Max(iglooSpacings);
                Require(spacingMax - spacingMin < 0.01f,
                    $"Edge-to-edge igloo gaps are equal for {count} players ({spacingMin:F3} to {spacingMax:F3})");
            }
        }
        finally
        {
            serialized.Update();
            countProperty.intValue = original;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            layout.Rebuild();
        }
        CheckDayNightCycle();
        Debug.Log("Game layout checks passed for every player count from 4 to 8.");
    }

    private static void CheckDayNightCycle()
    {
        DayNightCycle.Evaluate(0f, out Color midnight, out float midnightIntensity);
        DayNightCycle.Evaluate(DayNightCycle.Hours, out Color wrapped, out float wrappedIntensity);
        DayNightCycle.Evaluate(DayNightCycle.Noon, out _, out float noonIntensity);
        Require(midnight == wrapped && Mathf.Approximately(midnightIntensity, wrappedIntensity),
            "Day-night cycle wraps smoothly at midnight");
        Require(Mathf.Approximately(DayNightCycle.Darkness(noonIntensity), 0f)
            && Mathf.Approximately(DayNightCycle.Darkness(midnightIntensity), 1f),
            "Noon is full daylight and midnight is full night");
        float previous = midnightIntensity;
        for (int step = 1; step <= 480; step++)
        {
            DayNightCycle.Evaluate(step * DayNightCycle.Hours / 480f, out _, out float intensity);
            Require(Mathf.Abs(intensity - previous) < 0.02f, "Day-night light changes without jumps");
            previous = intensity;
        }
    }

    private static Vector2[] DenseBoundary()
    {
        var points = new Vector2[16384];
        for (int i = 0; i < points.Length; i++)
        {
            float angle = i * Mathf.PI * 2 / points.Length;
            points[i] = GameLayout.LayoutCenter + new Vector2(3.6f * Mathf.Cos(angle), 2f * Mathf.Sin(angle));
        }
        return points;
    }

    private static Vector2[] Silhouette(Transform root, Transform igloo)
    {
        Vector2[] outline = GameLayout.IglooOutline(igloo.GetComponent<SpriteRenderer>().sprite);
        for (int i = 0; i < outline.Length; i++)
            outline[i] = root.InverseTransformPoint(igloo.TransformPoint(outline[i]));
        return outline;
    }

    // Separating-axis test for two convex polygons, with a required clearance.
    private static bool Separated(Vector2[] a, Vector2[] b, float margin)
    {
        foreach (Vector2[] polygon in new[] { a, b })
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 edge = polygon[(i + 1) % polygon.Length] - polygon[i];
                Vector2 axis = new Vector2(-edge.y, edge.x).normalized;
                float aMin = float.PositiveInfinity, aMax = float.NegativeInfinity;
                float bMin = float.PositiveInfinity, bMax = float.NegativeInfinity;
                foreach (Vector2 point in a)
                {
                    aMin = Mathf.Min(aMin, Vector2.Dot(point, axis));
                    aMax = Mathf.Max(aMax, Vector2.Dot(point, axis));
                }
                foreach (Vector2 point in b)
                {
                    bMin = Mathf.Min(bMin, Vector2.Dot(point, axis));
                    bMax = Mathf.Max(bMax, Vector2.Dot(point, axis));
                }
                if (aMax + margin < bMin || bMax + margin < aMin)
                    return true;
            }
        return false;
    }

    // Degrees between a point's direction from the layout center and the expected clockwise-from-top angle.
    private static float AngleError(Vector2 point, float expected)
    {
        Vector2 relative = point - GameLayout.LayoutCenter;
        float actual = Mathf.Atan2(relative.x, relative.y) * Mathf.Rad2Deg;
        return Mathf.Abs(Mathf.DeltaAngle(actual, expected));
    }

    // Brute-force shortest distance between two polygon outlines, sampled densely along every edge;
    // independent of the Minkowski-sum shortcut used for placement.
    private static float DenseDistance(Vector2[] a, Vector2[] b)
    {
        var samplesA = DenseOutline(a);
        var samplesB = DenseOutline(b);
        float nearest = float.PositiveInfinity;
        foreach (Vector2 p in samplesA)
            foreach (Vector2 q in samplesB)
                nearest = Mathf.Min(nearest, (p - q).sqrMagnitude);
        return Mathf.Sqrt(nearest);
    }

    private static System.Collections.Generic.List<Vector2> DenseOutline(Vector2[] polygon)
    {
        var points = new System.Collections.Generic.List<Vector2>();
        for (int i = 0; i < polygon.Length; i++)
            for (int step = 0; step < 12; step++)
                points.Add(Vector2.Lerp(polygon[i], polygon[(i + 1) % polygon.Length], step / 12f));
        return points;
    }

    private static bool Inside(Vector2 point)
    {
        Vector2 relative = point - GameLayout.LayoutCenter;
        return relative.x * relative.x / (3.6f * 3.6f) + relative.y * relative.y / 4f < 1;
    }

    private static float Nearest(Vector2 point, Vector2[] boundary)
    {
        // Coarse scan, then a full-resolution scan around the best coarse sample.
        int best = 0;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < boundary.Length; i += 16)
        {
            float distance = (boundary[i] - point).sqrMagnitude;
            if (distance < nearest)
            {
                nearest = distance;
                best = i;
            }
        }
        for (int offset = -64; offset <= 64; offset++)
        {
            int i = (best + offset + boundary.Length) % boundary.Length;
            nearest = Mathf.Min(nearest, (boundary[i] - point).sqrMagnitude);
        }
        return Mathf.Sqrt(nearest);
    }

    private static void CheckHoleClearance(Vector2 character)
    {
        Vector2 fromHole = character - GameLayout.LayoutCenter;
        float closestX = Mathf.Max(Mathf.Abs(fromHole.x) - GameLayout.CharacterSize.x / 2f, 0);
        float closestY = Mathf.Max(Mathf.Abs(fromHole.y) - GameLayout.CharacterHeight / 2f, 0);
        Require(closestX * closestX / (1.1f * 1.1f) + closestY * closestY / (0.85f * 0.85f) > 1f,
            "Character body stays outside the clear area around the hole");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
