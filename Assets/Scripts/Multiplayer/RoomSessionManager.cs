using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;

[RequireComponent(typeof(NetworkManager), typeof(MultiplayerBootstrap))]
public class RoomSessionManager : MonoBehaviour
{
    [SerializeField, Min(2)]
    private int maxPlayers = 2;

    public ISession ActiveSession { get; private set; }
    public bool IsBusy { get; private set; }

    public Task<ISession> CreateRoomAsync(string roomName) => ConnectAsync(roomName, true);

    public Task<ISession> JoinRoomAsync(string roomCode) => ConnectAsync(roomCode, false);

    private async Task<ISession> ConnectAsync(string value, bool create)
    {
        if (IsBusy || ActiveSession != null)
            throw new InvalidOperationException("A room operation or session is already active.");
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A room name or code is required.", nameof(value));

        IsBusy = true;
        try
        {
            await GetComponent<MultiplayerBootstrap>().InitializeAsync();
            if (create)
            {
                var options = new SessionOptions
                {
                    Name = value.Trim(),
                    MaxPlayers = maxPlayers,
                    IsPrivate = true,
                }.WithRelayNetwork();
                ActiveSession = await MultiplayerService.Instance.CreateSessionAsync(options);
            }
            else
            {
                ActiveSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(
                    value.Trim().ToUpperInvariant()
                );
            }

            ActiveSession.Deleted += ClearSession;
            ActiveSession.RemovedFromSession += ClearSession;
            Debug.Log(
                $"Room connected. Name: {ActiveSession.Name}, join code: {ActiveSession.Code}",
                this
            );
            return ActiveSession;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LeaveRoomAsync()
    {
        if (IsBusy)
            throw new InvalidOperationException("A room operation is already active.");
        if (ActiveSession == null)
            return;

        IsBusy = true;
        try
        {
            await ActiveSession.LeaveAsync();
            ClearSession();
            Debug.Log("Left the room.", this);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearSession()
    {
        if (ActiveSession == null)
            return;
        ActiveSession.Deleted -= ClearSession;
        ActiveSession.RemovedFromSession -= ClearSession;
        ActiveSession = null;
    }

    private void OnDestroy() => ClearSession();
}
