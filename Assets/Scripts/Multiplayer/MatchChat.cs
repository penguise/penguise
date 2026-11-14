using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Host-authoritative match text chat over NGO named messages on the existing Relay connection.
// Players send to the host; the host checks identity (NGO sender id), match membership, text rules,
// and send rate, then relays accepted messages to every participant. History is bounded, in memory
// only, and cleared whenever a match starts, a start is cancelled, or the connection stops.
[RequireComponent(typeof(NetworkManager), typeof(LobbyProfiles))]
public sealed class MatchChat : MonoBehaviour
{
    private const string SendMessageName = "Match.Chat.Send.v1";
    private const string RelayMessageName = "Match.Chat.Message.v1";
    private const string RejectMessageName = "Match.Chat.Rejected.v1";
    public const int MaxHistory = 100;
    // A 200-character message is about 0.4 KB; anything far larger is malformed.
    private const int MaxPayloadBytes = 1024;

    private readonly List<ChatMessage> messages = new List<ChatMessage>();
    private readonly ChatRateLimiter hostLimiter = new ChatRateLimiter();
    private readonly ChatRateLimiter localLimiter = new ChatRateLimiter();
    private readonly List<ulong> relayTargets = new List<ulong>();
    private NetworkManager network;
    private LobbyProfiles profiles;
    private bool registered;
    private bool matchActive;

    public IReadOnlyList<ChatMessage> Messages => messages;
    public event Action<ChatMessage> MessageAdded;
    public event Action Cleared;
    // Raised on the sender when the host refuses a message it already sent.
    public event Action<ChatResult> SendRejected;

    // True while this player is a participant of a started match on a live connection.
    public bool IsAvailable => network != null && network.IsConnectedClient && profiles.Roster.IsLocked
        && profiles.Roster.TryGet(network.LocalClientId, out _);

    private void Awake()
    {
        network = GetComponent<NetworkManager>();
        profiles = GetComponent<LobbyProfiles>();
        network.OnServerStarted += RegisterMessages;
        network.OnClientStarted += RegisterMessages;
        network.OnClientDisconnectCallback += OnDisconnected;
        network.OnClientStopped += OnStopped;
        profiles.Changed += OnProfilesChanged;
    }

    private void RegisterMessages()
    {
        if (registered)
            return;
        network.CustomMessagingManager.RegisterNamedMessageHandler(SendMessageName, ReceiveSend);
        network.CustomMessagingManager.RegisterNamedMessageHandler(RelayMessageName, ReceiveRelay);
        network.CustomMessagingManager.RegisterNamedMessageHandler(RejectMessageName, ReceiveReject);
        registered = true;
    }

    private void OnStopped(bool wasHost)
    {
        registered = false;
        matchActive = false;
        Clear();
    }

    private void OnDisconnected(ulong clientId)
    {
        if (network.IsServer)
            hostLimiter.Forget(clientId);
    }

    // Each match (and each cancelled start) begins with an empty history. Also checked before every
    // send and receive, so the clear never depends on when the roster's Changed event fires.
    private void OnProfilesChanged()
    {
        bool locked = profiles.Roster.IsLocked;
        if (locked == matchActive)
            return;
        matchActive = locked;
        Clear();
    }

    private void Clear()
    {
        hostLimiter.Clear();
        localLimiter.Clear();
        if (messages.Count == 0)
            return;
        messages.Clear();
        Cleared?.Invoke();
    }

    // Sends a message as the local player. Accepted means it left this client (or the host took it);
    // a later host refusal arrives through SendRejected.
    public ChatResult Send(string text)
    {
        OnProfilesChanged();
        if (!IsAvailable)
            return ChatResult.Unavailable;
        if (!ChatRules.TryNormalize(text, out var normalized))
            return ChatResult.Invalid;
        if (network.IsServer)
            return HostAccept(network.LocalClientId, normalized);
        // Mirror the host's limit so ordinary typing never reaches a host refusal.
        if (!localLimiter.TryConsume(network.LocalClientId, Time.unscaledTime))
            return ChatResult.RateLimited;
        using var writer = new FastBufferWriter(MaxPayloadBytes, Allocator.Temp);
        writer.WriteValueSafe(normalized);
        network.CustomMessagingManager.SendNamedMessage(SendMessageName, NetworkManager.ServerClientId, writer);
        return ChatResult.Accepted;
    }

    private void ReceiveSend(ulong sender, FastBufferReader reader)
    {
        if (!network.IsServer || sender == network.LocalClientId)
            return;
        ChatResult result;
        if (reader.Length > MaxPayloadBytes)
            result = ChatResult.Invalid;
        else
        {
            try
            {
                reader.ReadValueSafe(out string text);
                result = HostAccept(sender, text);
            }
            catch (Exception exception) when (exception is OverflowException || exception is ArgumentException)
            {
                result = ChatResult.Invalid;
            }
        }
        if (result == ChatResult.Accepted || !network.ConnectedClients.ContainsKey(sender))
            return;
        using var writer = new FastBufferWriter(4, Allocator.Temp);
        writer.WriteValueSafe((byte)result);
        network.CustomMessagingManager.SendNamedMessage(RejectMessageName, sender, writer);
    }

    // Host only. The sender id comes from NGO, never from the payload.
    private ChatResult HostAccept(ulong sender, string text)
    {
        OnProfilesChanged();
        if (!network.ConnectedClients.ContainsKey(sender) || !profiles.Roster.IsLocked
            || !profiles.Roster.TryGet(sender, out _))
            return ChatResult.Unavailable;
        if (!ChatRules.TryNormalize(text, out var normalized))
            return ChatResult.Invalid;
        if (!hostLimiter.TryConsume(sender, Time.unscaledTime))
            return ChatResult.RateLimited;

        var message = new ChatMessage(sender, normalized);
        relayTargets.Clear();
        foreach (var id in network.ConnectedClientsIds)
            if (id != network.LocalClientId && profiles.Roster.TryGet(id, out _))
                relayTargets.Add(id);
        if (relayTargets.Count > 0)
        {
            using var writer = new FastBufferWriter(MaxPayloadBytes, Allocator.Temp);
            writer.WriteValueSafe(sender);
            writer.WriteValueSafe(normalized);
            network.CustomMessagingManager.SendNamedMessage(RelayMessageName, relayTargets, writer);
        }
        Add(message);
        return ChatResult.Accepted;
    }

    private void ReceiveRelay(ulong sender, FastBufferReader reader)
    {
        if (network.IsServer || sender != NetworkManager.ServerClientId || reader.Length > MaxPayloadBytes)
            return;
        OnProfilesChanged();
        try
        {
            reader.ReadValueSafe(out ulong author);
            reader.ReadValueSafe(out string text);
            if (profiles.Roster.IsLocked && profiles.Roster.TryGet(author, out _)
                && ChatRules.TryNormalize(text, out var normalized))
                Add(new ChatMessage(author, normalized));
        }
        catch (Exception exception) when (exception is OverflowException || exception is ArgumentException)
        {
            Debug.LogWarning("Ignored a malformed chat message.");
        }
    }

    private void ReceiveReject(ulong sender, FastBufferReader reader)
    {
        if (network.IsServer || sender != NetworkManager.ServerClientId)
            return;
        reader.ReadValueSafe(out byte result);
        if (result > (byte)ChatResult.Unavailable || result == (byte)ChatResult.Accepted)
            return;
        SendRejected?.Invoke((ChatResult)result);
    }

    private void Add(ChatMessage message)
    {
        messages.Add(message);
        if (messages.Count > MaxHistory)
            messages.RemoveAt(0);
        MessageAdded?.Invoke(message);
    }

    private void OnDestroy()
    {
        if (profiles != null)
            profiles.Changed -= OnProfilesChanged;
        if (network == null)
            return;
        network.OnServerStarted -= RegisterMessages;
        network.OnClientStarted -= RegisterMessages;
        network.OnClientDisconnectCallback -= OnDisconnected;
        network.OnClientStopped -= OnStopped;
        if (registered && network.CustomMessagingManager != null)
        {
            network.CustomMessagingManager.UnregisterNamedMessageHandler(SendMessageName);
            network.CustomMessagingManager.UnregisterNamedMessageHandler(RelayMessageName);
            network.CustomMessagingManager.UnregisterNamedMessageHandler(RejectMessageName);
        }
    }
}
