using Crystal.Native;
using Godot;
using C = ClientPackets;

public partial class Main
{
    internal static Color ChatColour(ChatType type) => new(type switch
    {
        ChatType.Normal => "eee4cf", ChatType.System => "dcca94", ChatType.Hint => "ff9878",
        ChatType.Announcement or ChatType.LevelUp => "ffdc6d", ChatType.Group => "6fd9f0", ChatType.Guild => "88dd9c",
        ChatType.WhisperIn or ChatType.WhisperOut => "e1a3ed", ChatType.Shout or ChatType.Shout2 or ChatType.Shout3 => "ffbe73",
        ChatType.System2 => "a0d78c", ChatType.Relationship or ChatType.Mentor => "edb5ca", _ => "ddd8c8"
    });
    private Panel chatFrame = null!;
    private Control chatResize = null!;
    private Control chatChannels = null!;
    private readonly List<Button> chatChannelTabs = [];
    private LineEdit whisperTarget = null!;
    private readonly Queue<(string Text, ChatType Type)> chatHistory = new();
    private ChatChannel currentChat;
    private string lastWhisper = "";
    private bool resizingChat;
    private float chatResizeY, chatResizeHeight;

    private void BuildChatChannels()
    {
        chatChannels = new Control { Name = "ChatChannels", Position = new Vector2(230, 648), Size = new Vector2(464, 20), MouseFilter = MouseFilterEnum.Ignore };
        game.AddChild(chatChannels);
        foreach (var channel in Enum.GetValues<ChatChannel>())
        {
            var index = (int)channel;
            var button = Button(chatChannels, ChatChannels.Caption(channel), index * 52, 0, 48, () => SelectChat(channel));
            button.Name = "ChatChannel" + channel; button.TooltipText = ChatChannels.Caption(channel) + "频道";
            TabStyle(button, 48); button.AddThemeFontSizeOverride("font_size", ClientUi.Small);
            button.CustomMinimumSize = new Vector2(48, 20); button.SetDeferred(Control.PropertyName.Size, button.CustomMinimumSize);
            button.SetPressedNoSignal(channel == currentChat); chatChannelTabs.Add(button);
        }
        whisperTarget = Edit(game, 231, 716, 110, "私信对象");
        whisperTarget.Name = "WhisperTarget"; whisperTarget.MaxLength = Globals.MaxCharacterNameLength;
        whisperTarget.AddThemeFontSizeOverride("font_size", ClientUi.Small);
        foreach (var style in new[] { "normal", "focus", "read_only" }) whisperTarget.AddThemeStyleboxOverride(style, chat.GetThemeStylebox(style));
        whisperTarget.Size = new Vector2(110, 19); whisperTarget.Hide();
        whisperTarget.TextSubmitted += _ => chat.GrabFocus();
        whisperTarget.GuiInput += input => { if (input is InputEventKey { Pressed: true, Keycode: Key.Escape }) { whisperTarget.ReleaseFocus(); whisperTarget.AcceptEvent(); } };
    }

    private void SelectChat(ChatChannel channel)
    {
        currentChat = channel;
        foreach (var button in chatChannelTabs) button.SetPressedNoSignal(button == chatChannelTabs[(int)channel]);
        var whisper = channel == ChatChannel.Whisper;
        if (whisper && whisperTarget.Text.Length == 0) whisperTarget.Text = lastWhisper;
        whisperTarget.Visible = whisper;
        chat.Position = new Vector2(whisper ? 342 : 231, 716);
        chat.Size = new Vector2(whisper ? 516 : 627, 19);
        chat.PlaceholderText = channel == ChatChannel.All ? "聊天：Enter 发送，Esc 返回游戏" : $"{ChatChannels.Caption(channel)} · Enter 发送";
        RenderChatHistory();
    }

    private void OpenWhisper(string name)
    {
        SelectChat(ChatChannel.Whisper); whisperTarget.Text = name; chat.GrabFocus();
    }

    private void SubmitChat(string text)
    {
        if (text.Trim().Length == 0) { chat.ReleaseFocus(); return; }
        var message = ChatChannels.Format(currentChat, text, whisperTarget.Text, State.GroupMembers.Count > 0, State.InGuild, out var error);
        if (error != null) { Log(error, ChatType.Hint); return; }
        if (message != null) Send(new C.Chat { Message = message });
        chat.Clear(); chat.ReleaseFocus();
    }

    private void AppendChat(string text, ChatType type)
    {
        if (type == ChatType.WhisperIn && text.IndexOf("=>", StringComparison.Ordinal) is > 0 and var separator)
        {
            lastWhisper = text[..separator].Trim();
            if (currentChat == ChatChannel.Whisper && whisperTarget.Text.Length == 0) whisperTarget.Text = lastWhisper;
        }
        text = text.TrimEnd('\r', '\n'); chatHistory.Enqueue((text, type));
        var added = ChatChannels.Includes(currentChat, type);
        if (added) AddChatLine(text, type);
        if (chatHistory.Count > 100)
        {
            var previous = chatHistory.Dequeue();
            if (ChatChannels.Includes(currentChat, previous.Type))
                for (var lines = previous.Text.Count(letter => letter == '\n') + 1; lines > 0 && messages.GetParagraphCount() > 1; lines--) messages.RemoveParagraph(0);
        }
    }

    private void AddChatLine(string text, ChatType type)
    {
        messages.PushColor(ChatColour(type)); messages.AddText(text + "\n"); messages.Pop();
    }

    private void RenderChatHistory()
    {
        messages.Clear();
        foreach (var (text, type) in chatHistory) if (ChatChannels.Includes(currentChat, type)) AddChatLine(text, type);
        messages.ScrollToLine(Math.Max(0, messages.GetLineCount() - 1));
    }

    private void ResetChat()
    {
        chatHistory.Clear(); lastWhisper = ""; whisperTarget.Clear(); chat.Clear(); SelectChat(ChatChannel.All);
    }

    private void BuildChatResize()
    {
        chatResize = new Control { Name = "ChatResizeHandle", Position = new Vector2(230, 668), Size = new Vector2(629, 4),
            MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.Vsize, TooltipText = "拖动顶部调整聊天高度（最多两倍）" };
        game.AddChild(chatResize);
    }

    private void ChatResizeInput(InputEvent input)
    {
        if (!chatResize.IsVisibleInTree()) { resizingChat = false; return; }
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse)
        {
            if (mouse.Pressed && chatResize.GetGlobalRect().HasPoint(mouse.Position))
            {
                var hovered = GetViewport().GuiGetHoveredControl();
                if (hovered != chatResize && hovered != chatFrame && hovered != messages) return;
                resizingChat = true; chatResizeY = mouse.Position.Y; chatResizeHeight = chatFrame.Size.Y;
                GetViewport().SetInputAsHandled();
            }
            else if (resizingChat && !mouse.Pressed)
            {
                resizingChat = false; GetViewport().SetInputAsHandled();
            }
        }
        else if (resizingChat && input is InputEventMouseMotion motion)
        {
            SetChatHeight(chatResizeHeight + chatResizeY - motion.Position.Y);
            GetViewport().SetInputAsHandled();
        }
    }

    private void SetChatHeight(float height)
    {
        height = Mathf.Round(Mathf.Clamp(height, 68, 136));
        var top = 736 - height;
        chatFrame.Position = new Vector2(230, top); chatFrame.Size = new Vector2(629, height);
        chatResize.Position = new Vector2(230, top);
        messages.Position = new Vector2(234, top + 3); messages.Size = new Vector2(621, height - 26);
        chatChannels.Position = new Vector2(230, top - 20);
        quickBars.Position = new Vector2(0, 44 - height);
    }
}
