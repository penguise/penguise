using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NetworkManager), typeof(MultiplayerBootstrap))]
public class RoomSessionManager : MonoBehaviour
{
    public const string LobbyScene = "Lobby";
    public const string MenuScene = "MainMenu";
    public static RoomSessionManager Instance { get; private set; }
    public event Action SessionChanged;

    [SerializeField, Min(2)]
    private int maxPlayers = 2;

    public ISession ActiveSession { get; private set; }
    public bool IsBusy { get; private set; }

    private bool returningToMenu;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public Task<ISession> CreateRoomAsync(string roomName) => ConnectAsync(roomName, true);

    public Task<ISession> JoinRoomAsync(string roomCode) => ConnectAsync(roomCode, false);

    private async Task<ISession> ConnectAsync(string value, bool create)
    {
        if (IsBusy || returningToMenu || ActiveSession != null)
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

            ActiveSession.Deleted += OnSessionEnded;
            ActiveSession.RemovedFromSession += OnSessionEnded;
            SessionChanged?.Invoke();

            if (create)
            {
                var status = GetComponent<NetworkManager>()
                    .SceneManager.LoadScene(LobbyScene, LoadSceneMode.Single);
                if (status != SceneEventProgressStatus.Started)
                {
                    await ActiveSession.AsHost().DeleteAsync();
                    ClearSession();
                    throw new InvalidOperationException($"Lobby scene loading failed: {status}.");
                }
            }
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
            if (ActiveSession.IsHost)
                await ActiveSession.AsHost().DeleteAsync();
            else
                await ActiveSession.LeaveAsync();
            ClearSession();
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
        ActiveSession.Deleted -= OnSessionEnded;
        ActiveSession.RemovedFromSession -= OnSessionEnded;
        ActiveSession = null;
        SessionChanged?.Invoke();
    }

    private async void OnSessionEnded()
    {
        ClearSession();
        if (IsBusy || SceneManager.GetActiveScene().name != LobbyScene)
            return;
        try
        {
            await ReturnToMenuAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
    }

    public async Task ReturnToMenuAsync()
    {
        if (returningToMenu)
            return;
        returningToMenu = true;
        try
        {
            await LeaveRoomAsync();
            var networkManager = GetComponent<NetworkManager>();
            if (networkManager.IsListening)
                networkManager.Shutdown();
            while (networkManager.ShutdownInProgress)
                await Awaitable.NextFrameAsync(destroyCancellationToken);
            await SceneManager.LoadSceneAsync(MenuScene);
        }
        finally
        {
            returningToMenu = false;
        }
    }

    private void OnDestroy()
    {
        ClearSession();
        if (Instance == this)
            Instance = null;
    }
}
