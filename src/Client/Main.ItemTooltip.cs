using Crystal.Native;
using Godot;

public partial class Main
{
    private string ItemText(UserItem item) => string.Join('\n', new[] { ItemPresentation.Name(item) }
        .Append(ItemPresentation.Category(item)).Concat(ItemPresentation.Stats(item)).Concat(ItemPresentation.Requirements(item, State).Select(requirement => requirement.Text))
        .Append(ItemPresentation.Details(item)).Append(item.Info?.ToolTip ?? ""));

    private void ItemTooltip(Button button, UserItem? item, string empty = "空格")
    {
        button.TooltipText = item == null ? empty : ItemText(item);
        if (button is ItemTipButton rich) rich.TooltipBuilder = item == null ? null : () => BuildItemTooltip(item);
    }

    internal Control BuildItemTooltip(UserItem item)
    {
        var scroll = new ItemTooltip { Name = "ItemTooltipScroll", Theme = Theme };
        var columns = new HBoxContainer { Name = "ItemComparison" }; columns.AddThemeConstantOverride("separation", 14);
        scroll.AddChild(columns);
        AddItemTooltipColumn(columns, item, "物品详情");
        if (item.Info == null || State.User == null || State.User.Equipment.Any(worn => worn?.UniqueID == item.UniqueID)) return scroll;
        foreach (var slot in ItemPresentation.Slots(item.Info.Type))
            if (State.User.Equipment.ElementAtOrDefault(slot) is { } worn && worn.Info != null)
            {
                columns.AddChild(new VSeparator());
                AddItemTooltipColumn(columns, worn, $"已装备 · {((EquipmentSlot)slot).ToLocalizedString()}", item);
            }
        return scroll;
    }

    private void AddItemTooltipColumn(Control parent, UserItem item, string title, UserItem? candidate = null)
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(200, 0) }; parent.AddChild(column);
        var text = new RichTextLabel { FitContent = true, ScrollActive = false,
            MouseFilter = MouseFilterEnum.Ignore, Name = candidate == null ? "ItemDetails" : "EquippedComparison" };
        text.AddThemeFontSizeOverride("normal_font_size", 11); column.AddChild(text);
        void Line(string value, Color? colour = null) { text.PushColor(colour ?? new Color("eadbb6")); text.AddText(value + "\n"); text.Pop(); }
        Line(title, Colors.Wheat); Line(ItemPresentation.Name(item), Colors.White);
        Line(ItemPresentation.Category(item));
        foreach (var value in ItemPresentation.Stats(item)) Line(value);
        Line(ItemPresentation.Details(item));
        foreach (var requirement in ItemPresentation.Requirements(item, State)) Line(requirement.Text, requirement.Met ? null : new Color("ff6060"));
        if (!string.IsNullOrWhiteSpace(item.Info?.ToolTip)) Line(item.Info.ToolTip);
        if (candidate != null)
        {
            column.AddChild(new HSeparator());
            text = new RichTextLabel { Name = "ComparisonDifference", FitContent = true, ScrollActive = false, MouseFilter = MouseFilterEnum.Ignore };
            text.AddThemeFontSizeOverride("normal_font_size", 11); column.AddChild(text);
            Line("换上左侧装备后", Colors.Wheat);
            foreach (var (value, change) in ItemPresentation.Comparison(candidate, item))
                Line(value, change > 0 ? new Color("78d888") : change < 0 ? new Color("ff6060") : Colors.Wheat);
        }
    }
}
