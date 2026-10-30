using System;
using System.Collections.Generic;
using System.Globalization;

public enum LobbyColor : byte
{
    Red, Orange, Yellow, Green, Blue, Purple, Pink, Brown
}

public enum ProfileResult : byte
{
    Accepted, ColorTaken, InvalidName, Locked, Unavailable
}

public readonly struct LobbyPlayer
{
    // Connection identity is independent of editable presentation data.
    public readonly ulong ClientId;
    public readonly string Name;
    public readonly LobbyColor Color;

    public LobbyPlayer(ulong clientId, string name, LobbyColor color)
    {
        ClientId = clientId;
        Name = name;
        Color = color;
    }
}

public sealed class LobbyRoster
{
    public const int MaxNameLength = 24;
    public const int ColorCount = 8;
    private readonly List<LobbyPlayer> players = new List<LobbyPlayer>();
    public IReadOnlyList<LobbyPlayer> Players { get; }
    public bool IsLocked { get; private set; }

    public LobbyRoster() => Players = players.AsReadOnly();

    public bool TryGet(ulong clientId, out LobbyPlayer player)
    {
        foreach (var entry in players)
            if (entry.ClientId == clientId)
            {
                player = entry;
                return true;
            }
        player = default;
        return false;
    }

    public bool IsTaken(LobbyColor color, ulong exceptClientId)
    {
        foreach (var player in players)
            if (player.ClientId != exceptClientId && player.Color == color)
                return true;
        return false;
    }

    public bool Join(ulong clientId, Func<int, string> defaultName)
    {
        if (TryGet(clientId, out _))
            return true;
        if (IsLocked || players.Count >= ColorCount)
            return false;
        for (byte color = 0; color < ColorCount; color++)
            if (!IsTaken((LobbyColor)color, clientId))
            {
                players.Add(new LobbyPlayer(clientId, defaultName(color + 1), (LobbyColor)color));
                return true;
            }
        return false;
    }

    public void Leave(ulong clientId)
    {
        // A started match keeps its participant identities and presentation snapshot.
        if (!IsLocked)
            players.RemoveAll(player => player.ClientId == clientId);
    }

    public ProfileResult Change(ulong clientId, string name, LobbyColor color)
    {
        if (IsLocked)
            return ProfileResult.Locked;
        if (!TryGet(clientId, out _) || (byte)color >= ColorCount)
            return ProfileResult.Unavailable;
        if (!TryNormalizeName(name, out var normalized))
            return ProfileResult.InvalidName;
        // No await between checking availability and replacing the profile on the host.
        if (IsTaken(color, clientId))
            return ProfileResult.ColorTaken;
        var index = players.FindIndex(player => player.ClientId == clientId);
        players[index] = new LobbyPlayer(clientId, normalized, color);
        return ProfileResult.Accepted;
    }

    public bool TryLock()
    {
        if (IsLocked || players.Count < RoomSessionManager.MinPlayers || players.Count > ColorCount)
            return false;
        IsLocked = true;
        return true;
    }

    public static bool TryNormalizeName(string value, out string normalized)
    {
        normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > MaxNameLength)
            return false;
        for (int i = 0; i < normalized.Length; i++)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(normalized, i);
            if (normalized[i] == '<' || normalized[i] == '>' || char.IsControl(normalized[i])
                || category == UnicodeCategory.Format || category == UnicodeCategory.LineSeparator
                || category == UnicodeCategory.ParagraphSeparator)
                return false;
            if (char.IsHighSurrogate(normalized[i]))
            {
                if (i + 1 >= normalized.Length || !char.IsLowSurrogate(normalized[++i]))
                    return false;
            }
            else if (char.IsLowSurrogate(normalized[i]))
                return false;
        }
        return true;
    }

    // Only the transport's server-authenticated snapshot handler calls this on clients.
    public void ApplySnapshot(List<LobbyPlayer> snapshot, bool locked)
    {
        players.Clear();
        players.AddRange(snapshot);
        IsLocked = locked;
    }

    public void Clear()
    {
        players.Clear();
        IsLocked = false;
    }
}
