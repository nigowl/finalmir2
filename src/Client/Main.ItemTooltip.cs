using Crystal.Native;
using Godot;

public partial class Main
{
    private string ItemText(UserItem item) => string.Join('\n', new[] { ItemPresentation.Name(item) }
        .Append(ItemPresentation.Category(item)).Concat(ItemPresentation.Stats(item)).Concat(ItemPresentation.Requirements(item, State, HeroInventoryContext).Select(requirement => requirement.Text))
        .Append(ItemPresentation.Details(item)).Append(item.Info?.ToolTip ?? ""));

    private void ItemTooltip(Button button, UserItem? item, string empty = "空格")
    {
        button.TooltipText = item == null ? empty : ItemText(item);
        if (button is ItemTipButton rich) { rich.Item = item; rich.TooltipBuilder = item == null ? null : () => BuildItemTooltip(item); rich.QueueRedraw(); }
    }

    internal Control BuildItemTooltip(UserItem item)
    {
        var scroll = new ItemTooltip { Name = "ItemTooltipScroll", Theme = Theme };
        var columns = new HBoxContainer { Name = "ItemComparison" }; columns.AddThemeConstantOverride("separation", 14);
        scroll.AddChild(columns);
        void Populate()
        {
            foreach (var child in columns.GetChildren()) { columns.RemoveChild(child); child.QueueFree(); }
            AddItemTooltipColumn(columns, item, "物品详情");
            if (item.Info == null || State.User == null || SubjectEquipment.Any(worn => worn?.UniqueID == item.UniqueID)) return;
            foreach (var slot in ItemPresentation.Slots(item.Info.Type))
                if (SubjectEquipment.ElementAtOrDefault(slot) is { } worn && worn.Info != null)
                {
                    columns.AddChild(new ItemTooltipSeparator { Vertical = true });
                    AddItemTooltipColumn(columns, worn, $"已装备 · {((EquipmentSlot)slot).ToLocalizedString()}", item);
                }
        }
        scroll.ContentRevision = () => HashCode.Combine(State.ItemOwnerNameRevision, State.InventoryRevision, HeroInventoryContext); scroll.RefreshContent = Populate;
        Populate();
        return scroll;
    }

    private void AddItemTooltipColumn(Control parent, UserItem item, string title, UserItem? candidate = null)
    {
        if (HandshakeReady && Connection.Connected && State.RequestItemOwnerName(item.SoulBoundId))
            Send(new ClientPackets.RequestUserName { UserID = (uint)item.SoulBoundId });
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(200, 0) }; parent.AddChild(column);
        var text = new RichTextLabel { FitContent = true, ScrollActive = false,
            MouseFilter = MouseFilterEnum.Ignore, Name = candidate == null ? "ItemDetails" : "EquippedComparison" };
        text.AddThemeFontSizeOverride("normal_font_size", 11); column.AddChild(text);
        void Line(string value, Color? colour = null) { text.PushColor(colour ?? Colors.White); text.AddText(value + "\n"); text.Pop(); }
        var qualityColour = EquipmentAppraisal.Colour(item);
        var colour = new Color(qualityColour.R / 255f, qualityColour.G / 255f, qualityColour.B / 255f);
        Line(title); Line(ItemPresentation.Name(item), colour);
        if (item.Info?.IsEquipment == true)
        {
            text.PushColor(Colors.White); text.AddText(item.Info.Type.ToLocalizedString() + " · "); text.Pop();
            Line(EquipmentAppraisal.Name(item), colour);
        }
        else Line(ItemPresentation.Category(item));
        foreach (var line in ItemPresentation.StatLines(item))
        {
            foreach (var part in line)
            {
                text.PushColor(part.Quality ? colour : Colors.White);
                text.AddText(part.Text); text.Pop();
            }
            text.AddText("\n");
        }
        Line(ItemPresentation.Details(item));
        foreach (var requirement in ItemPresentation.Requirements(item, State, HeroInventoryContext)) Line(requirement.Text, requirement.Met ? null : new Color("ff6060"));
        if (!string.IsNullOrWhiteSpace(item.Info?.ToolTip)) Line(item.Info.ToolTip);
        if (candidate != null)
        {
            column.AddChild(new ItemTooltipSeparator());
            text = new RichTextLabel { Name = "ComparisonDifference", FitContent = true, ScrollActive = false, MouseFilter = MouseFilterEnum.Ignore };
            text.AddThemeFontSizeOverride("normal_font_size", 11); column.AddChild(text);
            Line("对比");
            foreach (var line in ItemPresentation.ComparisonLines(candidate, item))
            {
                foreach (var part in line)
                {
                    text.PushColor(part.Change > 0 ? new Color("78d888") : part.Change < 0 ? new Color("ff6060") : Colors.White);
                    text.AddText(part.Text); text.Pop();
                }
                text.AddText("\n");
            }
        }
    }
}
