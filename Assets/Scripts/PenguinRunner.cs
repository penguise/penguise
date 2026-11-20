using System.Collections.Generic;
using UnityEngine;

// Runs one penguin along a ground path into its igloo and back out to its slot. In: plays the run cycle,
// opens the door ahead of it, shows it only through the doorway once it is past the door plane, darkens it
// in the tunnel, hides it at the end and closes the door behind it. Out: the same path backwards; the door
// opens as it sets off from the tunnel and closes once it is clear of the leaf, and it stops on its slot
// facing the middle again. Either run can be reversed midway. In Edit mode it ticks from
// EditorApplication.update, so the Inspector preview animates even while the Editor window is not focused.
[ExecuteAlways]
public sealed class PenguinRunner : MonoBehaviour
{
    // Run art: 16 frames per 0.38 s cycle, feet planted at 0.5512 units/s; frames advance with distance.
    public const int RunFrames = 16;
    public const float CycleDistance = 0.5512f * 0.38f;
    public const float DoorSeconds = 0.45f;
    private static readonly Color InsideTint = new Color(0.22f, 0.25f, 0.32f);

    public enum State { Idle, RunningIn, Inside, RunningOut }

    [SerializeField, HideInInspector] private SpriteRenderer body;
    [SerializeField, HideInInspector] private SpriteRenderer clothes;
    [SerializeField, HideInInspector] private Renderer label;
    [SerializeField, HideInInspector] private Sprite idleBody, idleClothes;
    [SerializeField, HideInInspector] private Sprite[] runBody, runClothes;
    [SerializeField, HideInInspector] private IglooDoor door;
    [SerializeField, HideInInspector] private Color clothesColor = Color.white;
    [SerializeField, HideInInspector] private bool idleFlip;
    [SerializeField, HideInInspector] private Vector2[] points;
    [SerializeField, HideInInspector] private float[] distances;
    [SerializeField, HideInInspector] private bool[] masked;
    [SerializeField, HideInInspector] private float[] shades;
    // Path distance in front of the door, clear of the open leaf's swing; the door closes once an
    // outgoing penguin is past it.
    [SerializeField, HideInInspector] private float doorClear;

    private State state;
    private float travelled, stride, speed, doorTimer;
    private bool doorClosing;
    // Run out: the penguin is past the leaf and has let the door close; it is not reopened.
    private bool doorReleased;
    private double lastTick;

    public State Current => state;
    public bool IsInside => state == State.Inside;
    public bool IsRunning => state == State.RunningIn || state == State.RunningOut;
    public IglooDoor Door => door;
    public float PathLength => distances != null && distances.Length > 0 ? distances[distances.Length - 1] : 0f;
    public float DoorClearDistance => doorClear;
    public IReadOnlyList<Vector2> Path => points;

    // Depth sorting shared with the igloos: lower on screen (nearer the camera) draws on top.
    // Even orders; a penguin's outfit takes the next odd one. About 40..760 on this background: above the
    // background (-10) and below the names (GameLayout.NameOrder).
    public static int DepthOrder(float y) => 400 - 2 * Mathf.RoundToInt(y * 30f);

    public void Setup(SpriteRenderer body, SpriteRenderer clothes, Renderer label, Sprite[] runBody,
        Sprite[] runClothes, IglooDoor door)
    {
        this.body = body;
        this.clothes = clothes;
        this.label = label;
        idleBody = body.sprite;
        idleClothes = clothes.sprite;
        this.runBody = runBody;
        this.runClothes = runClothes;
        this.door = door;
        clothesColor = clothes.color;
        idleFlip = body.flipX;
        ShowIdle();
    }

    // Path in the parent's local units, starting at the penguin's feet. `lengths` holds each point's
    // distance along the ground from the start, in the same units; `clearIndex` is the point in front of
    // the door that the open leaf does not reach.
    public void SetPath(Vector2[] path, float[] lengths, bool[] pathMasked, float[] pathShades, int clearIndex)
    {
        points = path;
        distances = lengths;
        masked = pathMasked;
        shades = pathShades;
        doorClear = lengths[Mathf.Clamp(clearIndex, 0, lengths.Length - 1)];
    }

    // From the slot (or turning back from a run out) into the igloo.
    public void RunIntoIgloo(float unitsPerSecond)
    {
        if (state == State.Idle)
            travelled = 0f;
        else if (state != State.RunningOut)
            return;
        Begin(State.RunningIn, unitsPerSecond);
    }

    // From inside the igloo (or turning back from a run in) out to the slot.
    public void RunOutOfIgloo(float unitsPerSecond)
    {
        if (state == State.Inside)
            travelled = PathLength;
        else if (state != State.RunningIn)
            return;
        Begin(State.RunningOut, unitsPerSecond);
    }

    private void Begin(State run, float unitsPerSecond)
    {
        if (points == null || points.Length < 2)
            return;
        state = run;
        speed = Mathf.Max(0.01f, unitsPerSecond);
        doorTimer = 0f;
        // An outgoing penguin already past the leaf leaves the door alone (closed or finishing closing).
        doorReleased = run == State.RunningOut && travelled <= doorClear;
        if (!doorReleased)
            doorClosing = false;
        else if (!doorClosing && door != null && door.Open > 0f)
            CloseDoor();
        SetVisible(true);
        lastTick = Now;
        Tick(0f);
#if UNITY_EDITOR
        SetEditorTicking(!Application.isPlaying);
#endif
    }

    // Advances the run by `seconds`; Update calls it with the frame time.
    public void Tick(float seconds)
    {
        doorTimer += seconds;
        if (state == State.RunningIn)
        {
            float next = Mathf.Min(travelled + speed * seconds, PathLength);
            stride += next - travelled;
            travelled = next;
            OpenDoor();
            Place(travelled, true);
            if (travelled >= PathLength)
            {
                state = State.Inside;
                SetVisible(false);
                CloseDoor();
            }
        }
        else if (state == State.RunningOut)
        {
            float next = Mathf.Max(travelled - speed * seconds, 0f);
            stride += travelled - next;
            travelled = next;
            if (!doorReleased)
                OpenDoor();
            Place(travelled, false);
            if (!doorReleased && travelled <= doorClear)
            {
                doorReleased = true;
                CloseDoor();
            }
            if (travelled <= 0f)
            {
                state = State.Idle;
                ShowIdle();
            }
        }
        if (doorClosing && door != null)
        {
            door.Open = Mathf.Min(door.Open, 1f - Mathf.SmoothStep(0f, 1f, doorTimer / DoorSeconds));
            doorClosing = doorTimer < DoorSeconds;
        }
    }

    // Opens over DoorSeconds from the start of the run, never closing a door that is already open.
    private void OpenDoor()
    {
        if (door != null)
            door.Open = Mathf.Max(door.Open, Mathf.SmoothStep(0f, 1f, doorTimer / DoorSeconds));
    }

    private void CloseDoor()
    {
        doorClosing = true;
        doorTimer = 0f;
    }

    private static double Now =>
#if UNITY_EDITOR
        UnityEditor.EditorApplication.timeSinceStartup;
#else
        Time.realtimeSinceStartupAsDouble;
#endif

    private bool Active => IsRunning || doorClosing;

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

    // `forward`: moving along the path (into the igloo) rather than back toward the slot.
    private void Place(float distance, bool forward)
    {
        int i = 1;
        while (i < distances.Length - 1 && distances[i] < distance)
            i++;
        float span = distances[i] - distances[i - 1];
        float t = span > 0f ? Mathf.Clamp01((distance - distances[i - 1]) / span) : 1f;
        Vector2 position = Vector2.Lerp(points[i - 1], points[i], t);
        float dx = (points[i].x - points[i - 1].x) * (forward ? 1f : -1f);
        bool flip = Mathf.Abs(dx) > 1e-4f ? dx < 0f : body.flipX;
        int frame = Mathf.FloorToInt(stride / CycleDistance * RunFrames) % RunFrames;
        bool inside = masked[i] && (masked[i - 1] || t >= 1f);
        float shade = Mathf.Lerp(shades[i - 1], shades[i], t);
        Show(position, runBody[frame], runClothes[frame], flip, inside, shade);
    }

    private void Show(Vector2 position, Sprite bodySprite, Sprite clothesSprite, bool flip, bool inside, float shade)
    {
        transform.localPosition = new Vector3(position.x, position.y, transform.localPosition.z);
        body.sprite = bodySprite;
        clothes.sprite = clothesSprite;
        body.flipX = clothes.flipX = flip;
        var interaction = inside ? SpriteMaskInteraction.VisibleInsideMask : SpriteMaskInteraction.None;
        body.maskInteraction = clothes.maskInteraction = interaction;
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
        Show(points != null && points.Length > 0 ? points[0] : (Vector2)transform.localPosition,
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
