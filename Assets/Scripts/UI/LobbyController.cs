using System;
using Unity.Netcode;
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
    private Label startRequirement;
    private Button copyButton;
    private Button leaveButton;
    private Button startButton;
    private LobbyProfiles profiles;
    private TextField playerNameInput;
    private Button saveNameButton;
    private Label profileStatus;
    private readonly Button[] colorButtons = new Button[LobbyRoster.ColorCount];
    private readonly Label[] colorStates = new Label[LobbyRoster.ColorCount];
    private string lastConfirmedName;
    private string profileStatusKey;
    private bool leaving;
    private bool copying;
    private IVisualElementScheduledItem copyFeedbackReset;

    private void OnEnable()
    {
        manager = RoomSessionManager.Instance;
        if (manager != null)
        {
            manager.SessionChanged += RefreshSession;
            profiles = manager.Profiles;
            profiles.Changed += RenderSession;
            profiles.RequestCompleted += OnProfileResult;
        }
        panel = GetComponent<PanelRenderer>();
        panel.RegisterUIReloadCallback(OnUIReload);
    }

    private void OnUIReload(PanelRenderer renderer, VisualElement root, int version)
    {
        if (leaveButton != null)
            leaveButton.clicked -= OnLeave;
        if (copyButton != null)
            copyButton.clicked -= OnCopyCode;
        UnregisterProfileCallbacks();
        copyFeedbackReset?.Pause();
        roomName = root.Q<Label>("room-name");
        roomCode = root.Q<Label>("room-code");
        status = root.Q<Label>("lobby-status");
        playerList = root.Q<ScrollView>("player-list");
        playersTitle = root.Q<Label>("players-title");
        startRequirement = root.Q<Label>("start-requirement");
        copyButton = root.Q<Button>("copy-code-button");
        leaveButton = root.Q<Button>("leave-room-button");
        startButton = root.Q<Button>("start-game-button");
        playerNameInput = root.Q<TextField>("player-name-input");
        saveNameButton = root.Q<Button>("save-name-button");
        profileStatus = root.Q<Label>("profile-status");
        if (
            roomName == null
            || roomCode == null
            || status == null
            || playerList == null
            || playersTitle == null
            || startRequirement == null
            || copyButton == null
            || leaveButton == null
            || startButton == null
            || playerNameInput == null
            || saveNameButton == null
            || profileStatus == null
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
        var requirementText = new LocalizedString(TableName, "lobby.start_requirement");
        requirementText.Add("minimum", new IntVariable { Value = RoomSessionManager.MinPlayers });
        startRequirement.SetBinding(nameof(Label.text), requirementText);
        ResetCopyFeedback();
        playerNameInput.maxLength = LobbyRoster.MaxNameLength;
        playerNameInput.SetBinding(nameof(TextField.label), new LocalizedString(TableName, "lobby.name_label"));
        playerNameInput.Query<TextElement>().ForEach(element => element.enableRichText = false);
        saveNameButton.SetBinding(nameof(Button.text), new LocalizedString(TableName, "lobby.save_name"));
        root.Q<Label>("color-title").SetBinding(nameof(Label.text), new LocalizedString(TableName, "lobby.color_label"));
        var colors = root.Q<VisualElement>("color-picker");
        colors.Clear();
        for (int i = 0; i < LobbyRoster.ColorCount; i++)
        {
            var color = (LobbyColor)i;
            var button = new Button(() => OnSelectColor(color));
            button.name = "color-" + color.ToString().ToLowerInvariant();
            button.AddToClassList("color-option");
            var swatch = new VisualElement();
            swatch.AddToClassList("color-swatch");
            swatch.AddToClassList("color-" + color.ToString().ToLowerInvariant());
            var heading = new VisualElement();
            heading.AddToClassList("color-heading");
            heading.Add(swatch);
            var colorName = new Label();
            colorName.SetBinding(nameof(Label.text), new LocalizedString(TableName, "lobby.color." + color.ToString().ToLowerInvariant()));
            heading.Add(colorName);
            button.Add(heading);
            var state = new Label();
            state.AddToClassList("color-state");
            button.Add(state);
            colors.Add(button);
            colorButtons[i] = button;
            colorStates[i] = state;
        }
        lastConfirmedName = null;
        playerNameInput.RegisterValueChangedCallback(OnNameChanged);
        saveNameButton.clicked += OnSaveName;
        startButton.clicked += OnStartMatch;
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
        playerCapacity.Value = RoomSessionManager.MaxPlayers;
        copyButton.SetEnabled(session != null && !leaving && !copying);
        leaveButton.SetEnabled(!leaving);
        startButton.SetEnabled(!leaving && manager != null && manager.CanStartGame);
        startButton.EnableInClassList(HiddenClass, session == null || !session.IsHost);
        startRequirement.EnableInClassList(
            HiddenClass,
            session == null || session.Players.Count >= RoomSessionManager.MinPlayers
        );
        RenderProfile();

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
        if (profiles == null)
            return;
        foreach (var player in profiles.Roster.Players)
        {
            var row = new VisualElement { name = "player-" + player.ClientId };
            row.AddToClassList("player-row");
            var isSelf = player.ClientId == profiles.LocalClientId;
            row.EnableInClassList("player-row-self", isSelf);
            var swatch = new VisualElement();
            swatch.AddToClassList("color-swatch");
            swatch.AddToClassList("color-" + player.Color.ToString().ToLowerInvariant());
            row.Add(swatch);
            var name = new Label(player.Name) { enableRichText = false };
            name.AddToClassList("player-name");
            row.Add(name);
            AddPlayerBadge(row, "lobby.color." + player.Color.ToString().ToLowerInvariant());
            if (isSelf)
                AddPlayerBadge(row, "lobby.you");
            if (player.ClientId == NetworkManager.ServerClientId)
                AddPlayerBadge(row, "lobby.host");
            playerList.Add(row);
        }
    }

    private void RenderProfile()
    {
        if (playerNameInput == null || saveNameButton == null || profileStatus == null)
            return;
        LobbyPlayer self = default;
        var hasProfile = profiles != null && profiles.Roster.TryGet(profiles.LocalClientId, out self);
        var editable = hasProfile && profiles.IsConnected && !profiles.IsPending && !profiles.Roster.IsLocked && !leaving;
        if (hasProfile && (lastConfirmedName == null || playerNameInput.value == lastConfirmedName))
            playerNameInput.SetValueWithoutNotify(self.Name);
        if (hasProfile)
            lastConfirmedName = self.Name;
        playerNameInput.SetEnabled(editable);
        saveNameButton.SetEnabled(editable && playerNameInput.value != self.Name);
        for (int i = 0; i < colorButtons.Length; i++)
        {
            if (colorButtons[i] == null)
                continue;
            var selected = hasProfile && self.Color == (LobbyColor)i;
            var taken = profiles != null && profiles.Roster.IsTaken((LobbyColor)i, profiles.LocalClientId);
            colorButtons[i].SetEnabled(editable && !selected && !taken);
            colorButtons[i].EnableInClassList("color-selected", selected);
            colorButtons[i].EnableInClassList("color-taken", taken);
            colorStates[i].SetBinding(nameof(Label.text), new LocalizedString(TableName,
                selected ? "lobby.color_selected" : taken ? "lobby.color_taken" : "lobby.color_free"));
        }
        if (profiles != null && profiles.Roster.IsLocked)
            ShowProfileStatus("lobby.profile_locked");
        else if (profiles != null && profiles.IsPending)
            ShowProfileStatus("lobby.profile_pending");
        else if (!hasProfile)
            ShowProfileStatus("lobby.profile_syncing");
        else if (profileStatusKey == "lobby.profile_syncing" || profileStatusKey == "lobby.profile_pending")
            profileStatus.AddToClassList(HiddenClass);
    }

    private void OnNameChanged(ChangeEvent<string> change)
    {
        if (LobbyRoster.TryNormalizeName(change.newValue, out _))
            profileStatus.AddToClassList(HiddenClass);
        else
            ShowProfileStatus("lobby.invalid_name");
        RenderProfile();
    }

    private void OnSaveName()
    {
        if (profiles != null && profiles.Roster.TryGet(profiles.LocalClientId, out var self))
            profiles.RequestChange(playerNameInput.value, self.Color);
    }

    private void OnSelectColor(LobbyColor color)
    {
        if (!leaving && profiles != null)
            profiles.RequestChange(playerNameInput.value, color);
    }

    private void OnProfileResult(ProfileResult result)
    {
        if (result == ProfileResult.Accepted)
        {
            lastConfirmedName = null;
            profileStatus?.AddToClassList(HiddenClass);
        }
        else
        {
            ShowProfileStatus(result switch
            {
                ProfileResult.ColorTaken => "lobby.color_conflict",
                ProfileResult.InvalidName => "lobby.invalid_name",
                ProfileResult.Locked => "lobby.profile_locked",
                _ => "lobby.profile_error",
            });
        }
        RenderSession();
    }

    private void ShowProfileStatus(string key)
    {
        if (profileStatus == null)
            return;
        profileStatusKey = key;
        profileStatus.SetBinding(nameof(Label.text), new LocalizedString(TableName, key));
        profileStatus.RemoveFromClassList(HiddenClass);
    }

    private void OnStartMatch()
    {
        if (!leaving && profiles != null && !profiles.TryStartMatch())
            ShowProfileStatus("lobby.start_error");
    }

    private void UnregisterProfileCallbacks()
    {
        if (playerNameInput != null)
            playerNameInput.UnregisterValueChangedCallback(OnNameChanged);
        if (saveNameButton != null)
            saveNameButton.clicked -= OnSaveName;
        if (startButton != null)
            startButton.clicked -= OnStartMatch;
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
        RenderProfile();
        startButton.SetEnabled(false);
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
                startButton.SetEnabled(manager != null && manager.CanStartGame);
                RenderProfile();
            }
        }
    }

    private void OnDisable()
    {
        if (manager != null)
            manager.SessionChanged -= RefreshSession;
        if (profiles != null)
        {
            profiles.Changed -= RenderSession;
            profiles.RequestCompleted -= OnProfileResult;
        }
        UnregisterProfileCallbacks();
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
