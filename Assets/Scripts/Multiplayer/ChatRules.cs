using System.Collections.Generic;
using System.Globalization;
using System.Text;

public enum ChatResult : byte
{
    Accepted, Invalid, RateLimited, Unavailable
}

public readonly struct ChatMessage
{
    // Connection identity of the author; name and color come from the frozen match roster.
    public readonly ulong SenderId;
    public readonly string Text;

    public ChatMessage(ulong senderId, string text)
    {
        SenderId = senderId;
        Text = text;
    }
}

// Message rules shared by the sender's UI check and the host's authoritative check.
public static class ChatRules
{
    public const int MaxLength = 200;

    // Trims, turns line breaks and tabs into spaces, and rejects empty, oversized, or unsafe text
    // (control characters, bidirectional overrides, broken surrogate pairs). Emoji are allowed.
    public static bool TryNormalize(string value, out string normalized)
    {
        normalized = null;
        if (value == null)
            return false;
        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\n' || c == '\r' || c == '\t')
            {
                builder.Append(' ');
                continue;
            }
            if (char.IsControl(c) || IsBidiControl(c))
                return false;
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.LineSeparator || category == UnicodeCategory.ParagraphSeparator)
                return false;
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    return false;
                builder.Append(c).Append(value[++i]);
                continue;
            }
            if (char.IsLowSurrogate(c))
                return false;
            builder.Append(c);
        }
        normalized = builder.ToString().Trim();
        if (normalized.Length == 0 || normalized.Length > MaxLength)
        {
            normalized = null;
            return false;
        }
        return true;
    }

    private static bool IsBidiControl(char c) =>
        c == '\u061C' || c == '\u200E' || c == '\u200F' || (c >= '\u202A' && c <= '\u202E') || (c >= '\u2066' && c <= '\u2069');
}

// Sliding-window send limit per sender: at most MaxMessages within WindowSeconds.
public sealed class ChatRateLimiter
{
    public const int MaxMessages = 5;
    public const float WindowSeconds = 5f;
    private readonly Dictionary<ulong, Queue<float>> sent = new Dictionary<ulong, Queue<float>>();

    public bool TryConsume(ulong senderId, float now)
    {
        if (!sent.TryGetValue(senderId, out var times))
            sent.Add(senderId, times = new Queue<float>());
        while (times.Count > 0 && now - times.Peek() >= WindowSeconds)
            times.Dequeue();
        if (times.Count >= MaxMessages)
            return false;
        times.Enqueue(now);
        return true;
    }

    public void Forget(ulong senderId) => sent.Remove(senderId);

    public void Clear() => sent.Clear();
}
