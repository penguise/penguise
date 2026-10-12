using System;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.SmartFormat.PersistentVariables;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(PanelRenderer))]
public class LobbyController : MonoBehaviour
{
    private const string TableName = "Lobby";
    private const string HiddenClass = "lobby-hidden";
    private readonly StringVariable roomCodeValue = new StringVariable();
    private readonly StringVariable roomNameValue = new StringVariable();
    private readonly IntVariable playerCount = new IntVariable();
    private readonly IntVariable playerCapacity = new IntVariable();

    private PanelRenderer panel;
    private RoomSessionManager manager;
    private ISession session;
    private Label roomName;
    private Label roomCode;
    private Label status;
    private ScrollView playerList;
    private Label playersTitle;
    private Button copyButton;
    private Button leaveButton;
    private Button startButton;
    private bool leaving;
    private bool copying;
    private IVisualElementScheduledItem copyFeedbackReset;

    private void OnEnable()
    {
        manager = RoomSessionManager.Instance;
        if (manager != null)
            manager.SessionChanged += RefreshSession;
        panel = GetComponent<PanelRenderer>();
        panel.RegisterUIReloadCallback(OnUIReload);
    }

    private void OnUIReload(PanelRenderer renderer, VisualElement root, int version)
    {
        if (leaveButton != null)
            leaveButton.clicked -= OnLeave;
        if (copyButton != null)
            copyButton.clicked -= OnCopyCode;
        copyFeedbackReset?.Pause();
        roomName = root.Q<Label>("room-name");
        roomCode = root.Q<Label>("room-code");
        status = root.Q<Label>("lobby-status");
        playerList = root.Q<ScrollView>("player-list");
        playersTitle = root.Q<Label>("players-title");
        copyButton = root.Q<Button>("copy-code-button");
        leaveButton = root.Q<Button>("leave-room-button");
        startButton = root.Q<Button>("start-game-button");
        if (
            roomName == null
            || roomCode == null
            || status == null
            || playerList == null
            || playersTitle == null
            || copyButton == null
            || leaveButton == null
            || startButton == null
        )
        {
            Debug.LogError("Lobby UI elements were not found.", this);
            return;
        }

        var codeText = new LocalizedString(TableName, "lobby.room_code");
        codeText.Add("code", roomCodeValue);
        roomCode.SetBinding(nameof(Label.text), codeText);
        var playersText = new LocalizedString(TableName, "lobby.players");
        playersText.Add("count", playerCount);
        playersText.Add("capacity", playerCapacity);
        playersTitle.SetBinding(nameof(Label.text), playersText);
        ResetCopyFeedback();
        copyButton.clicked += OnCopyCode;
        leaveButton.clicked += OnLeave;
        RefreshSession();
    }

    private void RefreshSession()
    {
        var current = manager != null ? manager.ActiveSession : null;
        if (session != current)
        {
            if (session != null)
                session.Changed -= RenderSession;
            session = current;
            if (session != null)
                session.Changed += RenderSession;
        }
        RenderSession();
    }

    private void RenderSession()
    {
        if (roomName == null || playerList == null || leaveButton == null || startButton == null)
            return;

        playerList.Clear();
        roomCodeValue.Value = session?.Code ?? string.Empty;
        roomNameValue.Value = session?.Name ?? string.Empty;
        playerCount.Value = session?.Players.Count ?? 0;
        playerCapacity.Value = session?.MaxPlayers ?? 0;
        copyButton.SetEnabled(session != null && !leaving && !copying);
        leaveButton.SetEnabled(!leaving);
        startButton.SetEnabled(false);
        startButton.EnableInClassList(HiddenClass, session == null || !session.IsHost);

        if (session == null)
        {
            roomName.SetBinding(
                nameof(Label.text),
                new LocalizedString(TableName, "lobby.no_session")
            );
            return;
        }

        var nameText = new LocalizedString(TableName, "lobby.room_name");
        nameText.Add("name", roomNameValue);
        roomName.SetBinding(nameof(Label.text), nameText);
        for (int i = 0; i < session.Players.Count; i++)
        {
            var player = session.Players[i];
            var row = new VisualElement { name = player.Id };
            row.AddToClassList("player-row");
            var isSelf = player.Id == AuthenticationService.Instance.PlayerId;
            row.EnableInClassList("player-row-self", isSelf);
            var name = new Label();
            name.AddToClassList("player-name");
            var text = new LocalizedString(TableName, "lobby.player");
            text.Add("number", new IntVariable { Value = i + 1 });
            name.SetBinding(nameof(Label.text), text);
            row.Add(name);
            if (isSelf)
                AddPlayerBadge(row, "lobby.you");
            if (player.Id == session.Host)
                AddPlayerBadge(row, "lobby.host");
            playerList.Add(row);
        }
    }

    private static void AddPlayerBadge(VisualElement row, string key)
    {
        var badge = new Label();
        badge.AddToClassList("player-badge");
        badge.SetBinding(nameof(Label.text), new LocalizedString(TableName, key));
        row.Add(badge);
    }

    private async void OnCopyCode()
    {
        if (session == null || copying || leaving)
            return;
        copying = true;
        copyButton.SetEnabled(false);
        copyFeedbackReset?.Pause();
        status.AddToClassList(HiddenClass);
        try
        {
            await ClipboardUtility.CopyTextAsync(session.Code);
            if (this == null || !isActiveAndEnabled)
                return;
            copyButton.SetBinding(
                nameof(Button.text),
                new LocalizedString(TableName, "lobby.copied")
            );
            copyFeedbackReset = copyButton.schedule.Execute(ResetCopyFeedback).StartingIn(2000);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            if (this != null && isActiveAndEnabled)
            {
                status.SetBinding(
                    nameof(Label.text),
                    new LocalizedString(TableName, "lobby.copy_error")
                );
                status.RemoveFromClassList(HiddenClass);
                ResetCopyFeedback();
            }
        }
        finally
        {
            copying = false;
            if (this != null && isActiveAndEnabled)
                copyButton.SetEnabled(session != null && !leaving);
        }
    }

    private void ResetCopyFeedback()
    {
        copyButton.SetBinding(
            nameof(Button.text),
            new LocalizedString(TableName, "lobby.copy_code")
        );
    }

    private async void OnLeave()
    {
        if (leaving)
            return;
        leaving = true;
        copyButton.SetEnabled(false);
        leaveButton.SetEnabled(false);
        leaveButton.SetBinding(
            nameof(Button.text),
            new LocalizedString(TableName, "lobby.leaving")
        );
        status.ClearBinding(nameof(Label.text));
        status.AddToClassList(HiddenClass);
        try
        {
            if (manager != null)
                await manager.ReturnToMenuAsync();
            else
                await SceneManager.LoadSceneAsync(RoomSessionManager.MenuScene);
        }
        catch (OperationCanceledException) when (this == null) { }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            if (this != null && isActiveAndEnabled)
            {
                status.SetBinding(
                    nameof(Label.text),
                    new LocalizedString(TableName, "lobby.leave_error")
                );
                status.RemoveFromClassList(HiddenClass);
            }
        }
        finally
        {
            leaving = false;
            if (this != null && isActiveAndEnabled)
            {
                leaveButton.SetBinding(
                    nameof(Button.text),
                    new LocalizedString(TableName, "lobby.leave_room")
                );
                leaveButton.SetEnabled(true);
                copyButton.SetEnabled(session != null && !copying);
            }
        }
    }

    private void OnDisable()
    {
        if (manager != null)
            manager.SessionChanged -= RefreshSession;
        if (session != null)
            session.Changed -= RenderSession;
        session = null;
        if (leaveButton != null)
            leaveButton.clicked -= OnLeave;
        if (copyButton != null)
            copyButton.clicked -= OnCopyCode;
        copyFeedbackReset?.Pause();
        panel.UnregisterUIReloadCallback(OnUIReload);
    }
}
