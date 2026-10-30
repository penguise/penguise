using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.SmartFormat.PersistentVariables;

[RequireComponent(typeof(NetworkManager))]
public sealed class LobbyProfiles : MonoBehaviour
{
    private const string RequestMessage = "Lobby.Profile.Request.v1";
    private const string SnapshotMessage = "Lobby.Profile.Snapshot.v1";
    private const string ResultMessage = "Lobby.Profile.Result.v1";
    private const string SyncMessage = "Lobby.Profile.Sync.v1";
    public LobbyRoster Roster { get; } = new LobbyRoster();
    public event Action Changed;
    public event Action<ProfileResult> RequestCompleted;
    public bool IsPending { get; private set; }
    public ulong LocalClientId => network.LocalClientId;
    public bool IsConnected => network != null && network.IsConnectedClient;
    private NetworkManager network;
    private bool registered;
    private uint requestId;
    private float requestDeadline;

    private void Awake()
    {
        network = GetComponent<NetworkManager>();
        network.OnServerStarted += RegisterMessages;
        network.OnClientStarted += RegisterMessages;
        network.OnClientConnectedCallback += OnConnected;
        network.OnClientDisconnectCallback += OnDisconnected;
        network.OnClientStopped += OnStopped;
    }

    private void RegisterMessages()
    {
        if (registered)
            return;
        Roster.Clear();
        network.CustomMessagingManager.RegisterNamedMessageHandler(RequestMessage, ReceiveRequest);
        network.CustomMessagingManager.RegisterNamedMessageHandler(SnapshotMessage, ReceiveSnapshot);
        network.CustomMessagingManager.RegisterNamedMessageHandler(ResultMessage, ReceiveResult);
        network.CustomMessagingManager.RegisterNamedMessageHandler(SyncMessage, ReceiveSync);
        registered = true;
    }

    private void OnStopped(bool wasHost)
    {
        registered = false;
        IsPending = false;
        Roster.Clear();
        Changed?.Invoke();
    }

    private void OnConnected(ulong clientId)
    {
        if (network.IsServer)
        {
            if (!Roster.Join(clientId, DefaultName))
            {
                network.DisconnectClient(clientId);
                return;
            }
            BroadcastSnapshot();
        }
        else if (clientId == LocalClientId)
        {
            using var writer = new FastBufferWriter(1, Allocator.Temp);
            network.CustomMessagingManager.SendNamedMessage(SyncMessage, NetworkManager.ServerClientId, writer);
        }
    }

    private static string DefaultName(int number)
    {
        var text = new LocalizedString("Lobby", "lobby.player");
        text.Add("number", new IntVariable { Value = number });
        return text.GetLocalizedString();
    }

    private void OnDisconnected(ulong clientId)
    {
        if (network.IsServer)
        {
            Roster.Leave(clientId);
            BroadcastSnapshot();
        }
        if (clientId == LocalClientId)
            IsPending = false;
    }

    private void ReceiveSync(ulong sender, FastBufferReader reader)
    {
        if (network.IsServer && network.ConnectedClients.ContainsKey(sender))
            SendSnapshot(sender);
    }

    public void RequestChange(string name, LobbyColor color)
    {
        if (IsPending)
            return;
        if (!IsConnected || !Roster.TryGet(LocalClientId, out _))
        {
            RequestCompleted?.Invoke(ProfileResult.Unavailable);
            return;
        }
        if (!LobbyRoster.TryNormalizeName(name, out var normalized))
        {
            RequestCompleted?.Invoke(ProfileResult.InvalidName);
            return;
        }
        IsPending = true;
        requestDeadline = Time.unscaledTime + 10f;
        requestId++;
        Changed?.Invoke();
        if (network.IsServer)
        {
            var result = Roster.Change(LocalClientId, normalized, color);
            if (result == ProfileResult.Accepted)
                BroadcastSnapshot();
            CompleteRequest(result);
            return;
        }
        using var writer = new FastBufferWriter(160, Allocator.Temp);
        writer.WriteValueSafe(requestId);
        writer.WriteValueSafe(new FixedString128Bytes(normalized));
        writer.WriteValueSafe((byte)color);
        network.CustomMessagingManager.SendNamedMessage(RequestMessage, NetworkManager.ServerClientId, writer);
    }

    private void ReceiveRequest(ulong sender, FastBufferReader reader)
    {
        if (!network.IsServer || !network.ConnectedClients.ContainsKey(sender))
            return;
        try
        {
            reader.ReadValueSafe(out uint id);
            reader.ReadValueSafe(out FixedString128Bytes name);
            reader.ReadValueSafe(out byte color);
            // NGO invokes handlers on the main thread; validation and reservation are synchronous.
            var result = Roster.Change(sender, name.ToString(), (LobbyColor)color);
            if (result == ProfileResult.Accepted)
                BroadcastSnapshot();
            using var writer = new FastBufferWriter(8, Allocator.Temp);
            writer.WriteValueSafe(id);
            writer.WriteValueSafe((byte)result);
            network.CustomMessagingManager.SendNamedMessage(ResultMessage, sender, writer);
        }
        catch (Exception exception) when (exception is OverflowException || exception is ArgumentException)
        {
            Debug.LogWarning("Rejected a malformed lobby profile request.");
        }
    }

    private void ReceiveResult(ulong sender, FastBufferReader reader)
    {
        if (network.IsServer || sender != NetworkManager.ServerClientId)
            return;
        reader.ReadValueSafe(out uint id);
        reader.ReadValueSafe(out byte result);
        if (IsPending && id == requestId)
            CompleteRequest((ProfileResult)result);
    }

    private void CompleteRequest(ProfileResult result)
    {
        IsPending = false;
        RequestCompleted?.Invoke(result);
        Changed?.Invoke();
    }

    private void Update()
    {
        if (IsPending && Time.unscaledTime >= requestDeadline)
            CompleteRequest(ProfileResult.Unavailable);
    }

    public bool TryStartMatch()
    {
        var manager = RoomSessionManager.Instance;
        if (!network.IsServer || manager == null || !manager.CanStartGame || !Roster.TryLock())
            return false;
        BroadcastSnapshot();
        return true;
    }

    private void BroadcastSnapshot()
    {
        foreach (var id in network.ConnectedClientsIds)
            if (id != LocalClientId)
                SendSnapshot(id);
        Changed?.Invoke();
    }

    private void SendSnapshot(ulong target)
    {
        using var writer = new FastBufferWriter(1200, Allocator.Temp);
        writer.WriteValueSafe(Roster.IsLocked);
        writer.WriteValueSafe((byte)Roster.Players.Count);
        foreach (var player in Roster.Players)
        {
            writer.WriteValueSafe(player.ClientId);
            writer.WriteValueSafe(new FixedString128Bytes(player.Name));
            writer.WriteValueSafe((byte)player.Color);
        }
        network.CustomMessagingManager.SendNamedMessage(SnapshotMessage, target, writer);
    }

    private void ReceiveSnapshot(ulong sender, FastBufferReader reader)
    {
        if (network.IsServer || sender != NetworkManager.ServerClientId)
            return;
        reader.ReadValueSafe(out bool locked);
        reader.ReadValueSafe(out byte count);
        if (count > LobbyRoster.ColorCount)
            return;
        var snapshot = new List<LobbyPlayer>(count);
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out ulong id);
            reader.ReadValueSafe(out FixedString128Bytes name);
            reader.ReadValueSafe(out byte color);
            snapshot.Add(new LobbyPlayer(id, name.ToString(), (LobbyColor)color));
        }
        Roster.ApplySnapshot(snapshot, locked);
        Changed?.Invoke();
    }

    private void OnDestroy()
    {
        if (network == null)
            return;
        network.OnServerStarted -= RegisterMessages;
        network.OnClientStarted -= RegisterMessages;
        network.OnClientConnectedCallback -= OnConnected;
        network.OnClientDisconnectCallback -= OnDisconnected;
        network.OnClientStopped -= OnStopped;
        if (registered && network.CustomMessagingManager != null)
        {
            network.CustomMessagingManager.UnregisterNamedMessageHandler(RequestMessage);
            network.CustomMessagingManager.UnregisterNamedMessageHandler(SnapshotMessage);
            network.CustomMessagingManager.UnregisterNamedMessageHandler(ResultMessage);
            network.CustomMessagingManager.UnregisterNamedMessageHandler(SyncMessage);
        }
    }
}
