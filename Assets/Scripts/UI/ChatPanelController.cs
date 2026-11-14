using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UIElements;

// Chat panel presentation with a local preview: a submitted message appears in this client's list
// as if sent by a random displayed player. Nothing is sent over the network yet; networked sending,
// host validation, and per-hand clearing replace the preview later.
[RequireComponent(typeof(PanelRenderer))]
public sealed class ChatPanelController : MonoBehaviour
{
    private const string TableName = "Game";
    // UI-side cap only; the host-validated limit is decided with the networked chat.
    private const int MaxMessageLength = 200;
    private const int MaxMessages = 100;
    [SerializeField, HideInInspector] private GameLayout layout;
    private readonly LocalizedString placeholder = new LocalizedString(TableName, "chat.placeholder");
    private PanelRenderer panel;
    private TextField input;
    private Button send;
    private ScrollView messages;
    private Label empty;
    private string placeholderText = string.Empty;

    private void OnEnable()
    {
        panel = GetComponent<PanelRenderer>();
        panel.RegisterUIReloadCallback(OnUIReload);
        placeholder.StringChanged += UpdatePlaceholder;
    }

    private void OnUIReload(PanelRenderer renderer, VisualElement root, int version)
    {
        UnregisterInputCallbacks();
        var title = root.Q<Label>("chat-title");
        empty = root.Q<Label>("chat-empty");
        messages = root.Q<ScrollView>("chat-messages");
        send = root.Q<Button>("chat-send-button");
        input = root.Q<TextField>("chat-input");
        if (title == null || empty == null || messages == null || send == null || input == null)
        {
            Debug.LogError("Chat UI elements were not found.", this);
            return;
        }
        var chatPanel = root.Q<VisualElement>("chat-panel");
        if (chatPanel != null && chatPanel.Q<SnowCap>() == null)
        {
            var snow = new SnowCap();
            snow.AddToClassList("chat-snow-cap");
            chatPanel.Add(snow);
        }
        title.SetBinding(nameof(Label.text), new LocalizedString(TableName, "chat.title"));
        empty.SetBinding(nameof(Label.text), new LocalizedString(TableName, "chat.empty"));
        send.SetBinding(nameof(Button.text), new LocalizedString(TableName, "chat.send"));
        input.maxLength = MaxMessageLength;
        input.Query<TextElement>().ForEach(element => element.enableRichText = false);
        input.textEdition.placeholder = placeholderText;
        input.RegisterValueChangedCallback(OnInputChanged);
        input.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
        send.clicked += Submit;
        UpdateSendButton();
    }

    private void OnInputChanged(ChangeEvent<string> change) => UpdateSendButton();

    private void UpdateSendButton() => send.SetEnabled(!string.IsNullOrWhiteSpace(input.value));

    private void OnInputKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
            return;
        Submit();
        evt.StopPropagation();
    }

    private void Submit()
    {
        var text = input.value?.Trim();
        if (string.IsNullOrEmpty(text))
            return;
        var players = layout != null ? layout.DisplayedPlayers : null;
        if (players == null || players.Count == 0)
            return;
        AddMessage(players[Random.Range(0, players.Count)], text);
        input.value = string.Empty;
        input.Focus();
    }

    private void AddMessage(LobbyPlayer author, string text)
    {
        empty.AddToClassList("chat-hidden");
        string color = author.Color.ToString().ToLowerInvariant();

        var row = new VisualElement();
        row.AddToClassList("chat-message");
        var avatar = new PenguinBadge(GameLayout.PlayerColor(author.Color));
        avatar.AddToClassList("chat-message-avatar");
        row.Add(avatar);
        var column = new VisualElement();
        column.AddToClassList("chat-message-body");
        var name = new Label(author.Name) { enableRichText = false };
        name.AddToClassList("chat-message-author");
        name.AddToClassList("chat-tag-" + color);
        column.Add(name);
        var body = new Label(text) { enableRichText = false };
        body.AddToClassList("chat-message-text");
        column.Add(body);
        row.Add(column);

        messages.Add(row);
        // Bounded history: drop the oldest messages (the empty hint is the first child).
        while (messages.contentContainer.childCount > MaxMessages + 1)
            messages.contentContainer.RemoveAt(1);
        row.RegisterCallback<GeometryChangedEvent>(ScrollToNewest);
    }

    private void ScrollToNewest(GeometryChangedEvent evt)
    {
        ((VisualElement)evt.target).UnregisterCallback<GeometryChangedEvent>(ScrollToNewest);
        // Next tick, after the scroll range includes the new row: snap to the very bottom.
        messages.schedule.Execute(() => messages.verticalScroller.value = messages.verticalScroller.highValue);
    }

    private void UpdatePlaceholder(string value)
    {
        placeholderText = value;
        if (input != null)
            input.textEdition.placeholder = value;
    }

    private void UnregisterInputCallbacks()
    {
        if (input != null)
        {
            input.UnregisterValueChangedCallback(OnInputChanged);
            input.UnregisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
        }
        if (send != null)
            send.clicked -= Submit;
    }

    private void OnDisable()
    {
        placeholder.StringChanged -= UpdatePlaceholder;
        UnregisterInputCallbacks();
        panel.UnregisterUIReloadCallback(OnUIReload);
    }
}
