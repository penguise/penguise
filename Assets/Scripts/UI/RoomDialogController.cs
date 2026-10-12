using System;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UIElements;

[RequireComponent(typeof(PanelRenderer))]
public class RoomDialogController : MonoBehaviour
{
    private const string HiddenClass = "room-dialog-hidden";
    private const string TableName = "MainMenu";
    private readonly LocalizedString placeholder = new LocalizedString();

    [SerializeField]
    private RoomSessionManager sessionManager;
    private bool createRoom;
    private bool isBusy;

    private PanelRenderer panel;
    private VisualElement overlay;
    private VisualElement menuContent;
    private VisualElement languagePanel;
    private Label title;
    private Label status;
    private TextField input;
    private Button createButton;
    private Button joinButton;
    private Button cancelButton;
    private Button confirmButton;
    private Button opener;

    private void OnEnable()
    {
        panel = GetComponent<PanelRenderer>();
        panel.RegisterUIReloadCallback(OnUIReload);
    }

    private void OnUIReload(PanelRenderer renderer, VisualElement root, int version)
    {
        UnregisterCallbacks();

        if (RoomSessionManager.Instance != null)
            sessionManager = RoomSessionManager.Instance;

        if (sessionManager == null)
        {
            Debug.LogError("Room session manager is not assigned.", this);
            return;
        }

        overlay = root.Q<VisualElement>("room-dialog-overlay");
        menuContent = root.Q<VisualElement>("menu-content");
        languagePanel = root.Q<VisualElement>("language-panel");
        title = root.Q<Label>("dialog-title");
        status = root.Q<Label>("dialog-status");
        input = root.Q<TextField>("room-input");
        createButton = root.Q<Button>("create-room-button");
        joinButton = root.Q<Button>("join-room-button");
        cancelButton = root.Q<Button>("cancel-button");
        confirmButton = root.Q<Button>("confirm-button");

        if (
            overlay == null
            || menuContent == null
            || languagePanel == null
            || title == null
            || status == null
            || input == null
            || createButton == null
            || joinButton == null
            || cancelButton == null
            || confirmButton == null
        )
        {
            Debug.LogError("Room dialog UI elements were not found.", this);
            return;
        }

        Close();
        confirmButton.SetEnabled(false);
        createButton.clicked += OpenCreate;
        joinButton.clicked += OpenJoin;
        cancelButton.clicked += OnCancel;
        confirmButton.clicked += OnConfirm;
        input.RegisterValueChangedCallback(OnInputChanged);
    }

    private void OpenCreate()
    {
        createRoom = true;
        Open(
            "room_dialog.create_title",
            "room_dialog.name_placeholder",
            "room_dialog.create_button",
            createButton
        );
    }

    private void OpenJoin()
    {
        createRoom = false;
        Open(
            "room_dialog.join_title",
            "room_dialog.code_placeholder",
            "room_dialog.join_button",
            joinButton
        );
    }

    private void Open(string titleKey, string placeholderKey, string buttonKey, Button source)
    {
        opener = source;
        title.text = string.Empty;
        input.textEdition.placeholder = string.Empty;
        confirmButton.text = string.Empty;
        title.SetBinding(nameof(Label.text), new LocalizedString(TableName, titleKey));
        placeholder.StringChanged -= UpdatePlaceholder;
        placeholder.SetReference(TableName, placeholderKey);
        placeholder.StringChanged += UpdatePlaceholder;
        confirmButton.SetBinding(nameof(Button.text), new LocalizedString(TableName, buttonKey));
        input.SetValueWithoutNotify(string.Empty);
        UpdateControls();
        ClearStatus();
        menuContent.SetEnabled(false);
        languagePanel.SetEnabled(false);
        overlay.RemoveFromClassList(HiddenClass);
        input.Focus();
    }

    private void Close()
    {
        overlay?.AddToClassList(HiddenClass);
        menuContent?.SetEnabled(true);
        languagePanel?.SetEnabled(true);
        opener?.Focus();
        opener = null;
        ClearStatus();
    }

    private void UpdatePlaceholder(string value)
    {
        input.textEdition.placeholder = value;
    }

    private void OnInputChanged(ChangeEvent<string> evt)
    {
        if (evt.target == input)
            UpdateControls();
    }

    private void UpdateControls()
    {
        input.SetEnabled(!isBusy && sessionManager != null && sessionManager.ActiveSession == null);
        confirmButton.SetEnabled(input.enabledSelf && !string.IsNullOrWhiteSpace(input.value));
        cancelButton.SetEnabled(!isBusy);
    }

    private async void OnConfirm()
    {
        if (isBusy || sessionManager == null)
            return;
        isBusy = true;
        ClearStatus();
        UpdateControls();
        SetConfirmLabel(true);
        try
        {
            if (createRoom)
                await sessionManager.CreateRoomAsync(input.value);
            else
                await sessionManager.JoinRoomAsync(input.value);
            if (this == null || !isActiveAndEnabled)
                return;
            if (createRoom)
                ShowCreatedStatus();
            else
                ShowJoinedStatus();
        }
        catch (Exception exception)
        {
            ReportError(exception, !createRoom);
        }
        finally
        {
            isBusy = false;
            if (this != null && isActiveAndEnabled)
            {
                SetConfirmLabel(false);
                UpdateControls();
            }
        }
    }

    private void SetConfirmLabel(bool loading)
    {
        var key = createRoom
            ? (loading ? "room_dialog.creating_button" : "room_dialog.create_button")
            : (loading ? "room_dialog.joining_button" : "room_dialog.join_button");
        confirmButton.SetBinding(nameof(Button.text), new LocalizedString(TableName, key));
    }

    private async void OnCancel()
    {
        if (isBusy)
            return;
        isBusy = true;
        UpdateControls();
        try
        {
            if (sessionManager != null)
                await sessionManager.LeaveRoomAsync();
            if (this != null && isActiveAndEnabled)
                Close();
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
        finally
        {
            isBusy = false;
            if (this != null && isActiveAndEnabled)
                UpdateControls();
        }
    }

    public void ShowCreatedStatus()
    {
        ShowStatus("room_dialog.created_status", true);
    }

    public void ShowJoinedStatus()
    {
        ShowStatus("room_dialog.joined_status", true);
    }

    public void ShowErrorStatus(Exception exception = null, bool joining = false)
    {
        ShowStatus(GetErrorKey(exception, joining), false);
    }

    private void ReportError(Exception exception, bool joining = false)
    {
        if (
            exception is SessionException
            || exception is RequestFailedException
            || exception is TimeoutException
        )
            Debug.LogWarning($"Room operation failed: {exception}", this);
        else
            Debug.LogException(exception, this);

        if (this != null && isActiveAndEnabled)
            ShowErrorStatus(exception, joining);
    }

    private static string GetErrorKey(Exception exception, bool joining)
    {
        if (exception is SessionException sessionException)
        {
            switch (sessionException.Error)
            {
                case SessionError.SessionNotFound:
                case SessionError.SessionDeleted:
                    return "room_dialog.not_found_error";
                case SessionError.NetworkSetupFailed:
                case SessionError.NetworkManagerStartFailed:
                case SessionError.AllocationNotFound:
                    return "room_dialog.connection_error";
                case SessionError.RateLimitExceeded:
                    return "room_dialog.rate_limit_error";
            }
        }

        if (exception is RequestFailedException requestException)
        {
            switch (requestException.ErrorCode)
            {
                case CommonErrorCodes.TransportError:
                case CommonErrorCodes.Timeout:
                case CommonErrorCodes.ServiceUnavailable:
                    return "room_dialog.connection_error";
                case CommonErrorCodes.TooManyRequests:
                    return "room_dialog.rate_limit_error";
            }
        }

        if (exception is TimeoutException)
            return "room_dialog.connection_error";

        return joining ? "room_dialog.join_error" : "room_dialog.error_status";
    }

    private void ShowStatus(string key, bool success)
    {
        if (status == null)
            return;

        status.text = string.Empty;
        status.EnableInClassList("dialog-status-success", success);
        status.EnableInClassList("dialog-status-error", !success);
        status.SetBinding(nameof(Label.text), new LocalizedString(TableName, key));
        status.RemoveFromClassList(HiddenClass);
    }

    private void ClearStatus()
    {
        if (status == null)
            return;

        status.ClearBinding(nameof(Label.text));
        status.text = string.Empty;
        status.RemoveFromClassList("dialog-status-success");
        status.RemoveFromClassList("dialog-status-error");
        status.AddToClassList(HiddenClass);
    }

    private void UnregisterCallbacks()
    {
        if (createButton != null)
            createButton.clicked -= OpenCreate;
        if (joinButton != null)
            joinButton.clicked -= OpenJoin;
        if (cancelButton != null)
            cancelButton.clicked -= OnCancel;
        if (confirmButton != null)
            confirmButton.clicked -= OnConfirm;
        input?.UnregisterValueChangedCallback(OnInputChanged);

        title?.ClearBinding(nameof(Label.text));
        placeholder.StringChanged -= UpdatePlaceholder;
        status?.ClearBinding(nameof(Label.text));
        confirmButton?.ClearBinding(nameof(Button.text));
    }

    private void OnDisable()
    {
        Close();
        UnregisterCallbacks();
        panel.UnregisterUIReloadCallback(OnUIReload);
    }
}
