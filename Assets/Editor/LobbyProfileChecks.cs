using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;

public static class LobbyProfileChecks
{
    [MenuItem("Tools/Penguise/Check Lobby Profiles")]
    public static void CheckFromMenu() => UnityEngine.Debug.Log(Run());

    public static string Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            checks++;
            if (!condition)
                throw new InvalidOperationException(message);
        }
        string DefaultName(int number) => "Player " + number;
        var roster = new LobbyRoster();
        for (ulong id = 0; id < 4; id++)
            Check(roster.Join(id, DefaultName), "Joining must reserve a free color.");
        Check(roster.Players[0].Name == "Player 1", "Joining must assign a default name.");
        Check(roster.TryLock(), "Four participants must be able to start.");
        roster.Clear();
        roster.Join(10, DefaultName);
        roster.Join(20, DefaultName);
        Check(!roster.TryLock(), "Two players cannot start a match.");
        Check(roster.Change(10, "  Deniz  ", LobbyColor.Blue) == ProfileResult.Accepted, "First claim wins.");
        Check(roster.Change(20, "Ece", LobbyColor.Blue) == ProfileResult.ColorTaken, "Second simultaneous claim loses.");
        Check(roster.TryGet(20, out var loser) && loser.Color == LobbyColor.Orange && loser.Name == "Player 2",
            "A rejected claim must preserve both previous name and color.");
        Check(roster.TryGet(10, out var winner) && winner.Name == "Deniz" && winner.ClientId == 10,
            "Names are trimmed without changing identity.");
        Check(roster.Change(20, "Ece", LobbyColor.Red) == ProfileResult.Accepted, "Changing color frees the old one.");
        roster.Leave(10);
        Check(roster.Change(20, "Ece", LobbyColor.Blue) == ProfileResult.Accepted, "Leaving frees the color.");
        Check(roster.Change(999, "Spoof", LobbyColor.Pink) == ProfileResult.Unavailable, "Unknown identities cannot edit.");
        Check(roster.Change(20, "Ece", (LobbyColor)8) == ProfileResult.Unavailable, "Only eight colors exist.");
        foreach (var invalid in new[] { "", "   ", "<b>Name</b>", "line\nbreak", "x\u202ey", "x\u2028y", "\ud800", new string('a', 25) })
            Check(roster.Change(20, invalid, LobbyColor.Green) == ProfileResult.InvalidName, "Reject invalid names.");
        Check(roster.Change(20, "İpek Çığ 😀", LobbyColor.Blue) == ProfileResult.Accepted, "Support Turkish and valid Unicode.");
        Check(roster.Change(20, new string('a', 24), LobbyColor.Blue) == ProfileResult.Accepted, "Allow maximum length.");
        roster.Clear();
        var colors = new HashSet<LobbyColor>();
        for (ulong id = 0; id < 8; id++)
        {
            Check(roster.Join(id, DefaultName), "All eight slots must be available.");
            Check(roster.TryGet(id, out var player) && colors.Add(player.Color), "Each color must be unique.");
        }
        Check(!roster.Join(9, DefaultName), "The ninth player must be rejected.");
        Check(roster.TryLock(), "Eight players can start.");
        Check(roster.Change(0, "Changed", LobbyColor.Brown) == ProfileResult.Locked, "Started profiles are frozen.");
        roster.Leave(0);
        Check(roster.Players.Count == 8 && roster.TryGet(0, out _), "Match snapshot survives disconnects.");
        Check(!roster.Join(9, DefaultName), "Late joiners cannot alter a locked match.");
        roster.Clear();
        Check(!roster.IsLocked && roster.Players.Count == 0, "The next session starts clean.");
        // Reverse arrival order must also accept exactly one claimant.
        roster.Join(10, DefaultName);
        roster.Join(20, DefaultName);
        Check(roster.Change(20, "Second", LobbyColor.Purple) == ProfileResult.Accepted
            && roster.Change(10, "First", LobbyColor.Purple) == ProfileResult.ColorTaken,
            "The host must decide by arrival order, independently of player identity.");
        roster.Clear();
        roster.Join(0, DefaultName);
        Check(roster.Change(0, "Player 1", LobbyColor.Green) == ProfileResult.Accepted,
            "The host can change only its color before anyone else joins.");
        for (ulong id = 1; id < 8; id++)
        {
            Check(roster.Join(id, DefaultName) && roster.TryGet(id, out var player)
                && player.Name == DefaultName((int)id + 1),
                "Default names must follow independent slots even when the host frees Red.");
        }
        Check(roster.TryGet(1, out var secondPlayer) && secondPlayer.Color == LobbyColor.Red,
            "The second player can use Red while keeping the name Player 2.");
        roster.Leave(2);
        Check(roster.Join(80, DefaultName) && roster.TryGet(80, out var replacement)
            && replacement.Name == "Player 3", "A departure frees the name slot without renumbering others.");
        roster.Clear();
        Check(roster.Join(81, DefaultName) && roster.TryGet(81, out var newHost)
            && newHost.Name == "Player 1", "A new room resets name slots.");
        return $"PASS: {checks} lobby profile checks.";
    }

    // Uses loopback NGO clients in one Editor; no Relay allocation or player build.
    public static async Task<string> RunNetworkAsync()
    {
        var manager = RoomSessionManager.Instance;
        if (!EditorApplication.isPlaying || manager == null || manager.ActiveSession != null)
            throw new InvalidOperationException("Run in Play mode from MainMenu with no active room.");
        var host = manager.GetComponent<NetworkManager>();
        if (host.IsListening)
            throw new InvalidOperationException("A network session is already running.");
        var clients = new List<NetworkManager>();
        var oldSceneManagement = host.NetworkConfig.EnableSceneManagement;
        var transport = host.GetComponent<UnityTransport>();
        var oldConnection = transport.ConnectionData;
        var oldProtocol = transport.Protocol;
        if (oldProtocol != UnityTransport.ProtocolType.UnityTransport)
            throw new InvalidOperationException("Run this check before starting a Relay session.");
        async Task WaitFor(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + 15f;
            while (!condition())
            {
                if (Time.realtimeSinceStartup >= deadline)
                    throw new TimeoutException("Loopback lobby check timed out.");
                await Awaitable.NextFrameAsync();
            }
        }
        void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
        try
        {
            // Force a first-use table load, as in a fresh WebGL page before Lobby opens.
            await UnityEngine.Localization.Settings.LocalizationSettings.InitializationOperation.Task;
            UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase.ReleaseTable("Lobby");
            await manager.Profiles.PrepareDefaultNamesAsync();
            host.NetworkConfig.EnableSceneManagement = false;
            transport.SetConnectionData("127.0.0.1", 17891, "127.0.0.1");
            Require(host.StartHost(), "Loopback host did not start.");
            Require(manager.Profiles.Roster.TryGet(host.LocalClientId, out var hostProfile)
                && LobbyRoster.TryNormalizeName(hostProfile.Name, out _) && hostProfile.Color == LobbyColor.Red,
                "The host must immediately receive a valid default name and a reserved color.");
            ProfileResult? hostResult = null;
            manager.Profiles.RequestCompleted += OnHostResult;
            void OnHostResult(ProfileResult result) => hostResult = result;
            try
            {
                manager.Profiles.RequestChange(hostProfile.Name, LobbyColor.Brown);
                Require(hostResult == ProfileResult.Accepted, "The host must be able to edit its own profile.");
            }
            finally
            {
                manager.Profiles.RequestCompleted -= OnHostResult;
            }
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Lobby check client " + i);
                var client = go.AddComponent<NetworkManager>();
                clients.Add(client);
                var clientTransport = go.AddComponent<UnityTransport>();
                clientTransport.UseWebSockets = transport.UseWebSockets;
                clientTransport.SetConnectionData("127.0.0.1", 17891);
                client.NetworkConfig = new NetworkConfig
                {
                    NetworkTransport = clientTransport,
                    EnableSceneManagement = false,
                    RpcHashSize = host.NetworkConfig.RpcHashSize,
                };
                go.AddComponent<LobbyProfiles>();
                Require(client.StartClient(), "Loopback client did not start.");
                await WaitFor(() => client.IsConnectedClient);
            }
            var first = clients[0].GetComponent<LobbyProfiles>();
            var second = clients[1].GetComponent<LobbyProfiles>();
            await WaitFor(() => first.Roster.Players.Count == 4 && second.Roster.Players.Count == 4);
            var defaultNames = new HashSet<string>();
            foreach (var player in manager.Profiles.Roster.Players)
                Require(defaultNames.Add(player.Name), "Changing only the host color must not duplicate default names.");
            foreach (var client in clients)
            {
                var peer = client.GetComponent<LobbyProfiles>();
                await WaitFor(() => peer.Roster.TryGet(host.LocalClientId, out var seen)
                    && seen.Name == hostProfile.Name && seen.Color == LobbyColor.Brown);
            }
            ProfileResult? firstResult = null;
            ProfileResult? secondResult = null;
            first.RequestCompleted += result => firstResult = result;
            second.RequestCompleted += result => secondResult = result;
            // Both clients see Purple as free and send before another network frame can run.
            first.RequestChange("Deniz", LobbyColor.Purple);
            second.RequestChange("Ece", LobbyColor.Purple);
            await WaitFor(() => firstResult.HasValue && secondResult.HasValue);
            Require((firstResult == ProfileResult.Accepted && secondResult == ProfileResult.ColorTaken)
                || (secondResult == ProfileResult.Accepted && firstResult == ProfileResult.ColorTaken),
                "Exactly one simultaneous network claim must be accepted.");
            var winner = firstResult == ProfileResult.Accepted ? first : second;
            var loser = firstResult == ProfileResult.ColorTaken ? first : second;
            var winnerId = winner.LocalClientId;
            await WaitFor(() => manager.Profiles.Roster.TryGet(winnerId, out var p) && p.Color == LobbyColor.Purple);
            foreach (var client in clients)
            {
                var peer = client.GetComponent<LobbyProfiles>();
                Require(peer.Roster.TryGet(winnerId, out var seen) && seen.Color == LobbyColor.Purple
                    && seen.Name == (winner == first ? "Deniz" : "Ece"), "All peers must see the accepted name and color.");
            }
            winner.RequestChange("Updated", LobbyColor.Pink);
            await WaitFor(() => !winner.IsPending && loser.Roster.TryGet(winnerId, out var p) && p.Color == LobbyColor.Pink);
            loser.RequestChange("Other", LobbyColor.Purple);
            await WaitFor(() => !loser.IsPending && loser.Roster.TryGet(loser.LocalClientId, out var p) && p.Color == LobbyColor.Purple);
            winner.GetComponent<NetworkManager>().Shutdown();
            await WaitFor(() => manager.Profiles.Roster.Players.Count == 3);
            await WaitFor(() => loser.Roster.Players.Count == 3);
            loser.RequestChange("Other", LobbyColor.Pink);
            await WaitFor(() => !loser.IsPending && loser.Roster.TryGet(loser.LocalClientId, out var p) && p.Color == LobbyColor.Pink);
            Require(!manager.Profiles.Roster.TryLock(), "A disconnect below four must prevent starting.");
            return "PASS: cold table loaded asynchronously; host default profile created, editable and visible to all 3 clients; simultaneous claim has one winner and one conflict; change and disconnect free colors.";
        }
        finally
        {
            foreach (var client in clients)
                if (client != null)
                {
                    client.Shutdown();
                    UnityEngine.Object.Destroy(client.gameObject);
                }
            host.Shutdown();
            while (host.ShutdownInProgress)
                await Awaitable.NextFrameAsync();
            host.NetworkConfig.EnableSceneManagement = oldSceneManagement;
            transport.ConnectionData = oldConnection;
        }
    }
}
