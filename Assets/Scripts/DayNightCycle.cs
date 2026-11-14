using UnityEngine;

// Ambient light over a 24-hour day, applied by GameLayout through a global 2D light on lit sprites.
public static class DayNightCycle
{
    public const float Hours = 24f;
    public const float Noon = 12f;
    public const float Midnight = 0f;

    private readonly struct Key
    {
        public readonly float Hour;
        public readonly Color Color;
        public readonly float Intensity;

        public Key(float hour, Color color, float intensity)
        {
            Hour = hour;
            Color = color;
            Intensity = intensity;
        }
    }

    private static readonly Color Night = new Color(0.42f, 0.52f, 0.85f);
    private const float NightIntensity = 0.42f;
    private const float DayIntensity = 1f;

    // Sorted by hour; the first and last keys match so the day wraps smoothly.
    private static readonly Key[] Keys =
    {
        new Key(0f, Night, NightIntensity),
        new Key(4.5f, Night, NightIntensity),
        new Key(6f, new Color(0.88f, 0.62f, 0.72f), 0.62f), // dawn
        new Key(7.5f, new Color(1f, 0.86f, 0.74f), 0.88f), // morning
        new Key(10f, Color.white, DayIntensity),
        new Key(16f, Color.white, DayIntensity),
        new Key(18f, new Color(1f, 0.8f, 0.6f), 0.88f), // golden hour
        new Key(19.5f, new Color(0.9f, 0.56f, 0.6f), 0.62f), // sunset
        new Key(21f, Night, NightIntensity),
        new Key(24f, Night, NightIntensity),
    };

    public static void Evaluate(float hour, out Color color, out float intensity)
    {
        hour = Mathf.Repeat(hour, Hours);
        for (int i = 1; i < Keys.Length; i++)
        {
            if (hour > Keys[i].Hour)
                continue;
            Key from = Keys[i - 1], to = Keys[i];
            float t = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(from.Hour, to.Hour, hour));
            color = Color.Lerp(from.Color, to.Color, t);
            intensity = Mathf.Lerp(from.Intensity, to.Intensity, t);
            return;
        }
        color = Keys[0].Color;
        intensity = Keys[0].Intensity;
    }

    // 0 in full daylight, 1 at full night; drives igloo window lights and name contrast.
    public static float Darkness(float intensity) =>
        Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.9f, NightIntensity + 0.05f, intensity));
}
