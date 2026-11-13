using Godot;

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
    private bool resizingChat;
    private float chatResizeY, chatResizeHeight;

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
        quickBars.Position = new Vector2(0, 68 - height);
    }
}
