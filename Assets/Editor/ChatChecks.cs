using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;

// Match chat checks. Run() covers text rules and the rate limiter in the Editor; RunNetworkAsync()
// runs in Play mode from MainMenu with no active room and uses one loopback host plus three
// loopback clients (no Relay allocation or player build).
public static class ChatChecks
{
    private const string SendMessageName = "Match.Chat.Send.v1";

    public static string Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            checks++;
            if (!condition)
                throw new InvalidOperationException(message);
        }
        Check(ChatRules.TryNormalize("  Merhaba  ", out var trimmed) && trimmed == "Merhaba", "Trim surrounding space.");
        Check(ChatRules.TryNormalize("a\nb\tc", out var flat) && flat == "a b c", "Line breaks and tabs become spaces.");
        Check(ChatRules.TryNormalize("İpek Çığ şüphe 😀👍🏽", out _), "Turkish letters and emoji are allowed.");
        Check(ChatRules.TryNormalize(new string('a', ChatRules.MaxLength), out _), "Maximum length is allowed.");
        foreach (var invalid in new[] { null, "", "   ", "\n\n", new string('a', ChatRules.MaxLength + 1),
                     "x\u202Ey", "x\u2066y", "x\u0007y", "x\u2028y", "\ud800", "a\udc00" })
            Check(!ChatRules.TryNormalize(invalid, out _), "Reject empty, oversized, or unsafe text.");

        var limiter = new ChatRateLimiter();
        for (int i = 0; i < ChatRateLimiter.MaxMessages; i++)
            Check(limiter.TryConsume(1, i * 0.1f), "A burst up to the limit is allowed.");
        Check(!limiter.TryConsume(1, 0.6f), "The next message inside the window is refused.");
        Check(limiter.TryConsume(2, 0.6f), "Limits are per sender.");
        Check(limiter.TryConsume(1, ChatRateLimiter.WindowSeconds + 0.01f), "The window slides.");
        limiter.Forget(1);
        for (int i = 0; i < ChatRateLimiter.MaxMessages; i++)
            Check(limiter.TryConsume(1, 10f), "Forgetting a sender resets its window.");
        return $"PASS: {checks} chat rule checks.";
    }

    public static async Task<string> RunNetworkAsync()
    {
        var manager = RoomSessionManager.Instance;
        if (!EditorApplication.isPlaying || manager == null || manager.ActiveSession != null)
            throw new InvalidOperationException("Run in Play mode from MainMenu with no active room.");
        var host = manager.GetComponent<NetworkManager>();
        if (host.IsListening)
            throw new InvalidOperationException("A network session is already running.");
        var transport = host.GetComponent<UnityTransport>();
        var oldConnection = transport.ConnectionData;
        var oldSceneManagement = host.NetworkConfig.EnableSceneManagement;
        var clients = new List<NetworkManager>();
        async Task WaitFor(Func<bool> condition, string what)
        {
            var deadline = Time.realtimeSinceStartup + 15f;
            while (!condition())
            {
                if (Time.realtimeSinceStartup >= deadline)
                    throw new TimeoutException($"Chat check timed out: {what} (host {manager.Chat.Messages.Count}, clients "
                        + string.Join("/", clients.ConvertAll(c => c.GetComponent<MatchChat>().Messages.Count
                            + (c.IsConnectedClient ? "" : "x"))) + ")");
                await Awaitable.NextFrameAsync();
            }
        }
        async Task Settle()
        {
            for (int i = 0; i < 20; i++)
                await Awaitable.NextFrameAsync();
        }
        void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
        // Bypasses the sender's own checks, as a modified client could.
        void RawSend(NetworkManager client, string text)
        {
            using var writer = new FastBufferWriter(4096, Allocator.Temp);
            writer.WriteValueSafe(text);
            client.CustomMessagingManager.SendNamedMessage(SendMessageName, NetworkManager.ServerClientId, writer);
        }
        try
        {
            await manager.Profiles.PrepareDefaultNamesAsync();
            host.NetworkConfig.EnableSceneManagement = false;
            transport.SetConnectionData("127.0.0.1", 17893, "127.0.0.1");
            Require(host.StartHost(), "Loopback host did not start.");
            var hostChat = manager.Chat;
            var chats = new List<MatchChat>();
            var rejections = new List<List<ChatResult>>();
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Chat check client " + i);
                var client = go.AddComponent<NetworkManager>();
                clients.Add(client);
                var clientTransport = go.AddComponent<UnityTransport>();
                clientTransport.UseWebSockets = transport.UseWebSockets;
                clientTransport.SetConnectionData("127.0.0.1", 17893);
                client.NetworkConfig = new NetworkConfig
                {
                    NetworkTransport = clientTransport,
                    EnableSceneManagement = false,
                    RpcHashSize = host.NetworkConfig.RpcHashSize,
                };
                go.AddComponent<LobbyProfiles>();
                var chat = go.AddComponent<MatchChat>();
                var seen = new List<ChatResult>();
                chat.SendRejected += seen.Add;
                chats.Add(chat);
                rejections.Add(seen);
                Require(client.StartClient(), "Loopback client did not start.");
                await WaitFor(() => client.IsConnectedClient, "client connect");
            }
            await WaitFor(() => chats.TrueForAll(c => c.GetComponent<LobbyProfiles>().Roster.Players.Count == 4), "rosters");

            // Lobby: chat belongs to the match, so nothing is accepted yet.
            Require(!chats[0].IsAvailable && chats[0].Send("early") == ChatResult.Unavailable, "No chat before the match.");
            RawSend(clients[0], "early");
            await WaitFor(() => rejections[0].Contains(ChatResult.Unavailable), "pre-match refusal");
            Require(hostChat.Messages.Count == 0, "The host must drop pre-match messages.");

            // Freeze the roster as a match start does, then resync every client's snapshot.
            Require(manager.Profiles.Roster.TryLock(), "Four players can start.");
            foreach (var client in clients)
            {
                using var writer = new FastBufferWriter(1, Allocator.Temp);
                client.CustomMessagingManager.SendNamedMessage("Lobby.Profile.Sync.v1", NetworkManager.ServerClientId, writer);
            }
            await WaitFor(() => chats.TrueForAll(c => c.IsAvailable) && hostChat.IsAvailable, "match lock");
            foreach (var r in rejections)
                r.Clear();

            Require(hostChat.Send("Herkes hazır mı?") == ChatResult.Accepted, "The host can chat.");
            Require(chats[0].Send("Selam 😀\nben buradayım") == ChatResult.Accepted, "A client can chat.");
            await WaitFor(() => hostChat.Messages.Count == 2 && chats.TrueForAll(c => c.Messages.Count == 2), "first relay");
            var firstClientId = clients[0].LocalClientId;
            foreach (var chat in chats)
            {
                Require(chat.Messages[0].SenderId == host.LocalClientId && chat.Messages[0].Text == "Herkes hazır mı?",
                    "Host message reaches every participant with the host as author.");
                Require(chat.Messages[1].SenderId == firstClientId && chat.Messages[1].Text == "Selam 😀 ben buradayım",
                    "Client message is normalized and attributed by the transport identity.");
            }

            // Invalid text from a modified client is refused by the host and never relayed.
            RawSend(clients[1], new string('x', ChatRules.MaxLength + 1));
            RawSend(clients[1], "evil\u202Etext");
            await WaitFor(() => rejections[1].Count == 2, "invalid refusals");
            Require(rejections[1].TrueForAll(r => r == ChatResult.Invalid), "Oversized or unsafe text is Invalid.");
            await Settle();
            Require(hostChat.Messages.Count == 2 && chats.TrueForAll(c => c.Messages.Count == 2), "Refused text is never relayed.");

            // Host rate limit against a client that ignores its own limiter.
            for (int i = 0; i < ChatRateLimiter.MaxMessages + 2; i++)
                RawSend(clients[2], "spam " + i);
            await WaitFor(() => rejections[2].Count == 2, "rate refusals");
            Require(rejections[2].TrueForAll(r => r == ChatResult.RateLimited), "Excess messages are RateLimited.");
            await WaitFor(() => hostChat.Messages.Count == 2 + ChatRateLimiter.MaxMessages
                && chats.TrueForAll(c => c.Messages.Count == hostChat.Messages.Count), "rate-limited relay");

            // A well-behaved client is stopped locally before reaching the host's limit.
            int accepted = 0;
            ChatResult last = ChatResult.Accepted;
            for (int i = 0; i < ChatRateLimiter.MaxMessages + 1; i++)
            {
                last = chats[1].Send("hızlı " + i);
                if (last == ChatResult.Accepted)
                    accepted++;
            }
            Require(accepted == ChatRateLimiter.MaxMessages && last == ChatResult.RateLimited, "The client limiter mirrors the host.");
            int expected = 2 + 2 * ChatRateLimiter.MaxMessages;
            await WaitFor(() => hostChat.Messages.Count == expected && chats.TrueForAll(c => c.Messages.Count == expected), "client burst");
            await Settle();
            Require(rejections[1].Count == 2, "Locally limited sends never reach a host refusal.");

            // Every participant sees the same history in the same order.
            foreach (var chat in chats)
                for (int i = 0; i < expected; i++)
                    Require(chat.Messages[i].SenderId == hostChat.Messages[i].SenderId
                        && chat.Messages[i].Text == hostChat.Messages[i].Text, "Histories match the host order.");

            // A departed participant can no longer send; others keep chatting.
            clients[0].Shutdown();
            await WaitFor(() => chats[0].Messages.Count == 0, "history cleared on disconnect");
            await WaitFor(() => !host.ConnectedClients.ContainsKey(firstClientId), "host sees the disconnect");
            Require(hostChat.Send("Biri ayrıldı.") == ChatResult.Accepted, "The host keeps chatting after a departure.");
            await WaitFor(() => chats[2].Messages.Count == hostChat.Messages.Count, "relay after departure");            return $"PASS: no chat before the match; host and client messages reach all 4 participants with transport identities; "
                + $"invalid and unsafe text refused; host limit {ChatRateLimiter.MaxMessages}/{ChatRateLimiter.WindowSeconds}s enforced and mirrored by clients; "
                + "identical ordered histories; history cleared on disconnect; chat continues after a departure.";
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
