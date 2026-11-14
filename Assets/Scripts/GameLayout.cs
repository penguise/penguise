using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

[ExecuteAlways]
public sealed class GameLayout : MonoBehaviour
{
    [SerializeField, Range(4, 8)] private int playerCount = 8;
    [SerializeField, HideInInspector] private Sprite igloo;
    [FormerlySerializedAs("capsule"), SerializeField, HideInInspector] private Sprite character;
    // White outfit art drawn over the penguin (same canvas and pivot), tinted per player color.
    [SerializeField, HideInInspector] private Sprite outfit;
    [SerializeField, HideInInspector] private Material spriteMaterial;
    [SerializeField, HideInInspector] private Font nameFont;
    private Transform generated;
    [SerializeField, HideInInspector] private Camera layoutCamera;
    [SerializeField, HideInInspector] private SpriteRenderer background;

    // Reference-image units (100 pixels/unit), mapped to the actual background sprite below.
    public static readonly Vector2 LayoutCenter = new Vector2(2f, -0.20f);
    public static readonly Vector2 IceRadii = new Vector2(3.6f, 2f);
    // Outfit tints, indexed by LobbyColor; keep in sync with the lobby swatches in Lobby.uss.
    private static readonly Color32[] PlayerColors =
    {
        new Color32(0xe5, 0x39, 0x35, 0xff), // Red
        new Color32(0xfb, 0x8c, 0x00, 0xff), // Orange
        new Color32(0xfd, 0xd8, 0x35, 0xff), // Yellow
        new Color32(0x43, 0xa0, 0x47, 0xff), // Green
        new Color32(0x1e, 0x88, 0xe5, 0xff), // Blue
        new Color32(0x8e, 0x24, 0xaa, 0xff), // Purple
        new Color32(0xec, 0x6f, 0xa9, 0xff), // Pink
        new Color32(0x8d, 0x6e, 0x63, 0xff), // Brown
    };

    public static Color PlayerColor(LobbyColor color) => PlayerColors[(int)color % PlayerColors.Length];

    // Visible penguin art size; the Penguin.png import (1250 PPU, feet pivot) produces it unscaled.
    public static readonly Vector2 CharacterSize = new Vector2(0.52f, 0.75f);
    public const float CharacterHeight = 0.75f;
    // Character gap: character bottom-center (feet) to the ice outline.
    // Igloo gap: nearest point of the igloo art to the outline; every igloo stays outside the ice.
    [SerializeField, Range(0f, 1f)] private float characterGap = 0.3f;
    [SerializeField, Range(0f, 0.6f)] private float iglooGap = 0.3f;
    // Hours on a 24-hour clock: 0 midnight, ~6 dawn, 12 noon, ~19.5 sunset.
    [SerializeField, Range(0f, DayNightCycle.Hours)] private float timeOfDay = DayNightCycle.Noon;
    [SerializeField, HideInInspector] private Light2D globalLight;
    public float CharacterGap => characterGap;
    public float IglooGap => iglooGap;
    private int renderedCount;
    private float renderedCharacterGap = float.NaN, renderedIglooGap = float.NaN;

    // Window glow: texture fractions (y up) of the igloo art's round window, plus light shape and color.
    private static readonly Vector2 IglooWindow = new Vector2(0.265f, 0.444f);
    private const float WindowLightRadius = 0.75f;
    private const float WindowLightIntensity = 1.4f;
    private static readonly Color WindowLightColor = new Color(1f, 0.78f, 0.45f);
    private static readonly Color DayBackground = new Color(0.863f, 0.929f, 0.957f);
    private static readonly Color DayNameColor = new Color32(32, 55, 73, 255);
    private static readonly Color NightNameColor = new Color32(236, 241, 250, 255);
    private readonly List<Light2D> windowLights = new List<Light2D>();
    private readonly List<TextMesh> nameLabels = new List<TextMesh>();

    private bool transitioning;
    private float transitionFrom, transitionSpan, transitionDuration, transitionElapsed;

    public float TimeOfDay
    {
        get => timeOfDay;
        set
        {
            transitioning = false;
            timeOfDay = Mathf.Repeat(value, DayNightCycle.Hours);
            ApplyLighting();
        }
    }

    public bool IsTransitioning => transitioning;

    // Advances the clock forward (wrapping past midnight) to `hour` over `seconds` of play time.
    public void TransitionTimeOfDay(float hour, float seconds)
    {
        transitionFrom = timeOfDay;
        transitionSpan = Mathf.Repeat(hour - timeOfDay, DayNightCycle.Hours);
        transitionDuration = Mathf.Max(0f, seconds);
        transitionElapsed = 0f;
        transitioning = true;
    }

    public static Vector2 BoundaryPoint(float angle)
    {
        return LayoutCenter + new Vector2(IceRadii.x * Mathf.Sin(angle), IceRadii.y * Mathf.Cos(angle));
    }

    public static Vector2 SlotDirection(float angle) => new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));

    // Bottom-center of the character on the slot ray, with shortest distance `gap` inside the outline.
    public static Vector2 FootPosition(float angle, float gap)
    {
        Vector2 direction = SlotDirection(angle);
        float low = 0, high = Mathf.Max(IceRadii.x, IceRadii.y);
        for (int i = 0; i < 40; i++)
        {
            float middle = (low + high) / 2;
            if (OutsideDistance(LayoutCenter + direction * middle) < -gap)
                low = middle;
            else
                high = middle;
        }
        return LayoutCenter + direction * low;
    }

    // Body center for a character standing on its foot point.
    public static Vector2 CharacterPosition(float angle, float gap)
    {
        return FootPosition(angle, gap) + Vector2.up * CharacterHeight / 2f;
    }

    // Convex hull of Igloo.png's visible art (alpha >= 64) as texture fractions, y up.
    // Regenerate if the igloo art changes.
    private static readonly Vector2[] IglooHull =
    {
        new Vector2(0.0973f, 0.3385f), new Vector2(0.1148f, 0.3114f), new Vector2(0.1555f, 0.2827f),
        new Vector2(0.2041f, 0.2596f), new Vector2(0.2376f, 0.2468f), new Vector2(0.2839f, 0.2325f),
        new Vector2(0.3150f, 0.2229f), new Vector2(0.5159f, 0.1703f), new Vector2(0.8381f, 0.2317f),
        new Vector2(0.8892f, 0.2460f), new Vector2(0.9219f, 0.3258f), new Vector2(0.9107f, 0.4031f),
        new Vector2(0.8971f, 0.4725f), new Vector2(0.8900f, 0.5036f), new Vector2(0.8573f, 0.5865f),
        new Vector2(0.8190f, 0.6527f), new Vector2(0.7759f, 0.7014f), new Vector2(0.7512f, 0.7237f),
        new Vector2(0.6850f, 0.7723f), new Vector2(0.6061f, 0.8074f), new Vector2(0.5742f, 0.8178f),
        new Vector2(0.5383f, 0.8250f), new Vector2(0.5040f, 0.8266f), new Vector2(0.4705f, 0.8234f),
        new Vector2(0.4234f, 0.8154f), new Vector2(0.3852f, 0.8034f), new Vector2(0.3293f, 0.7755f),
        new Vector2(0.2464f, 0.7165f), new Vector2(0.2177f, 0.6870f), new Vector2(0.1563f, 0.5873f),
        new Vector2(0.1388f, 0.5490f), new Vector2(0.1276f, 0.5171f), new Vector2(0.1132f, 0.4526f),
        new Vector2(0.1061f, 0.4095f), new Vector2(0.0973f, 0.3449f),
    };

    // Igloo silhouette in layout units, relative to the sprite pivot.
    public static Vector2[] IglooOutline(Sprite sprite)
    {
        var outline = new Vector2[IglooHull.Length];
        for (int i = 0; i < outline.Length; i++)
            outline[i] = (Vector2.Scale(IglooHull[i], sprite.rect.size) - sprite.pivot) / sprite.pixelsPerUnit;
        return outline;
    }

    // Shortest distance to the ice outline: positive outside, negative inside.
    public static float OutsideDistance(Vector2 point)
    {
        // Newton's method on the ellipse parameter, from the point's scaled angle. Reliable for points
        // near the outline (feet and igloos); layout checks verify it against a dense scan.
        Vector2 relative = point - LayoutCenter;
        float t = Mathf.Atan2(relative.x / IceRadii.x, relative.y / IceRadii.y);
        for (int i = 0; i < 12; i++)
        {
            float sin = Mathf.Sin(t), cos = Mathf.Cos(t);
            Vector2 offset = new Vector2(IceRadii.x * sin, IceRadii.y * cos) - relative;
            Vector2 tangent = new Vector2(IceRadii.x * cos, -IceRadii.y * sin);
            Vector2 curvature = new Vector2(-IceRadii.x * sin, -IceRadii.y * cos);
            float slope = tangent.sqrMagnitude + Vector2.Dot(offset, curvature);
            t -= Mathf.Clamp(Vector2.Dot(offset, tangent) / slope, -0.3f, 0.3f);
        }
        float nearest = Vector2.Distance(BoundaryPoint(t), point);
        bool inside = relative.x * relative.x / (IceRadii.x * IceRadii.x)
            + relative.y * relative.y / (IceRadii.y * IceRadii.y) < 1;
        return inside ? -nearest : nearest;
    }

    public static float IglooClearance(Vector2 pivot, Vector2[] outline)
    {
        float clearance = float.PositiveInfinity;
        foreach (Vector2 point in outline)
            clearance = Mathf.Min(clearance, OutsideDistance(pivot + point));
        return clearance;
    }

    // Igloo pivot such that the whole igloo is outside the ice and its nearest point is exactly `gap`
    // from the line. The igloo's visual center slides outward along the slot ray.
    public static Vector2 IglooPosition(float angle, float gap, Vector2[] outline)
    {
        Vector2 min = outline[0], max = outline[0];
        foreach (Vector2 point in outline)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        Vector2 start = LayoutCenter - (min + max) / 2, normal = SlotDirection(angle);
        float low = 0, high = 12;
        for (int i = 0; i < 26; i++)
        {
            float middle = (low + high) / 2;
            if (IglooClearance(start + normal * middle, outline) < gap)
                low = middle;
            else
                high = middle;
        }
        return start + normal * high;
    }

    // Igloo pivots sampled by ray angle around the ring, cached per gap and igloo size.
    private const int RingSamples = 120;
    private static Vector2[] ringCache;
    private static float ringGap = float.NaN;
    private static Vector2 ringKey;

    private static Vector2[] IglooRing(float gap, Vector2[] outline)
    {
        Vector2 key = outline[0] + outline[outline.Length / 2] * 3.7f;
        if (ringCache != null && ringGap == gap && ringKey == key)
            return ringCache;
        var ring = new Vector2[RingSamples];
        for (int i = 0; i < RingSamples; i++)
            ring[i] = IglooPosition(2 * Mathf.PI * i / RingSamples, gap, outline);
        ringCache = ring;
        ringGap = gap;
        ringKey = key;
        return ring;
    }

    private static Vector2 RingAt(Vector2[] ring, float position)
    {
        int index = Mathf.FloorToInt(position);
        return Vector2.Lerp(ring[index % ring.Length], ring[(index + 1) % ring.Length], position - index);
    }

    // Two igloos pivoted at a and b are exactly as far apart, edge to edge, as the point (b - a) is from
    // the Minkowski sum K + (-K) of the silhouette K (https://cp-algorithms.com/geometry/minkowski.html).
    private static Vector2[] pairShapeCache;
    private static Vector2 pairShapeKey;

    private static Vector2[] PairShape(Vector2[] outline)
    {
        Vector2 key = outline[0] + outline[outline.Length / 2] * 3.7f;
        if (pairShapeCache != null && pairShapeKey == key)
            return pairShapeCache;
        var points = new System.Collections.Generic.List<Vector2>();
        foreach (Vector2 p in outline)
            foreach (Vector2 q in outline)
                points.Add(p - q);
        pairShapeCache = ConvexHull(points);
        pairShapeKey = key;
        return pairShapeCache;
    }

    // Andrew's monotone chain; counter-clockwise result.
    private static Vector2[] ConvexHull(System.Collections.Generic.List<Vector2> points)
    {
        points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        var hull = new Vector2[points.Count * 2];
        int k = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            int start = k;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 point = points[pass == 0 ? i : points.Count - 1 - i];
                while (k >= start + 2 && Cross(hull[k - 2], hull[k - 1], point) <= 0)
                    k--;
                hull[k++] = point;
            }
            k--;
        }
        System.Array.Resize(ref hull, k);
        return hull;
    }

    private static float Cross(Vector2 o, Vector2 a, Vector2 b) =>
        (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

    // Edge-to-edge gap between two igloos whose pivots differ by `offset`; 0 when they touch.
    public static float SilhouetteGap(Vector2 offset, Vector2[] outline)
    {
        Vector2[] shape = PairShape(outline);
        bool inside = true;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < shape.Length; i++)
        {
            Vector2 a = shape[i], b = shape[(i + 1) % shape.Length];
            inside &= Cross(a, b, offset) >= 0;
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(offset - a, ab) / ab.sqrMagnitude);
            nearest = Mathf.Min(nearest, Vector2.Distance(offset, a + ab * t));
        }
        return inside ? 0 : nearest;
    }

    // Walk clockwise along the ring from `position` to the first point whose igloo is `gap` away
    // from this one, edge to edge.
    private static float Step(Vector2[] ring, Vector2[] outline, float position, float gap)
    {
        Vector2 origin = RingAt(ring, position);
        int first = Mathf.FloorToInt(position);
        for (int i = first; i < first + ring.Length; i++)
        {
            if (SilhouetteGap(ring[(i + 1) % ring.Length] - origin, outline) < gap)
                continue;
            float low = Mathf.Max(i, position), high = i + 1;
            for (int iteration = 0; iteration < 20; iteration++)
            {
                float middle = (low + high) / 2;
                if (SilhouetteGap(RingAt(ring, middle) - origin, outline) < gap)
                    low = middle;
                else
                    high = middle;
            }
            return high;
        }
        return float.PositiveInfinity;
    }

    // Slot ray angles: the first igloo at the top, then each next one clockwise with the same
    // edge-to-edge gap, chosen so the last igloo is also that gap from the first.
    public static float[] SlotAngles(int count, float gap, Vector2[] outline)
    {
        if (count < 4 || count > 8)
            throw new System.ArgumentOutOfRangeException(nameof(count));
        Vector2[] ring = IglooRing(gap, outline);
        var positions = new float[count];
        float low = 0, high = 0;
        for (int i = 0; i < RingSamples; i++)
            high += Vector2.Distance(ring[i], ring[(i + 1) % RingSamples]);
        high /= count;
        for (int iteration = 0; iteration < 30; iteration++)
        {
            float spacing = (low + high) / 2;
            for (int i = 1; i < count; i++)
                positions[i] = Step(ring, outline, positions[i - 1], spacing);
            bool fits = positions[count - 1] < RingSamples
                && SilhouetteGap(ring[0] - RingAt(ring, positions[count - 1]), outline) >= spacing;
            if (fits)
                low = spacing;
            else
                high = spacing;
        }
        var angles = new float[count];
        for (int i = 1; i < count; i++)
        {
            positions[i] = Step(ring, outline, positions[i - 1], low);
            angles[i] = 2 * Mathf.PI * positions[i] / RingSamples;
        }
        return angles;
    }

    public static Vector2[] CharacterPositions(float[] angles, float gap)
    {
        var positions = new Vector2[angles.Length];
        for (int i = 0; i < angles.Length; i++)
            positions[i] = CharacterPosition(angles[i], gap);
        return positions;
    }

    public Vector3 ReferenceToWorld(Vector2 point)
    {
        Bounds bounds = background.sprite.bounds;
        Vector3 local = bounds.center + new Vector3(point.x / 19.2f * bounds.size.x,
            point.y / 10.8f * bounds.size.y, 0);
        return background.transform.TransformPoint(local);
    }

    private void AlignWithBackground()
    {
        if (generated == null || background == null || background.sprite == null)
            return;
        generated.position = ReferenceToWorld(Vector2.zero);
        generated.rotation = background.transform.rotation;
        Vector3 size = background.sprite.bounds.size;
        Vector3 scale = background.transform.lossyScale;
        Vector3 parentScale = transform.lossyScale;
        generated.localScale = new Vector3(size.x / 19.2f * scale.x / parentScale.x,
            size.y / 10.8f * scale.y / parentScale.y, 1);
    }

    private void OnDrawGizmosSelected()
    {
        if (background == null || background.sprite == null)
            return;
        Gizmos.color = Color.green;
        for (int i = 0; i < 96; i++)
            Gizmos.DrawLine(ReferenceToWorld(BoundaryPoint(i * Mathf.PI * 2 / 96)),
                ReferenceToWorld(BoundaryPoint((i + 1) * Mathf.PI * 2 / 96)));
        if (igloo == null)
            return;
        Gizmos.color = Color.yellow;
        foreach (float angle in SlotAngles(Mathf.Clamp(playerCount, 4, 8), iglooGap, IglooOutline(igloo)))
        {
            // Slot ray from the center, through the feet, out past the ice line.
            Gizmos.DrawLine(ReferenceToWorld(LayoutCenter),
                ReferenceToWorld(LayoutCenter + SlotDirection(angle) * (IceRadii.x + 1f)));
        }
    }

    private void OnEnable() => renderedCount = 0;
    private void OnDisable() => ObserveRoster(null);

    private LobbyProfiles observedProfiles;
    private bool rosterDirty;

    private void ObserveRoster(LobbyProfiles profiles)
    {
        if (observedProfiles == profiles)
            return;
        if (observedProfiles != null)
            observedProfiles.Changed -= MarkRosterDirty;
        observedProfiles = profiles;
        if (profiles != null)
            profiles.Changed += MarkRosterDirty;
        rosterDirty = true;
    }

    private void MarkRosterDirty() => rosterDirty = true;
    // Layout settings rebuild in Update; time-of-day changes only relight, so scrubbing stays cheap.
    private void OnValidate() => ApplyLighting();

    private void Update()
    {
        if (transitioning && Application.isPlaying)
        {
            transitionElapsed += Time.deltaTime;
            float t = transitionDuration > 0f ? Mathf.Clamp01(transitionElapsed / transitionDuration) : 1f;
            timeOfDay = Mathf.Repeat(transitionFrom + transitionSpan * Mathf.SmoothStep(0, 1, t), DayNightCycle.Hours);
            transitioning = t < 1f;
        }
        ApplyLighting();

        int count = Mathf.Clamp(playerCount, 4, 8);
        var session = Application.isPlaying ? RoomSessionManager.Instance : null;
        ObserveRoster(session != null ? session.Profiles : null);
        bool settingsChanged = renderedCharacterGap != characterGap || renderedIglooGap != iglooGap;
        if (session != null)
        {
            // Lobby roster drives the match: player count, order, names and colors.
            // A late snapshot (or an invalid count, which clears the layout) triggers a rebuild.
            if (rosterDirty || generated == null || settingsChanged)
                Rebuild();
        }
#if !UNITY_EDITOR
        else
            return;
#else
        else if (renderedCount != count || generated == null || settingsChanged)
            Rebuild();
#endif

        AlignWithBackground();

        if (layoutCamera != null && background != null)
        {
            Bounds bounds = background.bounds;
            layoutCamera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x / layoutCamera.aspect);
            layoutCamera.transform.position = new Vector3(bounds.center.x, bounds.center.y,
                layoutCamera.transform.position.z);
        }
    }

    public void Rebuild()
    {
        if (igloo == null || character == null || outfit == null || spriteMaterial == null || nameFont == null)
            return;
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name != "Layout")
                continue;
            child.gameObject.SetActive(false);
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }

        rosterDirty = false;
        windowLights.Clear();
        nameLabels.Clear();
        renderedCharacterGap = characterGap;
        renderedIglooGap = iglooGap;
        generated = new GameObject("Layout").transform;
        generated.SetParent(transform, false);
#if UNITY_EDITOR
        if (!Application.isPlaying)
            generated.gameObject.tag = "EditorOnly";
#endif
        var session = Application.isPlaying ? RoomSessionManager.Instance : null;
        var players = session != null ? session.Profiles.Roster.Players : null;
        int count = players != null ? players.Count : Mathf.Clamp(playerCount, 4, 8);
        renderedCount = count;
        if (count < 4 || count > 8)
            return;
        AlignWithBackground();
        Vector2[] iglooOutline = IglooOutline(igloo);
        float[] angles = SlotAngles(count, iglooGap, iglooOutline);
        for (int i = 0; i < count; i++)
        {
            // The igloo import sets its size (pixels per unit) and a base pivot, so place it unscaled.
            var iglooRenderer = MakeSprite("Igloo " + (i + 1), igloo, IglooPosition(angles[i], iglooGap, iglooOutline),
                igloo.bounds.size, Color.white, 1, true);
            // Warm window glow that fades in as the ambient light drops.
            var window = new GameObject("Window Light").AddComponent<Light2D>();
            window.transform.SetParent(iglooRenderer.transform, false);
            window.transform.localPosition = (Vector2.Scale(IglooWindow, igloo.rect.size) - igloo.pivot) / igloo.pixelsPerUnit;
            window.lightType = Light2D.LightType.Point;
            window.shadowsEnabled = false;
            window.color = WindowLightColor;
            // Point radii are world units and ignore the transform's scale.
            window.pointLightOuterRadius = WindowLightRadius * generated.lossyScale.x;
            window.pointLightInnerRadius = 0.08f * generated.lossyScale.x;
            window.falloffIntensity = 0.6f;
            windowLights.Add(window);
            // Penguin pivot is its feet; the penguin stays untinted and its outfit carries the player color.
            var penguin = MakeSprite("Character " + (i + 1), character, FootPosition(angles[i], characterGap),
                character.bounds.size, Color.white, 2, true);
            // The art faces right: penguins on the right half turn left, toward the middle.
            penguin.flipX = SlotDirection(angles[i]).x > 0.001f;

            // Mock layouts use the lobby's default order (first free color per join).
            LobbyColor color = players != null ? players[i].Color : (LobbyColor)i;
            var clothes = new GameObject("Outfit").AddComponent<SpriteRenderer>();
            clothes.transform.SetParent(penguin.transform, false);
            clothes.sprite = outfit;
            clothes.sharedMaterial = spriteMaterial;
            clothes.color = PlayerColor(color);
            clothes.flipX = penguin.flipX;
            clothes.sortingOrder = 3;

            var label = new GameObject("Name");
            label.transform.SetParent(penguin.transform, false);
            label.transform.localPosition = new Vector3(0, CharacterHeight / 2f + 0.55f, -0.1f);
            var text = label.AddComponent<TextMesh>();
            text.font = nameFont;
            text.fontSize = 64;
            text.characterSize = 0.04f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.richText = false;
            text.text = players != null ? players[i].Name : "Player " + (i + 1);
            nameLabels.Add(text);
            var renderer = label.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = nameFont.material;
            renderer.sortingOrder = 4;
        }
        ApplyLighting();
    }

    // Ambient light, window glow, letterbox color, and name contrast for the current time of day.
    // Names stay unlit, switching to a light color at night so they remain readable.
    private void ApplyLighting()
    {
        DayNightCycle.Evaluate(timeOfDay, out Color ambient, out float intensity);
        float darkness = DayNightCycle.Darkness(intensity);
        if (globalLight != null)
        {
            globalLight.color = ambient;
            globalLight.intensity = intensity;
        }
        if (layoutCamera != null)
        {
            Color background = DayBackground * ambient * intensity;
            background.a = 1f;
            layoutCamera.backgroundColor = background;
        }
        foreach (Light2D window in windowLights)
        {
            if (window == null)
                continue;
            window.intensity = WindowLightIntensity * darkness;
            window.enabled = darkness > 0.001f;
        }
        Color nameColor = Color.Lerp(DayNameColor, NightNameColor, darkness);
        foreach (TextMesh label in nameLabels)
            if (label != null)
                label.color = nameColor;
    }

    private SpriteRenderer MakeSprite(string objectName, Sprite sprite, Vector2 position,
        Vector2 size, Color color, int order, bool anchorAtPivot = false)
    {
        var item = new GameObject(objectName);
        item.transform.SetParent(generated, false);
        item.transform.localPosition = position;
        item.transform.localScale = new Vector3(size.x / sprite.bounds.size.x,
            size.y / sprite.bounds.size.y, 1);
        // Keep the visual center stable unless the sprite's pivot is the intended anchor.
        if (!anchorAtPivot)
            item.transform.localPosition -= Vector3.Scale(sprite.bounds.center, item.transform.localScale);
        var renderer = item.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sharedMaterial = spriteMaterial;
        renderer.color = color;
        renderer.sortingOrder = order;
        return renderer;
    }
}
