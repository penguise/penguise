using UnityEngine;

// Idle animation for a penguin standing on its slot. Every clip is the shared breathing loop, optionally
// with a fidget on top, and lasts a whole number of breaths starting and ending in the same pose, so clips
// chain without a pop. Each penguin starts at a random point of the breath, plays at its own slightly
// different speed and picks its next clip at random, so the group never moves in sync. Plays only while
// the penguin's PenguinRunner is idle on its slot. In Edit mode it ticks from EditorApplication.update.
[ExecuteAlways]
public sealed class PenguinIdle : MonoBehaviour
{
    public const float FramesPerSecond = 15f;
    public const int FramesPerBreath = 36;   // one 2.4 s breath

    public enum Clip { Breathe, Blink, DoubleBlink, Flap, Stomp, Look }

    // Frame counts in sheet order (the idle sheets hold the clips back to back in this order).
    private static readonly int[] ClipFrames =
    {
        FramesPerBreath, FramesPerBreath, FramesPerBreath, FramesPerBreath, FramesPerBreath, 2 * FramesPerBreath,
    };
    // Relative odds of each clip coming next: mostly plain breathing and blinks, now and then a fidget.
    private static readonly int[] ClipWeights = { 5, 3, 1, 1, 1, 1 };
    public const float MinSpeed = 0.92f, MaxSpeed = 1.08f;

    public static int TotalFrames
    {
        get
        {
            int total = 0;
            foreach (int frames in ClipFrames)
                total += frames;
            return total;
        }
    }

    public static int FirstFrame(Clip clip)
    {
        int first = 0;
        for (int i = 0; i < (int)clip; i++)
            first += ClipFrames[i];
        return first;
    }

    public static int FrameCount(Clip clip) => ClipFrames[(int)clip];
    public static float Duration(Clip clip) => ClipFrames[(int)clip] / FramesPerSecond;

    [SerializeField, HideInInspector] private SpriteRenderer body;
    [SerializeField, HideInInspector] private SpriteRenderer clothes;
    [SerializeField, HideInInspector] private PenguinRunner runner;
    [SerializeField, HideInInspector] private Sprite[] bodyFrames, clothesFrames;
    [SerializeField, HideInInspector] private Sprite restBody, restClothes;
    [SerializeField, HideInInspector] private int seed;
    [SerializeField, HideInInspector] private bool playing = true;

    private System.Random random;
    private Clip clip;
    private float time, speed = 1f;
    private bool active;

    public Clip CurrentClip => clip;
    public float ClipTime => time;
    public float Speed => speed;
    // Whether the idle frames are showing (false while running, inside the igloo, or switched off).
    public bool IsActive => active;

    public bool Playing
    {
        get => playing;
        set
        {
            if (playing == value)
                return;
            playing = value;
            if (!playing && active)
            {
                active = false;
                body.sprite = restBody;
                clothes.sprite = restClothes;
            }
            UpdateEditorTicking();
        }
    }

    public void Setup(SpriteRenderer body, SpriteRenderer clothes, PenguinRunner runner, Sprite[] bodyFrames,
        Sprite[] clothesFrames, int seed, bool playing)
    {
        this.body = body;
        this.clothes = clothes;
        this.runner = runner;
        this.bodyFrames = bodyFrames;
        this.clothesFrames = clothesFrames;
        restBody = body.sprite;
        restClothes = clothes.sprite;
        this.seed = seed;
        this.playing = playing;
        random = null;
        active = false;
        Advance(0f);
        UpdateEditorTicking();
    }

    // Advances the idle by `seconds`; Update calls it with the frame time.
    public void Advance(float seconds)
    {
        bool standing = playing && runner != null && runner.Current == PenguinRunner.State.Idle;
        if (!standing)
        {
            // The runner owns the sprites while it moves; the idle restarts from the top of a breath.
            active = false;
            return;
        }
        if (random == null)
        {
            random = new System.Random(seed);
            speed = Mathf.Lerp(MinSpeed, MaxSpeed, (float)random.NextDouble());
            clip = Clip.Breathe;
            time = (float)random.NextDouble() * Duration(Clip.Breathe);
        }
        else if (!active)
        {
            clip = Clip.Breathe;
            time = 0f;
        }
        else
        {
            time += seconds * speed;
            while (time >= Duration(clip))
            {
                time -= Duration(clip);
                clip = NextClip();
            }
        }
        active = true;
        int frame = FirstFrame(clip) + Mathf.Min(FrameCount(clip) - 1, Mathf.FloorToInt(time * FramesPerSecond));
        if (body.sprite != bodyFrames[frame])
        {
            body.sprite = bodyFrames[frame];
            clothes.sprite = clothesFrames[frame];
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }
    }

    private Clip NextClip()
    {
        int total = 0;
        foreach (int weight in ClipWeights)
            total += weight;
        int pick = random.Next(total);
        for (int i = 0; i < ClipWeights.Length; i++)
        {
            pick -= ClipWeights[i];
            if (pick < 0)
                return (Clip)i;
        }
        return Clip.Breathe;
    }

    private void Update()
    {
        if (Application.isPlaying)
            Advance(Time.deltaTime);
    }

#if UNITY_EDITOR
    private bool editorTicking;
    private double lastTick;
    private static double Now => UnityEditor.EditorApplication.timeSinceStartup;

    private void OnEnable() => UpdateEditorTicking();
    private void OnDisable() => SetEditorTicking(false);

    private void UpdateEditorTicking() => SetEditorTicking(isActiveAndEnabled && playing && !Application.isPlaying);

    private void SetEditorTicking(bool on)
    {
        if (on == editorTicking)
            return;
        editorTicking = on;
        lastTick = Now;
        if (on)
            UnityEditor.EditorApplication.update += EditorTick;
        else
            UnityEditor.EditorApplication.update -= EditorTick;
    }

    private void EditorTick()
    {
        if (this == null || Application.isPlaying)
        {
            SetEditorTicking(false);
            return;
        }
        double now = Now;
        Advance(Mathf.Min((float)(now - lastTick), 0.1f));
        lastTick = now;
    }
#else
    private void UpdateEditorTicking() { }
#endif
}
