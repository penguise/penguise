using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UIElements;

// Match chat panel: sends through MatchChat (host-validated, relayed to every participant) and shows
// the shared history; authors' names and colors come from the match roster. Without a live
// connection (e.g. Game opened directly in the Editor) the panel stays read-only.
[RequireComponent(typeof(PanelRenderer))]
public sealed class ChatPanelController : MonoBehaviour
{
    private const string TableName = "Game";
    private const string HiddenClass = "chat-hidden";
    private readonly LocalizedString placeholder = new LocalizedString(TableName, "chat.placeholder");
    private readonly LocalizedString statusText = new LocalizedString();
    private PanelRenderer panel;
    private TextField input;
    private Button send;
    private ScrollView messages;
    private Label empty;
    private Label status;
    private string placeholderText = string.Empty;
    private MatchChat chat;

    private void OnEnable()
    {
        panel = GetComponent<PanelRenderer>();
        var manager = Application.isPlaying ? RoomSessionManager.Instance : null;
        if (manager != null && manager.Profiles.IsConnected)
        {
            chat = manager.Chat;
            chat.MessageAdded += OnMessageAdded;
            chat.Cleared += RenderHistory;
            chat.SendRejected += ShowStatus;
        }
        panel.RegisterUIReloadCallback(OnUIReload);
        placeholder.StringChanged += UpdatePlaceholder;
        statusText.StringChanged += UpdateStatus;
    }

    private void OnUIReload(PanelRenderer renderer, VisualElement root, int version)
    {
        UnregisterInputCallbacks();
        var title = root.Q<Label>("chat-title");
        empty = root.Q<Label>("chat-empty");
        status = root.Q<Label>("chat-status");
        messages = root.Q<ScrollView>("chat-messages");
        send = root.Q<Button>("chat-send-button");
        input = root.Q<TextField>("chat-input");
        if (title == null || empty == null || status == null || messages == null || send == null || input == null)
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
        input.maxLength = ChatRules.MaxLength;
        input.Query<TextElement>().ForEach(element => element.enableRichText = false);
        input.textEdition.placeholder = placeholderText;
        input.RegisterValueChangedCallback(OnInputChanged);
        input.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
        send.clicked += Submit;
        HideStatus();
        RenderHistory();
        UpdateSendButton();
    }

    private void OnInputChanged(ChangeEvent<string> change)
    {
        HideStatus();
        UpdateSendButton();
    }

    private void UpdateSendButton() => send.SetEnabled(chat != null && chat.IsAvailable
        && !string.IsNullOrWhiteSpace(input.value));

    private void OnInputKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
            return;
        Submit();
        evt.StopPropagation();
    }

    private void Submit()
    {
        if (chat == null || input == null || string.IsNullOrWhiteSpace(input.value))
            return;
        var result = chat.Send(input.value);
        if (result != ChatResult.Accepted)
        {
            // Keep the text so the player can fix or resend it.
            ShowStatus(result);
            return;
        }
        input.value = string.Empty;
        input.Focus();
    }

    private void OnMessageAdded(ChatMessage message)
    {
        if (messages == null)
            return;
        AddMessage(message);
        // Bounded history: drop the oldest rows (the empty hint is the first child).
        while (messages.contentContainer.childCount > MatchChat.MaxHistory + 1)
            messages.contentContainer.RemoveAt(1);
    }

    private void RenderHistory()
    {
        if (messages == null || chat == null)
            return;
        while (messages.contentContainer.childCount > 1)
            messages.contentContainer.RemoveAt(1);
        empty.RemoveFromClassList(HiddenClass);
        foreach (var message in chat.Messages)
            AddMessage(message);
    }

    private void AddMessage(ChatMessage message)
    {
        var profiles = RoomSessionManager.Instance.Profiles;
        if (!profiles.Roster.TryGet(message.SenderId, out var author))
            return;
        empty.AddToClassList(HiddenClass);
        string color = author.Color.ToString().ToLowerInvariant();

        var row = new VisualElement();
        row.AddToClassList("chat-message");
        row.EnableInClassList("chat-message-self", message.SenderId == profiles.LocalClientId);
        var avatar = new PenguinBadge(GameLayout.PlayerColor(author.Color));
        avatar.AddToClassList("chat-message-avatar");
        row.Add(avatar);
        var column = new VisualElement();
        column.AddToClassList("chat-message-body");
        var name = new Label(author.Name) { enableRichText = false };
        name.AddToClassList("chat-message-author");
        name.AddToClassList("chat-tag-" + color);
        column.Add(name);
        var body = new Label(message.Text) { enableRichText = false };
        body.AddToClassList("chat-message-text");
        column.Add(body);
        row.Add(column);

        messages.Add(row);
        row.RegisterCallback<GeometryChangedEvent>(ScrollToNewest);
    }

    private void ScrollToNewest(GeometryChangedEvent evt)
    {
        ((VisualElement)evt.target).UnregisterCallback<GeometryChangedEvent>(ScrollToNewest);
        // Next tick, after the scroll range includes the new row: snap to the very bottom.
        messages.schedule.Execute(() => messages.verticalScroller.value = messages.verticalScroller.highValue);
    }

    private void ShowStatus(ChatResult result)
    {
        if (status == null)
            return;
        string key = result switch
        {
            ChatResult.RateLimited => "chat.error.rate_limited",
            ChatResult.Invalid => "chat.error.invalid",
            _ => "chat.error.unavailable",
        };
        statusText.SetReference(TableName, key);
        status.RemoveFromClassList(HiddenClass);
    }

    private void UpdateStatus(string value)
    {
        if (status != null)
            status.text = value;
    }

    private void HideStatus() => status?.AddToClassList(HiddenClass);

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
        statusText.StringChanged -= UpdateStatus;
        if (chat != null)
        {
            chat.MessageAdded -= OnMessageAdded;
            chat.Cleared -= RenderHistory;
            chat.SendRejected -= ShowStatus;
            chat = null;
        }
        UnregisterInputCallbacks();
        panel.UnregisterUIReloadCallback(OnUIReload);
    }
}
