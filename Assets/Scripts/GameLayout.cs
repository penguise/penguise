using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

[ExecuteAlways]
public sealed class GameLayout : MonoBehaviour
{
    [SerializeField, Range(4, 8)] private int playerCount = 8;
    [SerializeField, HideInInspector] private Sprite igloo;
    // Door leaf faces, cropped from the igloo canvas with the pivot on the hinge (see IglooDoor).
    [SerializeField, HideInInspector] private Sprite doorFront;
    [SerializeField, HideInInspector] private Sprite doorBack;
    [FormerlySerializedAs("capsule"), SerializeField, HideInInspector] private Sprite character;
    // White outfit art drawn over the penguin (same canvas and pivot), tinted per player color.
    [SerializeField, HideInInspector] private Sprite outfit;
    // Run cycle frames for the penguin and its outfit (PenguinRun/PenguinOutfitRun sheets, same pivot).
    [SerializeField, HideInInspector] private Sprite[] runFrames;
    [SerializeField, HideInInspector] private Sprite[] runOutfitFrames;
    // Idle clips back to back (PenguinIdle0..3 / PenguinOutfitIdle0..3 sheets, same pivot), see PenguinIdle.
    [SerializeField, HideInInspector] private Sprite[] idleFrames;
    [SerializeField, HideInInspector] private Sprite[] idleOutfitFrames;
    // Knocked-out clips (PenguinDizzy/Faint/Down and their outfit sheets, same pivot), see PenguinRunner.
    [SerializeField, HideInInspector] private Sprite[] dizzyFrames, dizzyOutfitFrames;
    [SerializeField, HideInInspector] private Sprite[] faintFrames, faintOutfitFrames;
    [SerializeField, HideInInspector] private Sprite[] downFrames, downOutfitFrames;
    // Fishing clips (Tools/ArtGen/penguin/export_fishing.py): body and outfit (same pivot as the others), the
    // tackle in front, and the catch's unlit sparkles and "+1" (a pre-mirrored sheet for flipped penguins).
    [SerializeField, HideInInspector] private Sprite[] fishCatchFrames, fishCatchOutfitFrames, fishCatchTackle;
    [SerializeField, HideInInspector] private Sprite[] fishCatchEffect, fishCatchEffectFlipped;
    [SerializeField, HideInInspector] private Sprite[] fishMissFrames, fishMissOutfitFrames, fishMissTackle;
    // Igloo doorway opening, pivot on the door hinge: masks penguins once they pass the door plane.
    [SerializeField, HideInInspector] private Sprite doorway;
    // Lamp-off window pane (centre pivot) shown while a visitor is inside, see IglooAction.
    [SerializeField, HideInInspector] private Sprite windowDark;
    // Heal action: mint window pane (same crop and pivot as windowDark) and the particles rising from the
    // dome (HealPlus/HealSparkle, centre pivot, igloo scale), drawn unlit with effectMaterial.
    [SerializeField, HideInInspector] private Sprite windowHeal, healPlus, healSparkle;
    [SerializeField, HideInInspector] private Material effectMaterial;
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
    // Door preview: opens every igloo door (0 closed, 1 fully open). Runtime code drives single doors
    // through Doors; changing this slider overrides them all.
    [SerializeField, Range(0f, 1f)] private float doorOpen;
    // Preview: on, every penguin runs into its own igloo; off, they run back out to their slots.
    // Runtime code uses RunIntoIgloo(slot) / RunOutOfIgloo(slot) or the RunAll... forms instead.
    [SerializeField] private bool runIntoIgloos;
    // Ground speed in layout units per second; the run cycle keeps pace (0.55 matches the art's stride).
    [SerializeField, Range(0.3f, 3f)] private float runSpeed = 1.1f;
    // Idle animation of penguins standing on their slots (breathing plus random fidgets); off shows the
    // still pose. Also animates in Edit mode.
    [SerializeField] private bool idleAnimation = true;
    // Preview: the slot (1-based, 0 off) played as "us". In Play mode, clicking another igloo sends that
    // penguin on a visit (out of its own igloo, or from its slot, into the clicked one, a short wait, then
    // back into its own igloo); clicking its own igloo runs it in or out. Runtime code uses Visit(slot, target, action).
    [SerializeField, Range(0, 8)] private int playAs;
    // Preview: what the Play As penguin does inside the igloo it visits (seen from outside).
    [SerializeField] private IglooAction.Kind visitAction = IglooAction.Kind.KnockOut;
    // Preview: clicking the ice hole sends the Play As penguin fishing there; this picks the clip.
    public enum FishingOutcome { Random, Catch, Miss }
    [SerializeField] private FishingOutcome fishingOutcome = FishingOutcome.Random;
    // Preview: knocks out that slot (1-based, 0 off). Inside its igloo it walks out dizzy on the next run out
    // and faints past its door; standing on its slot, it faints at once. Changing the field brings the
    // previous one back, so only the chosen slot is knocked out. Runtime code uses KnockOut(slot).
    [SerializeField, Range(0, 8)] private int knockOut;
    // Ground speed of a knocked-out penguin's dizzy walk out (units/s); the art's own pace is
    // PenguinRunner.DizzyArtSpeed, and frames follow distance, so faster plays the loop faster.
    [SerializeField, Range(0.2f, 1.5f)] private float dizzySpeed = 0.6f;
    private int renderedKnockOut;
    public float CharacterGap => characterGap;
    public float IglooGap => iglooGap;
    private int renderedCount;
    private float renderedCharacterGap = float.NaN, renderedIglooGap = float.NaN, renderedDoorOpen = float.NaN;
    private readonly List<IglooDoor> doors = new List<IglooDoor>();
    public IReadOnlyList<IglooDoor> Doors => doors;
    private readonly List<IglooAction> actions = new List<IglooAction>();
    // Per igloo: what the outside sees while a visitor is inside (knock-out or heal).
    public IReadOnlyList<IglooAction> Actions => actions;
    private readonly List<PenguinRunner> runners = new List<PenguinRunner>();
    public IReadOnlyList<PenguinRunner> Runners => runners;
    private readonly List<PenguinIdle> idles = new List<PenguinIdle>();
    public IReadOnlyList<PenguinIdle> Idles => idles;
    private bool renderedRun;

    public void RunIntoIgloo(int slot)
    {
        if (slot >= 0 && slot < runners.Count && runners[slot] != null)
            runners[slot].RunIntoIgloo(runSpeed);
    }

    public void RunAllIntoIgloos()
    {
        for (int i = 0; i < runners.Count; i++)
            RunIntoIgloo(i);
    }

    public void RunOutOfIgloo(int slot)
    {
        if (slot >= 0 && slot < runners.Count && runners[slot] != null)
            runners[slot].RunOutOfIgloo(runSpeed);
    }

    public void RunAllOutOfIgloos()
    {
        for (int i = 0; i < runners.Count; i++)
            RunOutOfIgloo(i);
    }

    // Knocks out the penguin of `slot` for good (see PenguinRunner.KnockOut): it comes out of its igloo
    // dizzy and faints on its slot, or faints at once if it stands there.
    public void KnockOut(int slot)
    {
        if (slot >= 0 && slot < runners.Count && runners[slot] != null)
            runners[slot].KnockOut();
    }

    // Seconds a visitor stays inside the igloo it visits; that igloo's IglooAction stretches to the stay.
    public const float VisitMinSeconds = 3f, VisitMaxSeconds = 5f;
    // Ground area (reference units) where visit routes may turn: on screen, right of the chat panel, with
    // room for the penguin above its feet.
    public static readonly Rect WalkArea = Rect.MinMaxRect(-5.2f, -5.3f, 9.3f, 4.6f);
    // Ice hole in the background art (reference units) and the ground radius visit routes keep from it
    // (its snow rim plus half a penguin).
    public static readonly Vector2 IceHole = new Vector2(1.95f, -0.12f);
    public const float IceHoleClearance = 1.1f;
    // Fishing (reference units): the water's centre in the hole, the hole's opening (centre and radii, a
    // little wider than the art for clicks), and how far left or right of the water a fishing penguin's feet
    // stand (fish_catch.GAME_STAND: the float lands where the art casts it).
    public static readonly Vector2 FishingWater = new Vector2(1.975f, -0.195f);
    public static readonly Vector2 HoleOpening = new Vector2(1.97f, -0.095f);
    public static readonly Vector2 HoleClickRadii = new Vector2(0.6f, 0.45f);
    public const float FishingStand = 0.72f;

    private Vector2[] iglooPositions, feet, iglooOutline;
    private Vector2 domeCenter;
    private float unitsPerPixel;
    // The action each visiting slot plays in the igloo it visits.
    private readonly Dictionary<int, IglooAction.Kind> visitActions = new Dictionary<int, IglooAction.Kind>();
    private readonly Dictionary<(int, int, bool), PenguinRunner.Route> visitRoutes =
        new Dictionary<(int, int, bool), PenguinRunner.Route>();
    private readonly Dictionary<(int, int, bool), PenguinRunner.Route> fishingRoutes =
        new Dictionary<(int, int, bool), PenguinRunner.Route>();

    // Stand point (reference units) left (side -1) or right (+1) of the hole's water.
    public static Vector2 FishingStandPoint(int side) => FishingWater + new Vector2(side * FishingStand, 0f);

    // True if `point` (layout units) is on the ice hole's opening.
    public static bool OnIceHole(Vector2 point)
    {
        Vector2 d = point - HoleOpening;
        return d.x * d.x / (HoleClickRadii.x * HoleClickRadii.x)
            + d.y * d.y / (HoleClickRadii.y * HoleClickRadii.y) <= 1f;
    }

    // Sends the penguin of `slot` fishing: out of its own igloo when inside (or from its slot when standing
    // there) straight to the nearer side of the ice hole (whichever stand point its route reaches sooner),
    // the catch or miss clip there facing the water, then home into its own igloo. False if it is busy or
    // knocked out, or no route keeps clear of the igloos.
    public bool GoFishing(int slot, bool caught)
    {
        if (slot < 0 || slot >= runners.Count || runners[slot] == null)
            return false;
        PenguinRunner runner = runners[slot];
        bool inside = runner.Current == PenguinRunner.State.Inside;
        if (runner.KnockedOut || !inside && runner.Current != PenguinRunner.State.Idle)
            return false;
        int side = FishingSide(slot, !inside);
        if (side == 0)
        {
            Debug.LogWarning($"No route from igloo {slot + 1} to the ice hole.", this);
            return false;
        }
        return runner.GoFishing(FishingRoute(slot, side, !inside), FishingRoute(slot, side, null), runSpeed, caught,
            side > 0);
    }

    // Route length (ground units) under which the other side of the hole does not count as shorter.
    public const float FishingSideTie = 0.3f;

    // The stand side (-1 left, +1 right, 0 none) whose route from igloo `slot` (its tunnel, or its slot) is
    // shorter.
    public int FishingSide(int slot, bool fromSlot)
    {
        // Nearly equal ways (within FishingSideTie) go to the side the igloo is on.
        int own = iglooPositions != null && slot >= 0 && slot < iglooPositions.Length
            && iglooPositions[slot].x + domeCenter.x > FishingWater.x ? 1 : -1;
        int best = 0;
        float shortest = float.PositiveInfinity;
        foreach (int side in new[] { own, -own })
        {
            PenguinRunner.Route go = FishingRoute(slot, side, fromSlot);
            if (go != null && FishingRoute(slot, side, null) != null
                && go.Length < shortest - (best == own ? FishingSideTie : 0f))
            {
                shortest = go.Length;
                best = side;
            }
        }
        return best;
    }

    // Route for igloo `slot` and hole side `side`: `fromSlot` true from its penguin's slot, false from its
    // tunnel, to the stand point; null from the stand point back into its tunnel. Cached until the next rebuild.
    public PenguinRunner.Route FishingRoute(int slot, int side, bool? fromSlot)
    {
        if (iglooPositions == null || slot < 0 || slot >= iglooPositions.Length || doors.Count != iglooPositions.Length
            || side != -1 && side != 1)
            return null;
        int key = fromSlot.HasValue ? (fromSlot.Value ? 1 : 0) : 2;
        if (fishingRoutes.TryGetValue((slot, key, side > 0), out PenguinRunner.Route cached))
            return cached;
        Vector2 origin = iglooPositions[slot] + domeCenter;
        var domes = new List<Vector2>();
        foreach (Vector2 position in iglooPositions)
            domes.Add((position - iglooPositions[slot]) / unitsPerPixel);
        Rect area = Rect.MinMaxRect((WalkArea.xMin - origin.x) / unitsPerPixel, (WalkArea.yMin - origin.y) / unitsPerPixel,
            (WalkArea.xMax - origin.x) / unitsPerPixel, (WalkArea.yMax - origin.y) / unitsPerPixel);
        var hole = new[] { ((IceHole - origin) / unitsPerPixel, IceHoleClearance / unitsPerPixel) };
        Vector2 stand = FishingStandPoint(side);
        Vector2 standOffset = (stand - origin) / unitsPerPixel;
        PenguinRunner.Route route;
        if (fromSlot.HasValue)
        {
            Vector2? start = fromSlot.Value ? (feet[slot] - origin) / unitsPerPixel : (Vector2?)null;
            List<IglooGround.Waypoint> path = IglooGround.PathBetween(start, slot, slot, domes, area, hole, standOffset);
            route = path == null ? null : MakeRoute(path, origin, fromSlot.Value ? null : doors[slot], null,
                fromSlot.Value ? feet[slot] : (Vector2?)null);
            if (route != null)
                route.Points[route.Points.Length - 1] = stand;
        }
        else
        {
            List<IglooGround.Waypoint> path = IglooGround.PathBetween(standOffset, slot, slot, domes, area, hole);
            route = path == null ? null : MakeRoute(path, origin, null, doors[slot], stand);
        }
        fishingRoutes[(slot, key, side > 0)] = route;
        return route;
    }

    // Sends the penguin of `slot` into igloo `target`: out of its own igloo when inside (or from its slot
    // when standing there), a wait of 3-5 s while the igloo plays `action` (knock-out: its lamp flickers and
    // it shakes; heal: it beats with a mint glow and sparkles), then back into its own igloo. False if it is
    // busy, the slots are invalid or no route keeps clear of the igloos.
    public bool Visit(int slot, int target, IglooAction.Kind action = IglooAction.Kind.KnockOut)
    {
        if (slot < 0 || slot >= runners.Count || target < 0 || target >= runners.Count || slot == target
            || runners[slot] == null)
            return false;
        PenguinRunner runner = runners[slot];
        bool inside = runner.Current == PenguinRunner.State.Inside;
        if (!inside && runner.Current != PenguinRunner.State.Idle)
            return false;
        PenguinRunner.Route back = VisitRoute(slot, target, false);
        PenguinRunner.Route visit = inside ? back : VisitRoute(slot, target, true);
        if (back == null || visit == null)
        {
            Debug.LogWarning($"No route from igloo {slot + 1} to igloo {target + 1}.", this);
            return false;
        }
        if (!runner.Visit(visit, inside ? null : back, runSpeed, Random.Range(VisitMinSeconds, VisitMaxSeconds)))
            return false;
        visitActions[slot] = action;
        return true;
    }

    // Route from igloo `slot` (its tunnel, or its penguin's slot) into igloo `target`'s tunnel; cached until
    // the next rebuild.
    public PenguinRunner.Route VisitRoute(int slot, int target, bool fromSlot)
    {
        if (iglooPositions == null || slot < 0 || slot >= iglooPositions.Length || target < 0
            || target >= iglooPositions.Length || slot == target || doors.Count != iglooPositions.Length)
            return null;
        if (visitRoutes.TryGetValue((slot, target, fromSlot), out PenguinRunner.Route cached))
            return cached;
        Vector2 origin = iglooPositions[slot] + domeCenter;
        var domes = new List<Vector2>();
        foreach (Vector2 position in iglooPositions)
            domes.Add((position - iglooPositions[slot]) / unitsPerPixel);
        Rect area = Rect.MinMaxRect((WalkArea.xMin - origin.x) / unitsPerPixel, (WalkArea.yMin - origin.y) / unitsPerPixel,
            (WalkArea.xMax - origin.x) / unitsPerPixel, (WalkArea.yMax - origin.y) / unitsPerPixel);
        Vector2? start = fromSlot ? (feet[slot] - origin) / unitsPerPixel : (Vector2?)null;
        var hole = new[] { ((IceHole - origin) / unitsPerPixel, IceHoleClearance / unitsPerPixel) };
        List<IglooGround.Waypoint> path = IglooGround.PathBetween(start, slot, target, domes, area, hole);
        PenguinRunner.Route route = path == null ? null
            : MakeRoute(path, origin, fromSlot ? null : doors[slot], doors[target], fromSlot ? feet[slot] : (Vector2?)null);
        visitRoutes[(slot, target, fromSlot)] = route;
        return route;
    }

    // Ground waypoints (canvas pixels around `origin`) as a route in layout units.
    private PenguinRunner.Route MakeRoute(List<IglooGround.Waypoint> path, Vector2 origin, IglooDoor startDoor,
        IglooDoor endDoor, Vector2? firstPoint)
    {
        var route = new PenguinRunner.Route
        {
            Points = new Vector2[path.Count],
            Distances = new float[path.Count],
            Masked = new bool[path.Count],
            Shades = new float[path.Count],
            StartDoor = startDoor,
            EndDoor = endDoor,
        };
        int firstClear = -1, lastClear = -1;
        for (int k = 0; k < path.Count; k++)
        {
            if (path[k].DoorClear)
            {
                if (firstClear < 0)
                    firstClear = k;
                lastClear = k;
            }
            route.Points[k] = origin + path[k].Pixels * unitsPerPixel;
            route.Distances[k] = k == 0 ? 0f
                : route.Distances[k - 1] + Vector3.Distance(path[k - 1].Frame, path[k].Frame) * unitsPerPixel;
            route.Masked[k] = path[k].Masked;
            route.Shades[k] = path[k].Shade;
        }
        if (firstPoint.HasValue)
            route.Points[0] = firstPoint.Value;
        route.StartClear = startDoor != null && firstClear >= 0 ? route.Distances[firstClear] : 0f;
        route.EndClear = lastClear >= 0 ? route.Distances[lastClear] : 0f;
        return route;
    }

    // Front-most igloo whose silhouette holds `point` (layout units), or -1.
    public int IglooAt(Vector2 point)
    {
        if (iglooPositions == null || iglooOutline == null)
            return -1;
        int hit = -1;
        for (int i = 0; i < iglooPositions.Length; i++)
        {
            Vector2 local = point - iglooPositions[i];
            bool positive = false, negative = false;
            for (int k = 0; k < iglooOutline.Length; k++)
            {
                float side = Cross(iglooOutline[k], iglooOutline[(k + 1) % iglooOutline.Length], local);
                positive |= side > 0f;
                negative |= side < 0f;
            }
            if (!(positive && negative) && (hit < 0 || iglooPositions[i].y < iglooPositions[hit].y))
                hit = i;
        }
        return hit;
    }

    // Play-mode preview input for the Play As slot.
    private void HandleClick()
    {
        if (!Application.isPlaying || playAs < 1 || playAs > runners.Count || layoutCamera == null || generated == null)
            return;
        var pointer = UnityEngine.InputSystem.Pointer.current;
        if (pointer == null || !pointer.press.wasPressedThisFrame)
            return;
        Vector3 world = layoutCamera.ScreenToWorldPoint(pointer.position.ReadValue());
        Vector2 point = generated.InverseTransformPoint(world);
        int slot = playAs - 1;
        if (runners[slot] == null)
            return;
        if (OnIceHole(point))
        {
            bool caught = fishingOutcome == FishingOutcome.Catch
                || fishingOutcome == FishingOutcome.Random && Random.value < 0.5f;
            GoFishing(slot, caught);
            return;
        }
        int target = IglooAt(point);
        if (target < 0)
            return;
        if (target != slot)
            Visit(slot, target, visitAction);
        else if (runners[slot].Current == PenguinRunner.State.Inside)
            runners[slot].RunOutOfIgloo(runSpeed);
        else
            runners[slot].RunIntoIgloo(runSpeed);
    }

    // Window glow: texture fractions (y up) of the igloo art's round window, plus light shape and color.
    private static readonly Vector2 IglooWindow = new Vector2(0.2587f, 0.4336f);
    // Centre of the lamp-off pane's crop (IglooWindowDark.png; IglooWindowHeal.png shares it) as a texture
    // fraction of the igloo art.
    private static readonly Vector2 IglooWindowDark = new Vector2(0.261164f, 0.433812f);
    // Door hinge (bottom of the leaf's right edge) as a texture fraction of the igloo art, y up.
    private static readonly Vector2 IglooDoorHinge = new Vector2(0.775521f, 0.231698f);
    // Top of the dome as a texture fraction of the igloo art: heal particles rise from here.
    private static readonly Vector2 IglooApex = new Vector2(0.505582f, 0.797970f);
    // Names draw above every igloo and penguin (depth orders stay well below this).
    public const int NameOrder = 1000;
    private const float WindowLightRadius = 0.75f;
    private const float WindowLightIntensity = 1.4f;
    private static readonly Color WindowLightColor = new Color(1f, 0.78f, 0.45f);
    // Mint light at the window while a heal glows (scaled by IglooAction.Glow).
    private const float HealLightIntensity = 1.5f;
    private static readonly Color HealLightColor = new Color(0.45f, 1f, 0.7f);
    private static readonly Color DayBackground = new Color(0.863f, 0.929f, 0.957f);
    private static readonly Color DayNameColor = new Color32(32, 55, 73, 255);
    private static readonly Color NightNameColor = new Color32(236, 241, 250, 255);
    private readonly List<Light2D> windowLights = new List<Light2D>();
    private readonly List<Light2D> healLights = new List<Light2D>();
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
        new Vector2(0.0941f, 0.3620f), new Vector2(0.0973f, 0.3453f), new Vector2(0.1077f, 0.3206f),
        new Vector2(0.1372f, 0.2919f), new Vector2(0.1571f, 0.2775f), new Vector2(0.1898f, 0.2584f),
        new Vector2(0.2400f, 0.2384f), new Vector2(0.2823f, 0.2265f), new Vector2(0.5144f, 0.1659f),
        new Vector2(0.6300f, 0.1778f), new Vector2(0.7879f, 0.2073f), new Vector2(0.8788f, 0.2329f),
        new Vector2(0.8804f, 0.2344f), new Vector2(0.9163f, 0.3549f), new Vector2(0.9171f, 0.3620f),
        new Vector2(0.9147f, 0.4171f), new Vector2(0.9091f, 0.4585f), new Vector2(0.8995f, 0.5016f),
        new Vector2(0.8876f, 0.5399f), new Vector2(0.8740f, 0.5742f), new Vector2(0.8581f, 0.6069f),
        new Vector2(0.8429f, 0.6332f), new Vector2(0.8254f, 0.6595f), new Vector2(0.7959f, 0.6962f),
        new Vector2(0.7703f, 0.7225f), new Vector2(0.7488f, 0.7416f), new Vector2(0.7249f, 0.7600f),
        new Vector2(0.6994f, 0.7767f), new Vector2(0.6635f, 0.7959f), new Vector2(0.6372f, 0.8070f),
        new Vector2(0.6164f, 0.8142f), new Vector2(0.5869f, 0.8222f), new Vector2(0.5622f, 0.8270f),
        new Vector2(0.5215f, 0.8309f), new Vector2(0.4896f, 0.8309f), new Vector2(0.4490f, 0.8270f),
        new Vector2(0.4242f, 0.8222f), new Vector2(0.3947f, 0.8142f), new Vector2(0.3565f, 0.7998f),
        new Vector2(0.3317f, 0.7879f), new Vector2(0.3054f, 0.7727f), new Vector2(0.2863f, 0.7600f),
        new Vector2(0.2624f, 0.7416f), new Vector2(0.2408f, 0.7225f), new Vector2(0.2153f, 0.6962f),
        new Vector2(0.1858f, 0.6595f), new Vector2(0.1683f, 0.6332f), new Vector2(0.1531f, 0.6069f),
        new Vector2(0.1372f, 0.5742f), new Vector2(0.1236f, 0.5399f), new Vector2(0.1116f, 0.5016f),
        new Vector2(0.1021f, 0.4585f), new Vector2(0.0965f, 0.4171f),
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
    private void OnValidate()
    {
        ApplyLighting();
        ApplyDoors();
#if UNITY_EDITOR
        // Edit-mode Update only follows scene changes; apply the toggles on the next Editor tick.
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.update -= ApplySettingsOnce;
            UnityEditor.EditorApplication.update += ApplySettingsOnce;
        }
#endif
    }

#if UNITY_EDITOR
    private void ApplySettingsOnce()
    {
        UnityEditor.EditorApplication.update -= ApplySettingsOnce;
        if (this == null || Application.isPlaying)
            return;
        ApplyRunSetting();
        ApplyIdleSetting();
    }
#endif

    private void ApplyIdleSetting()
    {
        foreach (PenguinIdle idle in idles)
            if (idle != null)
                idle.Playing = idleAnimation;
    }

    // Inspector run toggle: on sends every penguin into its igloo, off brings them back out to their slots
    // (a penguin still on its way turns around).
    private void ApplyRunSetting()
    {
        if (runIntoIgloos == renderedRun || generated == null)
            return;
        renderedRun = runIntoIgloos;
        if (runIntoIgloos)
            RunAllIntoIgloos();
        else
            RunAllOutOfIgloos();
    }

    private static bool Frames(Sprite[] body, Sprite[] outfit, int count) =>
        body != null && body.Length == count && outfit != null && outfit.Length == count;

    // Inspector knock-out preview: brings the previous slot back and knocks out the chosen one (0: none).
    // Dragging the slider passes through other slots, which must not stay knocked out.
    private void ApplyKnockOutSetting()
    {
        if (knockOut == renderedKnockOut || generated == null)
            return;
        int previous = renderedKnockOut - 1;
        if (previous >= 0 && previous < runners.Count && runners[previous] != null)
            runners[previous].UndoKnockOutPreview();
        renderedKnockOut = knockOut;
        if (knockOut > 0)
            KnockOut(knockOut - 1);
    }

    private void ApplyDizzySpeed()
    {
        foreach (PenguinRunner runner in runners)
            if (runner != null && runner.DizzySpeed != dizzySpeed)
                runner.DizzySpeed = dizzySpeed;
    }

    // Applies the inspector door slider when it changes, so runtime door control is left alone otherwise.
    private void ApplyDoors()
    {
        if (renderedDoorOpen == doorOpen)
            return;
        renderedDoorOpen = doorOpen;
        foreach (IglooDoor door in doors)
            if (door != null)
                door.Open = doorOpen;
    }

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
        ApplyDoors();

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
        ApplyRunSetting();
        ApplyDizzySpeed();
        ApplyKnockOutSetting();
        ApplyIdleSetting();
        HandleClick();

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
        if (igloo == null || doorFront == null || doorBack == null || character == null || outfit == null
            || doorway == null || windowDark == null || windowHeal == null || healPlus == null || healSparkle == null
            || effectMaterial == null || runFrames == null || runFrames.Length != PenguinRunner.RunFrames
            || runOutfitFrames == null || runOutfitFrames.Length != PenguinRunner.RunFrames
            || idleFrames == null || idleFrames.Length != PenguinIdle.TotalFrames
            || idleOutfitFrames == null || idleOutfitFrames.Length != PenguinIdle.TotalFrames
            || !Frames(dizzyFrames, dizzyOutfitFrames, PenguinRunner.DizzyFrames)
            || !Frames(faintFrames, faintOutfitFrames, PenguinRunner.FaintFrames)
            || !Frames(downFrames, downOutfitFrames, PenguinRunner.DownFrames)
            || !Frames(fishCatchFrames, fishCatchOutfitFrames, PenguinRunner.CatchFrames)
            || !Frames(fishMissFrames, fishMissOutfitFrames, PenguinRunner.MissFrames)
            || fishCatchTackle == null || fishCatchTackle.Length != PenguinRunner.CatchTackleFrames
            || fishMissTackle == null || fishMissTackle.Length != PenguinRunner.MissTackleFrames
            || !Frames(fishCatchEffect, fishCatchEffectFlipped, PenguinRunner.CatchEffectFrames)
            || spriteMaterial == null || nameFont == null)
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
        healLights.Clear();
        nameLabels.Clear();
        doors.Clear();
        actions.Clear();
        runners.Clear();
        idles.Clear();
        visitRoutes.Clear();
        fishingRoutes.Clear();
        visitActions.Clear();
        iglooPositions = feet = null;
        renderedRun = false;
        renderedKnockOut = 0;
        renderedCharacterGap = characterGap;
        renderedIglooGap = iglooGap;
        generated = new GameObject("Layout").transform;
        generated.SetParent(transform, false);
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            generated.gameObject.tag = "EditorOnly";
            // Edit-mode layouts are regenerated on load, so keep them out of the scene file.
            generated.gameObject.hideFlags = HideFlags.DontSave;
        }
#endif
        var session = Application.isPlaying ? RoomSessionManager.Instance : null;
        var players = session != null ? session.Profiles.Roster.Players : null;
        int count = players != null ? players.Count : Mathf.Clamp(playerCount, 4, 8);
        renderedCount = count;
        renderedDoorOpen = doorOpen;
        if (count < 4 || count > 8)
            return;
        AlignWithBackground();
        iglooOutline = IglooOutline(igloo);
        float[] angles = SlotAngles(count, iglooGap, iglooOutline);
        domeCenter = IglooPoint(IglooGround.DomeCenter);
        unitsPerPixel = 1f / IglooCanvasPixelsPerUnit(igloo);
        iglooPositions = new Vector2[count];
        feet = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            iglooPositions[i] = IglooPosition(angles[i], iglooGap, iglooOutline);
            feet[i] = FootPosition(angles[i], characterGap);
        }
        for (int i = 0; i < count; i++)
        {
            // The igloo import sets its size (pixels per unit) and a base pivot, so place it unscaled.
            Vector2 iglooPosition = iglooPositions[i];
            var iglooRenderer = MakeSprite("Igloo " + (i + 1), igloo, iglooPosition,
                igloo.bounds.size, Color.white, 0, true);
            // The igloo and its door sort as one unit, by depth with the penguins: a penguin whose feet are
            // above the dome's base center on screen is behind the igloo.
            var group = iglooRenderer.gameObject.AddComponent<SortingGroup>();
            group.sortingOrder = PenguinRunner.DepthOrder(iglooPosition.y + domeCenter.y);
            var door = IglooDoor.Create(iglooRenderer.transform, IglooPoint(IglooDoorHinge), doorFront, doorBack,
                spriteMaterial, 1);
            door.Open = doorOpen;
            doors.Add(door);
            // Warm window glow that fades in as the ambient light drops.
            var window = new GameObject("Window Light").AddComponent<Light2D>();
            window.transform.SetParent(iglooRenderer.transform, false);
            window.transform.localPosition = IglooPoint(IglooWindow);
            window.lightType = Light2D.LightType.Point;
            window.shadowsEnabled = false;
            window.color = WindowLightColor;
            // Point radii are world units and ignore the transform's scale.
            window.pointLightOuterRadius = WindowLightRadius * generated.lossyScale.x;
            window.pointLightInnerRadius = 0.08f * generated.lossyScale.x;
            window.falloffIntensity = 0.6f;
            windowLights.Add(window);
            var healLight = Instantiate(window, iglooRenderer.transform, false);
            healLight.name = "Heal Light";
            healLight.color = HealLightColor;
            healLight.enabled = false;
            healLights.Add(healLight);
            var pane = new GameObject("Window Dark").AddComponent<SpriteRenderer>();
            pane.transform.SetParent(iglooRenderer.transform, false);
            pane.transform.localPosition = IglooPoint(IglooWindowDark);
            pane.sprite = windowDark;
            pane.sharedMaterial = spriteMaterial;
            pane.sortingOrder = 1;
            var action = iglooRenderer.gameObject.AddComponent<IglooAction>();
            action.Setup(iglooRenderer.transform, pane, 1f / unitsPerPixel);
            var healPane = new GameObject("Window Heal").AddComponent<SpriteRenderer>();
            healPane.transform.SetParent(iglooRenderer.transform, false);
            healPane.transform.localPosition = IglooPoint(IglooWindowDark);
            healPane.sprite = windowHeal;
            healPane.sharedMaterial = spriteMaterial;
            healPane.sortingOrder = 2;
            // Particles stay out of the igloo's sorting group and its hop: unlit, above igloos and penguins.
            var particles = new GameObject("Heal Particles " + (i + 1)).transform;
            particles.SetParent(generated, false);
            action.SetupHeal(healPane, particles, iglooPosition, IglooPoint(IglooApex), IglooPoint(IglooWindow),
                healPlus, healSparkle, effectMaterial, NameOrder - 1);
            actions.Add(action);
            // Penguin pivot is its feet; the penguin stays untinted and its outfit carries the player color.
            Vector2 foot = FootPosition(angles[i], characterGap);
            var penguin = MakeSprite("Character " + (i + 1), character, foot,
                character.bounds.size, Color.white, PenguinRunner.DepthOrder(foot.y), true);
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
            clothes.sortingOrder = penguin.sortingOrder + 1;

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
            renderer.sortingOrder = NameOrder;

            // Doorway mask outside the igloo's sorting group, so it reaches the penguin's renderers.
            var mask = new GameObject("Doorway " + (i + 1)).AddComponent<SpriteMask>();
            mask.transform.SetParent(generated, false);
            mask.transform.localPosition = iglooPosition + IglooPoint(IglooDoorHinge);
            mask.sprite = doorway;

            var runner = penguin.gameObject.AddComponent<PenguinRunner>();
            runner.Setup(penguin, clothes, renderer, runFrames, runOutfitFrames);
            runner.SetupKnockOut(dizzyFrames, dizzyOutfitFrames, faintFrames, faintOutfitFrames, downFrames,
                downOutfitFrames);
            runner.DizzySpeed = dizzySpeed;
            // Fishing: the tackle in front of the outfit (lit, by depth), the catch's sparkles and "+1" unlit
            // above every igloo and penguin.
            var tackle = new GameObject("Tackle").AddComponent<SpriteRenderer>();
            tackle.transform.SetParent(penguin.transform, false);
            tackle.sharedMaterial = spriteMaterial;
            var effect = new GameObject("Fish Effect").AddComponent<SpriteRenderer>();
            effect.transform.SetParent(penguin.transform, false);
            effect.sharedMaterial = effectMaterial;
            effect.sortingOrder = NameOrder - 1;
            runner.SetupFishing(tackle, effect, fishCatchFrames, fishCatchOutfitFrames, fishCatchTackle,
                fishCatchEffect, fishCatchEffectFlipped, fishMissFrames, fishMissOutfitFrames, fishMissTackle);
            var otherDomes = new List<Vector2>();
            for (int other = 0; other < count; other++)
                if (other != i)
                    otherDomes.Add((iglooPositions[other] - iglooPosition) / unitsPerPixel);
            var route = IglooGround.PathToDoor((foot - iglooPosition - domeCenter) / unitsPerPixel, otherDomes);
            runner.SetHome(MakeRoute(route, iglooPosition + domeCenter, null, door, foot));
            int visitor = i;
            runner.GuestArrived += (door, seconds) => PlayAction(visitor, door, seconds);
            runners.Add(runner);

            // Each penguin gets its own random breath phase, speed and clip order.
            var idle = penguin.gameObject.AddComponent<PenguinIdle>();
            idle.Setup(penguin, clothes, runner, idleFrames, idleOutfitFrames,
                unchecked(System.Environment.TickCount * 31 + i * 7919), idleAnimation);
            idles.Add(idle);
        }
#if UNITY_EDITOR
        if (!Application.isPlaying)
            foreach (Transform part in generated.GetComponentsInChildren<Transform>(true))
                part.gameObject.hideFlags = HideFlags.DontSave;
#endif
        ApplyLighting();
    }

    // The penguin of `visitor` went in through `door`: that igloo plays the visit's action for the stay.
    private void PlayAction(int visitor, IglooDoor door, float seconds)
    {
        int igloo = doors.IndexOf(door);
        if (igloo < 0 || igloo >= actions.Count || actions[igloo] == null)
            return;
        if (!visitActions.TryGetValue(visitor, out IglooAction.Kind action))
            action = IglooAction.Kind.KnockOut;
        actions[igloo].Play(seconds, action);
    }

    // Igloo art point (texture fraction, y up) in the igloo sprite's local units.
    private Vector2 IglooPoint(Vector2 fraction) => IglooArtPoint(igloo, fraction);

    public static Vector2 IglooArtPoint(Sprite sprite, Vector2 fraction) =>
        (Vector2.Scale(fraction, sprite.rect.size) - sprite.pivot) / sprite.pixelsPerUnit;

    // Igloo art canvas pixels (IglooGround's units) per layout unit.
    public static float IglooCanvasPixelsPerUnit(Sprite sprite) =>
        IglooGround.CanvasPixels * sprite.pixelsPerUnit / sprite.rect.width;

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
        for (int i = 0; i < windowLights.Count; i++)
        {
            Light2D window = windowLights[i];
            if (window == null)
                continue;
            float lamp = i < actions.Count && actions[i] != null ? actions[i].Lamp : 1f;
            window.intensity = WindowLightIntensity * darkness * lamp;
            window.enabled = darkness * lamp > 0.001f;
            Light2D heal = i < healLights.Count ? healLights[i] : null;
            if (heal == null)
                continue;
            float glow = i < actions.Count && actions[i] != null ? actions[i].Glow : 0f;
            heal.intensity = HealLightIntensity * darkness * glow;
            heal.enabled = darkness * glow > 0.001f;
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
