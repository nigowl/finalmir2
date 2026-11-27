using Godot;

public partial class ItemTipButton : Button
{
    public UserItem? Item;
    public Func<Control>? TooltipBuilder;
    internal ItemTooltip? OpenTooltip { get; private set; }

    public override void _Draw()
    {
        if (Item?.Info?.IsEquipment != true) return;
        var colour = EquipmentAppraisal.Colour(Item);
        if (Item.EquipmentQuality > EquipmentGrade.Normal && Item.Identified)
            DrawRect(new Rect2(Vector2.One, Size - Vector2.One * 2), new Color(colour.R / 255f, colour.G / 255f, colour.B / 255f, .8f), false);
        else if (EquipmentAppraisal.Pending(Item))
            DrawString(ThemeDB.FallbackFont, new Vector2(Size.X - 9, 10), "?", fontSize: 10, modulate: new Color("c5b99d"));
    }

    public override GodotObject _MakeCustomTooltip(string forText)
    {
        // 列表滚动后原生提示计时器可能仍引用旧行，详情应以当前悬停格为准。
        var source = GetViewport().GuiGetHoveredControl() as ItemTipButton ?? this;
        var content = source.TooltipBuilder?.Invoke() ?? new Label { Text = source == this ? forText : source.TooltipText };
        source.OpenTooltip = content as ItemTooltip;
        return content;
    }

    public override void _Input(InputEvent input)
    {
        if (input is not InputEventMouseButton { Pressed: true } mouse || mouse.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown) ||
            !IsHovered() || !IsInstanceValid(OpenTooltip) || !OpenTooltip!.IsVisibleInTree()) return;
        var bar = OpenTooltip.GetVScrollBar();
        if (bar.MaxValue <= bar.Page) return;
        // 原生提示窗不接收鼠标，滚轮在物品格上处理，避免被视口关闭提示或传给背包列表。
        OpenTooltip.ScrollVertical += mouse.ButtonIndex == MouseButton.WheelDown ? 48 : -48;
        GetViewport().SetInputAsHandled();
    }
}

public partial class ItemTooltip : ScrollContainer
{
    internal const int MaximumHeight = 360;
    public Func<int>? ContentRevision;
    public Action? RefreshContent;
    private int contentRevision = -1;

    public override void _Process(double delta)
    {
        if (ContentRevision == null) return;
        var revision = ContentRevision();
        if (revision == contentRevision) return;
        contentRevision = revision; RefreshContent?.Invoke();
    }

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Disabled;
        if (GetParent() is PopupPanel popup) popup.AddThemeStyleboxOverride("panel", GetThemeStylebox("panel", "GameItemTooltip"));
        GetChild<Control>(0).MinimumSizeChanged += FitHeight;
        FitHeight();
    }

    private void FitHeight()
    {
        var content = GetChild<Control>(0).GetCombinedMinimumSize();
        var size = new Vector2(content.X + GetVScrollBar().GetCombinedMinimumSize().X, Math.Min(MaximumHeight, content.Y));
        if (CustomMinimumSize == size) return;
        CustomMinimumSize = size;
        // 富文本完成换行后最小高度会缩小，原生提示窗也须收缩，不能保留首次排版的空白。
        if (GetParent() is PopupPanel popup) popup.ResetSize();
    }
}
