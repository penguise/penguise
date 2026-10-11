using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UIElements;

[RequireComponent(typeof(PanelRenderer))]
public class RoomDialogController : MonoBehaviour
{
    private const string HiddenClass = "room-dialog-hidden";
    private const string TableName = "MainMenu";
    private readonly LocalizedString placeholder = new LocalizedString();

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
        cancelButton.clicked += Close;
    }

    private void OpenCreate()
    {
        Open(
            "room_dialog.create_title",
            "room_dialog.name_placeholder",
            "room_dialog.create_button",
            createButton
        );
    }

    private void OpenJoin()
    {
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

    public void ShowCreatedStatus()
    {
        ShowStatus("room_dialog.created_status", true);
    }

    public void ShowJoinedStatus()
    {
        ShowStatus("room_dialog.joined_status", true);
    }

    public void ShowErrorStatus()
    {
        ShowStatus("room_dialog.error_status", false);
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
            cancelButton.clicked -= Close;

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
