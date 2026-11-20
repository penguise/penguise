using System;
using System.Collections.Generic;
using UnityEngine;

// Runs one penguin along ground routes. Home route: from its slot into its own igloo and back out. In:
// plays the run cycle, opens the door ahead of it, shows it only through the doorway once it is past the
// door plane, darkens it in the tunnel, hides it at the end and closes the door behind it. Out: the same
// route backwards; the door opens as it sets off from the tunnel and closes once it is clear of the leaf,
// and it stops on its slot facing the middle again. Either run can be reversed midway.
// Visit: from its own tunnel (or its slot) along a route around the domes into another igloo's tunnel,
// a wait inside, then back into its own igloo (the same route when it set off from there). Each door opens as the penguin comes
// near it or sets off inside it, and closes once the penguin is clear of the leaf or has gone in.
// In Edit mode it ticks from EditorApplication.update, so previews animate while the Editor is unfocused.
[ExecuteAlways]
public sealed class PenguinRunner : MonoBehaviour
{
    // Run art: 16 frames per 0.38 s cycle, feet planted at 0.5512 units/s; frames advance with distance.
    public const int RunFrames = 16;
    public const float CycleDistance = 0.5512f * 0.38f;
    public const float DoorSeconds = 0.45f;
    private static readonly Color InsideTint = new Color(0.22f, 0.25f, 0.32f);

    // Visiting: on the way to another igloo; Guest: inside it; Returning: on the way back.
    public enum State { Idle, RunningIn, Inside, RunningOut, Visiting, Guest, Returning }

    // A ground route in the parent's local units. A door at either end belongs to the tunnel that end of
    // the route lies in; its Clear distance is the point in front of the door that the open leaf does not
    // reach.
    [Serializable]
    public sealed class Route
    {
        public Vector2[] Points;
        public float[] Distances;   // ground distance of each point from the start
        public bool[] Masked;       // past a doorway plane: only visible through the opening
        public float[] Shades;      // 0 at a doorway, 1 deep in a tunnel
        public IglooDoor StartDoor;
        public float StartClear;
        public IglooDoor EndDoor;
        public float EndClear;

        public float Length => Distances != null && Distances.Length > 0 ? Distances[Distances.Length - 1] : 0f;
        public bool Valid => Points != null && Points.Length >= 2;
    }

    private struct DoorDrive
    {
        public float Timer;
        public bool Opening, Closing, Released;
    }

    [SerializeField, HideInInspector] private SpriteRenderer body;
    [SerializeField, HideInInspector] private SpriteRenderer clothes;
    [SerializeField, HideInInspector] private Renderer label;
    [SerializeField, HideInInspector] private Sprite idleBody, idleClothes;
    [SerializeField, HideInInspector] private Sprite[] runBody, runClothes;
    [SerializeField, HideInInspector] private Color clothesColor = Color.white;
    [SerializeField, HideInInspector] private bool idleFlip;
    [SerializeField, HideInInspector] private Route home;

    private Route route, returnRoute;
    private State state;
    private float travelled, stride, speed, waitLeft;
    private bool waitingForDoor;
    private DoorDrive startDrive, endDrive;
    private double lastTick;

    public State Current => state;
    public bool IsInside => state == State.Inside;
    public bool IsRunning => state == State.RunningIn || state == State.RunningOut
        || state == State.Visiting || state == State.Returning;
    public bool IsVisiting => state == State.Visiting || state == State.Guest || state == State.Returning;
    // Home route: own door, its length and clear distance, and its points (slot first).
    public IglooDoor Door => home?.EndDoor;
    public float PathLength => home?.Length ?? 0f;
    public float DoorClearDistance => home?.EndClear ?? 0f;
    public IReadOnlyList<Vector2> Path => home?.Points;
    // The visit under way (null otherwise) and the distance travelled along it.
    public Route VisitRoute => IsVisiting ? route : null;
    public float Travelled => travelled;
    // Hidden at the far end of a tunnel, waiting for its door to open before walking out.
    public bool WaitingForDoor => waitingForDoor;
    // Depth sorting shared with the igloos: lower on screen (nearer the camera) draws on top.
    // Even orders; a penguin's outfit takes the next odd one. About 40..760 on this background: above the
    // background (-10) and below the names (GameLayout.NameOrder).
    public static int DepthOrder(float y) => 400 - 2 * Mathf.RoundToInt(y * 30f);

    public void Setup(SpriteRenderer body, SpriteRenderer clothes, Renderer label, Sprite[] runBody,
        Sprite[] runClothes)
    {
        this.body = body;
        this.clothes = clothes;
        this.label = label;
        idleBody = body.sprite;
        idleClothes = clothes.sprite;
        this.runBody = runBody;
        this.runClothes = runClothes;
        clothesColor = clothes.color;
        idleFlip = body.flipX;
    }

    // Home route from the penguin's feet on its slot into its own tunnel (EndDoor is its own door).
    public void SetHome(Route path)
    {
        home = path;
        route = path;
        state = State.Idle;
        ShowIdle();
    }

    // From the slot (or turning back from a run out) into the igloo.
    public void RunIntoIgloo(float unitsPerSecond)
    {
        if (state == State.Idle)
            travelled = 0f;
        else if (state != State.RunningOut)
            return;
        Begin(home, State.RunningIn, unitsPerSecond);
    }

    // From inside the igloo (or turning back from a run in) out to the slot.
    public void RunOutOfIgloo(float unitsPerSecond)
    {
        if (state == State.Inside)
            travelled = PathLength;
        else if (state != State.RunningIn)
            return;
        Begin(home, State.RunningOut, unitsPerSecond);
    }

    // Visit along `visit` (from the own tunnel when inside, from the slot when idle there), stay inside
    // for `waitSeconds`, then come back along `back` walked backwards (a route from the own tunnel into
    // the same igloo, so the penguin ends inside its own igloo), or along `visit` when `back` is null.
    // False if the penguin is busy or the routes do not start where it is.
    public bool Visit(Route visit, Route back, float unitsPerSecond, float waitSeconds)
    {
        if (visit == null || !visit.Valid || visit.EndDoor == null)
            return false;
        if (state == State.Inside ? visit.StartDoor != Door : state != State.Idle || visit.StartDoor != null)
            return false;
        if (back != null && (!back.Valid || back.EndDoor != visit.EndDoor))
            return false;
        returnRoute = back;
        waitLeft = Mathf.Max(0f, waitSeconds);
        travelled = 0f;
        Begin(visit, State.Visiting, unitsPerSecond);
        return true;
    }

    private void Begin(Route next, State run, float unitsPerSecond)
    {
        if (next == null || !next.Valid)
            return;
        if (next != route)
        {
            // Door drives follow their doors to the new route; a door left mid-close finishes at once.
            DoorDrive oldStart = startDrive, oldEnd = endDrive;
            startDrive = Carry(route, oldStart, oldEnd, next.StartDoor);
            endDrive = Carry(route, oldStart, oldEnd, next.EndDoor);
            Release(route?.StartDoor, oldStart, next);
            Release(route?.EndDoor, oldEnd, next);
            route = next;
        }
        state = run;
        speed = Mathf.Max(0.01f, unitsPerSecond);
        startDrive.Opening = endDrive.Opening = false;
        startDrive.Released = endDrive.Released = false;
        // Setting off from the far end of a tunnel: wait there, hidden, until its door is fully open, then
        // walk out of the dark tunnel.
        bool fromStart = run == State.RunningIn || run == State.Visiting;
        IglooDoor exitDoor = fromStart ? route.StartDoor : route.EndDoor;
        waitingForDoor = exitDoor != null && exitDoor.Open < 0.999f
            && (fromStart ? route.Masked[0] && travelled <= 1e-4f
                : route.Masked[route.Masked.Length - 1] && travelled >= route.Length - 1e-4f);
        SetVisible(!waitingForDoor);
        lastTick = Now;
        Tick(0f);
#if UNITY_EDITOR
        SetEditorTicking(!Application.isPlaying);
#endif
    }

    private static DoorDrive Carry(Route old, DoorDrive oldStart, DoorDrive oldEnd, IglooDoor door)
    {
        if (door != null && old != null && old.StartDoor == door)
            return oldStart;
        if (door != null && old != null && old.EndDoor == door)
            return oldEnd;
        return default;
    }

    private static void Release(IglooDoor door, DoorDrive drive, Route next)
    {
        if (door != null && drive.Closing && door != next.StartDoor && door != next.EndDoor)
            door.Open = 0f;
    }

    // Advances the run (or the wait inside) by `seconds`; Update calls it with the frame time.
    public void Tick(float seconds)
    {
        startDrive.Timer += seconds;
        endDrive.Timer += seconds;
        if (route == null || !route.Valid)
            return;
        bool forward = state == State.RunningIn || state == State.Visiting;
        bool backward = state == State.RunningOut || state == State.Returning;
        if (waitingForDoor && (forward || backward))
        {
            IglooDoor exitDoor = forward ? route.StartDoor : route.EndDoor;
            if (exitDoor == null || exitDoor.Open >= 0.999f)
                waitingForDoor = false;
            else
            {
                DriveDoor(route.EndDoor, ref endDrive, true, route.EndClear, forward);
                DriveDoor(route.StartDoor, ref startDrive, false, route.StartClear, forward);
                ApplyDoor(route.StartDoor, ref startDrive);
                ApplyDoor(route.EndDoor, ref endDrive);
                return;
            }
        }
        if (forward)
        {
            float next = Mathf.Min(travelled + speed * seconds, route.Length);
            stride += next - travelled;
            travelled = next;
        }
        else if (backward)
        {
            float next = Mathf.Max(travelled - speed * seconds, 0f);
            stride += travelled - next;
            travelled = next;
        }
        if (forward || backward)
        {
            DriveDoor(route.EndDoor, ref endDrive, true, route.EndClear, forward);
            DriveDoor(route.StartDoor, ref startDrive, false, route.StartClear, forward);
            Place(travelled, forward);
            if (forward && travelled >= route.Length)
                Arrive(state == State.RunningIn ? State.Inside : State.Guest, ref endDrive);
            else if (backward && travelled <= 0f)
            {
                if (route.StartDoor != null)
                    Arrive(State.Inside, ref startDrive);
                else
                {
                    state = State.Idle;
                    route = home;
                    ShowIdle();
                }
            }
        }
        else if (state == State.Guest)
        {
            waitLeft -= seconds;
            if (waitLeft <= 0f)
            {
                // Both routes end deep in the same tunnel, so switching there is seamless.
                Route back = returnRoute ?? route;
                returnRoute = null;
                if (back != route)
                    travelled = back.Length;
                Begin(back, State.Returning, speed);
            }
        }
        ApplyDoor(route.StartDoor, ref startDrive);
        ApplyDoor(route.EndDoor, ref endDrive);
    }

    // Hidden inside a tunnel; the door there closes behind the penguin.
    private void Arrive(State inside, ref DoorDrive drive)
    {
        state = inside;
        SetVisible(false);
        StartDoor(ref drive, false);
    }

    // A door at one end of the route (`atEnd`: the end the route runs into). Moving toward it, it opens as
    // the penguin comes within a door's opening time of the clear point; moving away, it stays open while
    // the penguin is still between the door and the clear point and closes once it is past.
    private void DriveDoor(IglooDoor door, ref DoorDrive drive, bool atEnd, float clear, bool forward)
    {
        if (door == null)
            return;
        float beyond = atEnd ? travelled - clear : clear - travelled;
        if (forward == atEnd)
        {
            if (!drive.Opening && beyond > -(speed * DoorSeconds + 0.15f))
                StartDoor(ref drive, true);
        }
        else if (!drive.Released)
        {
            if (beyond > 0f)
            {
                if (!drive.Opening)
                    StartDoor(ref drive, true);
            }
            else
            {
                drive.Released = true;
                StartDoor(ref drive, false);
            }
        }
    }

    private static void StartDoor(ref DoorDrive drive, bool open)
    {
        if (open ? drive.Opening : drive.Closing)
            return;
        drive.Opening = open;
        drive.Closing = !open;
        drive.Timer = 0f;
    }

    // Opens over DoorSeconds, never closing a door that is already open, and closes the same way.
    private static void ApplyDoor(IglooDoor door, ref DoorDrive drive)
    {
        if (door == null)
            return;
        float eased = Mathf.SmoothStep(0f, 1f, drive.Timer / DoorSeconds);
        if (drive.Opening)
            door.Open = Mathf.Max(door.Open, eased);
        else if (drive.Closing)
        {
            door.Open = Mathf.Min(door.Open, 1f - eased);
            drive.Closing = drive.Timer < DoorSeconds;
        }
    }

    private static double Now =>
#if UNITY_EDITOR
        UnityEditor.EditorApplication.timeSinceStartup;
#else
        Time.realtimeSinceStartupAsDouble;
#endif

    private bool Active => IsRunning || state == State.Guest || startDrive.Closing || endDrive.Closing;

    private void Update()
    {
        if (Application.isPlaying && Active)
            Tick(Time.deltaTime);
    }

#if UNITY_EDITOR
    private bool editorTicking;

    private void SetEditorTicking(bool on)
    {
        if (on == editorTicking)
            return;
        editorTicking = on;
        if (on)
            UnityEditor.EditorApplication.update += EditorTick;
        else
            UnityEditor.EditorApplication.update -= EditorTick;
    }

    private void EditorTick()
    {
        if (this == null || Application.isPlaying || !Active)
        {
            SetEditorTicking(false);
            return;
        }
        double now = Now;
        Tick(Mathf.Min((float)(now - lastTick), 0.1f));
        lastTick = now;
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        UnityEditor.SceneView.RepaintAll();
    }

    private void OnDisable() => SetEditorTicking(false);
#endif

    // `forward`: moving along the route (toward its end) rather than back toward its start.
    private void Place(float distance, bool forward)
    {
        float[] distances = route.Distances;
        Vector2[] points = route.Points;
        int i = 1;
        while (i < distances.Length - 1 && distances[i] < distance)
            i++;
        float span = distances[i] - distances[i - 1];
        float t = span > 0f ? Mathf.Clamp01((distance - distances[i - 1]) / span) : 1f;
        Vector2 position = Vector2.Lerp(points[i - 1], points[i], t);
        float dx = (points[i].x - points[i - 1].x) * (forward ? 1f : -1f);
        bool flip = Mathf.Abs(dx) > 1e-4f ? dx < 0f : body.flipX;
        int frame = Mathf.FloorToInt(stride / CycleDistance * RunFrames) % RunFrames;
        // A segment between a masked and an open point counts as outside, whichever way it is walked.
        bool inside = route.Masked[i] && route.Masked[i - 1]
            || route.Masked[i] && t >= 1f || route.Masked[i - 1] && t <= 0f;
        float shade = Mathf.Lerp(route.Shades[i - 1], route.Shades[i], t);
        Show(position, runBody[frame], runClothes[frame], flip, inside, shade);
        // The doorway mask draws the penguin in front of the igloo, over a closed or half-open leaf too, so
        // in a tunnel it shows only once that tunnel's door is fully open (e.g. setting off from inside).
        IglooDoor tunnelDoor = distance > route.Length * 0.5f ? route.EndDoor : route.StartDoor;
        body.enabled = clothes.enabled = !(inside && tunnelDoor != null && tunnelDoor.Open < 0.999f);
    }

    private void Show(Vector2 position, Sprite bodySprite, Sprite clothesSprite, bool flip, bool inside, float shade)
    {
        transform.localPosition = new Vector3(position.x, position.y, transform.localPosition.z);
        var interaction = inside ? SpriteMaskInteraction.VisibleInsideMask : SpriteMaskInteraction.None;
        if (body.maskInteraction != interaction || clothes.maskInteraction != interaction)
        {
            body.maskInteraction = clothes.maskInteraction = interaction;
            // Unity 6000.6: after a mask interaction change the outfit kept drawing with the body's texture
            // (white parts in the player color, no hat or sweater) until its sprite was reassigned; later
            // frame changes did not fix it. Clearing the sprites makes the assignment below rebuild them.
            body.sprite = null;
            clothes.sprite = null;
        }
        body.sprite = bodySprite;
        clothes.sprite = clothesSprite;
        body.flipX = clothes.flipX = flip;
        Color tint = Color.Lerp(Color.white, InsideTint, shade);
        body.color = tint;
        clothes.color = clothesColor * tint;
        int order = DepthOrder(position.y);
        body.sortingOrder = order;
        clothes.sortingOrder = order + 1;
        if (label != null)
            label.enabled = !inside;
    }

    // Standing on the slot: idle art, facing the middle.
    private void ShowIdle()
    {
        Show(home != null && home.Valid ? home.Points[0] : (Vector2)transform.localPosition,
            idleBody, idleClothes, idleFlip, false, 0f);
        stride = 0f;
    }

    private void SetVisible(bool visible)
    {
        body.enabled = clothes.enabled = visible;
        if (label != null)
            label.enabled = visible;
    }
}
