using UnityEngine;

// What the outside sees while a visitor is inside an igloo: the window lamp flickers off and on and the
// igloo shakes in bursts about its base, then it all settles. The timeline is authored for a 4 s stay
// and stretched to the actual one (Tools/ArtGen/igloo/preview_action.py draws the same curves).
// The lamp-off pane shows over the window as the lamp goes out; GameLayout scales the window light by Lamp.
public sealed class IglooAction : MonoBehaviour
{
    private const float AuthoredSeconds = 4f;
    // Each lamp switch fades over this long, so the lamp flickers rather than blinks.
    private const float Fade = 0.05f;
    // (time in the authored stay, lit) switch points.
    private static readonly (float At, float Lit)[] LampSwitches =
    {
        (0.45f, 0), (0.55f, 1), (0.62f, 0), (0.70f, 1), (0.95f, 0), (1.35f, 1), (1.42f, 0),
        (2.05f, 1), (2.12f, 0), (2.18f, 1), (2.25f, 0), (3.05f, 1), (3.12f, 0), (3.30f, 1),
    };
    // (start, length, strength) in the authored stay; each is a decaying wobble.
    private static readonly (float Start, float Length, float Strength)[] ShakeBursts =
    {
        (0.95f, 0.45f, 1f), (1.5f, 0.55f, 0.8f), (2.3f, 0.6f, 1.15f), (3.0f, 0.25f, 0.45f),
    };
    // Peak tilt (degrees), vertical squash (the dome bulges sideways by half of it) and lift (igloo art
    // canvas pixels) at strength 1, and the wobble frequency.
    private const float TiltDegrees = 2.4f;
    private const float Squash = 0.035f;
    private const float HopCanvasPixels = 7f;
    private const float WobbleHz = 7.5f;

    private Transform body;
    private SpriteRenderer darkPane;
    private Vector3 restPosition;
    private float hopUnits;
    private float duration, elapsed;
    private bool playing;

    // Lamp brightness, 1 lit, 0 off.
    public float Lamp { get; private set; } = 1f;
    public bool Playing => playing;

    // `body` is the igloo's transform (pivot at the base); `darkPane` the lamp-off pane over the window.
    public void Setup(Transform body, SpriteRenderer darkPane, float canvasPixelsPerUnit)
    {
        this.body = body;
        this.darkPane = darkPane;
        restPosition = body.localPosition;
        hopUnits = HopCanvasPixels / canvasPixelsPerUnit;
        Apply(1f, 0f, 0f, 0f);
    }

    // Plays the action over a stay of `seconds` (restarts if already playing).
    public void Play(float seconds)
    {
        duration = Mathf.Max(0.01f, seconds);
        elapsed = 0f;
        playing = true;
        Tick(0f);
    }

    public void Stop()
    {
        playing = false;
        Apply(1f, 0f, 0f, 0f);
    }

    private void Update()
    {
        if (playing)
            Tick(Time.deltaTime);
    }

    public void Tick(float seconds)
    {
        if (!playing)
            return;
        elapsed += seconds;
        if (elapsed >= duration)
        {
            Stop();
            return;
        }
        Sample(elapsed, duration, out float lamp, out float tilt, out float squash, out float lift);
        Apply(lamp, tilt, squash, lift * hopUnits);
    }

    // Lamp (0..1), tilt (degrees), squash and lift (fraction of the peak hop) at `time` into a `stay`.
    public static void Sample(float time, float stay, out float lamp, out float tilt, out float squash, out float lift)
    {
        float k = stay / AuthoredSeconds;
        lamp = 1f;
        foreach (var (at, lit) in LampSwitches)
        {
            float start = at * k;
            if (time >= start)
                lamp = time >= start + Fade ? lit : lamp + (lit - lamp) * (time - start) / Fade;
        }
        tilt = squash = lift = 0f;
        foreach (var (start, length, strength) in ShakeBursts)
        {
            float u = (time - start * k) / (length * k);
            if (u < 0f || u > 1f)
                continue;
            float envelope = strength * Mathf.Sin(Mathf.PI * Mathf.Min(1f, u * 3f) / 2f) * Mathf.Pow(1f - u, 1.3f);
            float phase = 2f * Mathf.PI * WobbleHz * (time - start * k);
            tilt += TiltDegrees * envelope * Mathf.Sin(phase);
            squash += Squash * envelope * Mathf.Abs(Mathf.Sin(phase));
            lift += envelope * Mathf.Abs(Mathf.Cos(phase));
        }
    }

    private void Apply(float lamp, float tilt, float squash, float lift)
    {
        Lamp = lamp;
        if (body != null)
        {
            body.localPosition = restPosition + Vector3.up * lift;
            body.localRotation = Quaternion.Euler(0f, 0f, tilt);
            body.localScale = new Vector3(1f + squash / 2f, 1f - squash, 1f);
        }
        if (darkPane != null)
        {
            darkPane.color = new Color(1f, 1f, 1f, 1f - lamp);
            darkPane.enabled = lamp < 0.999f;
        }
    }
}
