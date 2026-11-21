using UnityEngine;

// What the outside sees while a visitor is inside an igloo. Each kind's timeline is authored for a 4 s stay
// and stretched to the actual one, and it all settles back to rest before the visitor comes out.
// KnockOut: the window lamp flickers off and on and the igloo shakes in bursts about its base
// (Tools/ArtGen/igloo/preview_action.py draws the same curves). The lamp-off pane shows as the lamp goes out.
// Heal: the lamp dims softly, the igloo beats like a heart (lub-dub, quickening) with a mint glow in the
// window, then it hops once with a bright flash, plus signs and sparkles rise from the dome and the lamp
// comes back (Tools/ArtGen/igloo/preview_heal.py draws the same curves).
// GameLayout scales the window light by Lamp and the mint heal light by Glow.
public sealed class IglooAction : MonoBehaviour
{
    public enum Kind { KnockOut, Heal }

    private const float AuthoredSeconds = 4f;

    // ---- knock-out ----
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

    // ---- heal ----
    // The warm lamp fades down to DimTo over (DimStart, DimEnd) of the authored stay.
    private const float DimStart = 0.35f, DimEnd = 0.9f, DimTo = 0.3f;
    // (time in the authored stay, strength) heartbeats; each is a lub-dub BeatGap seconds apart (not
    // stretched: a heartbeat keeps its own shape).
    private static readonly (float At, float Strength)[] Beats =
    {
        (1.0f, 0.45f), (1.5f, 0.6f), (1.92f, 0.75f), (2.28f, 0.9f), (2.56f, 1f),
    };
    private const float BeatGap = 0.17f;
    // The revival (authored stay time): hop, flash, particles, the lamp back on.
    private const float Pop = 2.75f;
    // Peak uniform swell and extra vertical stretch at beat strength 1.
    private const float Swell = 0.022f, Stretch = 0.012f;
    // The pop's hop (lift in canvas pixels, air time) and landing squash (peak, settle time), in seconds.
    private const float HealHopCanvasPixels = 22f, HopSeconds = 0.32f;
    private const float LandSquash = 0.05f, LandSeconds = 0.35f;

    // Rising particles, positions in igloo art canvas pixels (y down) from the base pivot: plus signs from
    // the dome's top and a sparkle burst around it at the pop, a twinkle at the window on the stronger beats.
    private struct Particle
    {
        public bool Plus;
        public float Start, Life;   // seconds, already stretched to the stay
        public Vector2 From, Move;  // canvas px from the base pivot, y down
        public float Size, Spin;    // canvas px, degrees over the life
    }
    private const int PlusCount = 3, BurstCount = 9;
    // Where each plus sign starts, canvas px from the dome's top (y down).
    private static readonly Vector2[] PlusOffsets = { new Vector2(-150f, 40f), new Vector2(10f, -10f), new Vector2(160f, 50f) };
    private const int ParticleCount = PlusCount + BurstCount + 4;   // + one twinkle per beat after the first
    // Particle sprites are this many canvas pixels wide (HealPlus.png / HealSparkle.png at the igloo's scale).
    public const float ParticleCanvasPixels = 128f;

    private Transform body;
    private SpriteRenderer darkPane, healPane;
    private SpriteRenderer[] particleRenderers;
    private Particle[] particles = new Particle[ParticleCount];
    private Vector2 apexPixels, windowPixels, particleOrigin;
    private Vector3 restPosition;
    private float canvasPixelsPerUnit;
    private float duration, elapsed;
    private bool playing;
    private Kind kind;

    // Lamp brightness, 1 lit, 0 off (above 1 briefly as the heal ends).
    public float Lamp { get; private set; } = 1f;
    // Mint heal glow in the window, 0..1.
    public float Glow { get; private set; }
    public bool Playing => playing;
    public Kind Current => kind;
    // Particles drawn right now (heal).
    public int VisibleParticles { get; private set; }

    // `body` is the igloo's transform (pivot at the base); `darkPane` the lamp-off pane over the window.
    public void Setup(Transform body, SpriteRenderer darkPane, float canvasPixelsPerUnit)
    {
        this.body = body;
        this.darkPane = darkPane;
        this.canvasPixelsPerUnit = canvasPixelsPerUnit;
        restPosition = body.localPosition;
        Rest();
    }

    // Heal look: the mint pane over the window, and particle renderers under `particleParent` (unrotated,
    // unscaled by the action) around `basePoint`, the igloo's base pivot in that parent's space. `apex` and
    // `window` are the dome's top and the window, in the igloo's local units from its pivot.
    public void SetupHeal(SpriteRenderer healPane, Transform particleParent, Vector2 basePoint, Vector2 apex,
        Vector2 window, Sprite plus, Sprite sparkle, Material material, int sortingOrder)
    {
        this.healPane = healPane;
        particleOrigin = basePoint;
        apexPixels = new Vector2(apex.x, -apex.y) * canvasPixelsPerUnit;
        windowPixels = new Vector2(window.x, -window.y) * canvasPixelsPerUnit;
        particleRenderers = new SpriteRenderer[ParticleCount];
        for (int i = 0; i < ParticleCount; i++)
        {
            var item = new GameObject(i < PlusCount ? "Heal Plus" : "Heal Sparkle").AddComponent<SpriteRenderer>();
            item.transform.SetParent(particleParent, false);
            item.sprite = i < PlusCount ? plus : sparkle;
            item.sharedMaterial = material;
            item.sortingOrder = sortingOrder;
            item.enabled = false;
            particleRenderers[i] = item;
        }
        Rest();
    }

    // Plays the knock-out action over a stay of `seconds` (restarts if already playing).
    public void Play(float seconds) => Play(seconds, Kind.KnockOut);

    // Plays `action` over a stay of `seconds` (restarts if already playing).
    public void Play(float seconds, Kind action)
    {
        Rest();
        kind = action;
        duration = Mathf.Max(0.01f, seconds);
        elapsed = 0f;
        playing = true;
        if (kind == Kind.Heal)
            PlanParticles(duration);
        Tick(0f);
    }

    public void Stop()
    {
        playing = false;
        Rest();
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
        if (kind == Kind.Heal)
        {
            SampleHeal(elapsed, duration, out float lamp, out float glow, out Vector2 scale, out float lift);
            Apply(lamp, glow, 0f, scale, lift / canvasPixelsPerUnit);
            ShowParticles(elapsed);
        }
        else
        {
            Sample(elapsed, duration, out float lamp, out float tilt, out float squash, out float lift);
            Apply(lamp, 0f, tilt, new Vector2(1f + squash / 2f, 1f - squash), lift * HopCanvasPixels / canvasPixelsPerUnit);
        }
    }

    // Knock-out: lamp (0..1), tilt (degrees), squash and lift (fraction of the peak hop) at `time` into a `stay`.
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

    // Heal: warm lamp (1 normal), mint glow (0..1), body scale about the base and lift (canvas pixels)
    // at `time` into a `stay`.
    public static void SampleHeal(float time, float stay, out float lamp, out float glow, out Vector2 scale,
        out float lift)
    {
        float k = stay / AuthoredSeconds;
        float dimEnd = DimEnd * k, pop = Pop * k;
        lamp = 1f - (1f - DimTo) * SmoothStep(DimStart * k, dimEnd, time);
        float u = time - pop;
        if (u > 0f)
            lamp = DimTo + (1f - DimTo) * SmoothStep(0f, 0.3f, u) + (u < 0.7f ? 0.35f * Mathf.Sin(Mathf.PI * u / 0.7f) : 0f);

        float beat = 0f;
        foreach (var (at, strength) in Beats)
            beat += strength * (Pulse(time - at * k) + 0.6f * Pulse(time - at * k - BeatGap));
        float hum = 0.3f * SmoothStep(dimEnd - 0.2f, dimEnd + 0.3f, time) * (1f - SmoothStep(pop + 0.1f, pop + 0.9f, time));
        float flash = time >= pop - 0.03f ? Mathf.Exp(-Mathf.Max(0f, u) / 0.25f) : 0f;
        glow = Mathf.Min(1f, hum + 0.7f * beat + flash);

        float sx = 1f + Swell * beat, sy = 1f + (Swell + Stretch) * beat;
        lift = 0f;
        if (u >= 0f && u < HopSeconds)
        {
            float air = Mathf.Sin(Mathf.PI * u / HopSeconds);
            lift = HealHopCanvasPixels * air;
            sy += 0.03f * air;    // stretched in the air
            sx -= 0.015f * air;
        }
        else if (u >= HopSeconds)
        {
            float v = (u - HopSeconds) / LandSeconds;
            if (v < 1f)
            {
                float squash = LandSquash * Mathf.Exp(-3f * v) * Mathf.Cos(2.5f * Mathf.PI * v);
                sy -= squash;
                sx += squash / 2f;
            }
        }
        scale = new Vector2(sx, sy);
    }

    // One heartbeat thump: quick rise, softer fall (`u` seconds since the thump).
    private static float Pulse(float u) => u < 0f ? Mathf.Exp(-(u / 0.045f) * (u / 0.045f)) : Mathf.Exp(-u / 0.11f);

    private static float SmoothStep(float a, float b, float t)
    {
        float u = Mathf.Clamp01((t - a) / (b - a));
        return u * u * (3f - 2f * u);
    }

    private void PlanParticles(float stay)
    {
        float k = stay / AuthoredSeconds, pop = Pop * k;
        int n = 0;
        for (int i = 0; i < PlusCount; i++)
        {
            Vector2 offset = PlusOffsets[i];
            particles[n++] = new Particle
            {
                Plus = true, Start = pop + 0.1f * i * k, Life = 0.95f * k,
                From = apexPixels + offset, Move = new Vector2(0.25f * offset.x, -160f),
                Size = 112f - 10f * i,
            };
        }
        for (int i = 0; i < BurstCount; i++)
        {
            float a = (-160f + i * 140f / 8f) * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            particles[n++] = new Particle
            {
                Start = pop + 0.04f * (i % 3), Life = 0.6f * k,
                From = apexPixels + new Vector2(0f, 120f) + 240f * dir, Move = 90f * dir,
                Size = 46f + 10f * (i % 2), Spin = 90f,
            };
        }
        for (int i = 1; i < Beats.Length; i++)
            particles[n++] = new Particle
            {
                Start = Beats[i].At * k, Life = 0.4f,
                From = windowPixels + new Vector2(-70f, -80f), Move = new Vector2(-20f, -40f),
                Size = 40f + 20f * Beats[i].Strength, Spin = 60f,
            };
    }

    private void ShowParticles(float time)
    {
        VisibleParticles = 0;
        if (particleRenderers == null)
            return;
        for (int i = 0; i < particleRenderers.Length; i++)
        {
            SpriteRenderer item = particleRenderers[i];
            Particle p = particles[i];
            float u = (time - p.Start) / p.Life;
            if (item == null)
                continue;
            if (u < 0f || u > 1f)
            {
                item.enabled = false;
                continue;
            }
            Vector2 at;
            float size, alpha, angle;
            if (p.Plus)
            {
                float e = 1f - (1f - u) * (1f - u);
                at = p.From + p.Move * e + new Vector2(14f * Mathf.Sin(2f * Mathf.PI * 1.4f * u), 0f);
                size = Mathf.Min(1f, u / 0.15f) * (1.15f - 0.15f * Mathf.Min(1f, u / 0.3f));
                alpha = 1f - SmoothStep(0.6f, 1f, u);
                angle = 12f * Mathf.Sin(2f * Mathf.PI * u);
            }
            else
            {
                float e = 1f - (1f - u) * (1f - u) * (1f - u);
                at = p.From + p.Move * e;
                size = Mathf.Sin(Mathf.PI * u);
                alpha = 1f;
                angle = p.Spin * u;
            }
            float scale = p.Size * size / ParticleCanvasPixels;
            item.transform.localPosition = particleOrigin + new Vector2(at.x, -at.y) / canvasPixelsPerUnit;
            item.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            item.transform.localScale = new Vector3(scale, scale, 1f);
            item.color = new Color(1f, 1f, 1f, alpha);
            item.enabled = scale > 0.001f && alpha > 0.001f;
            if (item.enabled)
                VisibleParticles++;
        }
    }

    private void Rest()
    {
        Apply(1f, 0f, 0f, Vector2.one, 0f);
        VisibleParticles = 0;
        if (particleRenderers != null)
            foreach (SpriteRenderer item in particleRenderers)
                if (item != null)
                    item.enabled = false;
    }

    private void Apply(float lamp, float glow, float tilt, Vector2 scale, float lift)
    {
        Lamp = lamp;
        Glow = glow;
        if (body != null)
        {
            body.localPosition = restPosition + Vector3.up * lift;
            body.localRotation = Quaternion.Euler(0f, 0f, tilt);
            body.localScale = new Vector3(scale.x, scale.y, 1f);
        }
        if (darkPane != null)
        {
            float dark = 1f - Mathf.Min(1f, lamp);
            darkPane.color = new Color(1f, 1f, 1f, dark);
            darkPane.enabled = dark > 0.001f;
        }
        if (healPane != null)
        {
            healPane.color = new Color(1f, 1f, 1f, glow);
            healPane.enabled = glow > 0.004f;
        }
    }
}
