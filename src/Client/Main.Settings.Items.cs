using Crystal.Native;
using Godot;

public partial class Main
{
    private Tree itemRuleTable = null!;
    private LineEdit itemRuleSearch = null!;
    private CheckBox itemRuleOwnedOnly = null!;
    private Dictionary<string, ItemActions> itemRuleDraft = new(StringComparer.OrdinalIgnoreCase);

    private void BuildItemSettings(Control page)
    {
        autoSell = SettingToggle(page, "自动回收（手动、挂机均生效）", 2); autoSell.Name = "AutoRecycle";
        autoSell.TooltipText = "按标准售价的一半回收，无须商人。未鉴定装备仅明确勾选后按普通属性计价，可能失去隐藏品质；已鉴定高品质及任务、绑定、强化物品保留。";
        itemRuleSearch = new LineEdit { Name = "ItemRuleSearch", Position = new Vector2(10, 32), Size = new Vector2(214, 25), PlaceholderText = "搜索物品名称" };
        page.AddChild(itemRuleSearch); itemRuleSearch.TextChanged += _ => FillItemRuleRows();
        itemRuleOwnedOnly = new CheckBox { Name = "ItemRuleOwnedOnly", Text = "仅显示背包物品", Position = new Vector2(230, 31), Size = new Vector2(170, 28), FocusMode = FocusModeEnum.None };
        StyleCheckBox(itemRuleOwnedOnly); page.AddChild(itemRuleOwnedOnly);
        itemRuleOwnedOnly.Toggled += _ => FillItemRuleRows();
        itemRuleTable = new Tree { Name = "ItemRuleTable", Position = new Vector2(10, 62), Size = new Vector2(390, 144),
            Columns = 4, ColumnTitlesVisible = true, HideRoot = true, HideFolding = true };
        page.AddChild(itemRuleTable);
        string[] titles = ["显示", "拣取", "回收", "物品"];
        for (var i = 0; i < 4; i++)
        {
            itemRuleTable.SetColumnTitle(i, titles[i]); itemRuleTable.SetColumnExpand(i, i == 3);
            itemRuleTable.SetColumnTitleAlignment(i, HorizontalAlignment.Left);
            itemRuleTable.SetColumnCustomMinimumWidth(i, i == 3 ? 200 : 50);
        }
        itemRuleTable.AddThemeIconOverride("checked", checkedIcon); itemRuleTable.AddThemeIconOverride("unchecked", uncheckedIcon);
        itemRuleTable.AddThemeConstantOverride("v_separation", 2);
        itemRuleTable.AddThemeConstantOverride("indent", 0);
        itemRuleTable.ItemEdited += () =>
        {
            var row = itemRuleTable.GetEdited(); var column = itemRuleTable.GetEditedColumn();
            if (row == null || column is < 0 or > 2) return;
            SetItemRule(row.GetText(3), column, row.IsChecked(column));
        };
        Text(page, "勾选后自动保存；回收任务、绑定、强化物品时会保留。", 12, 207, 385).AddThemeFontSizeOverride("font_size", ClientUi.Small);
    }

    private void SetItemRule(string name, int column, bool enabled)
    {
        var flag = (ItemActions)(1 << column);
        var current = itemRuleDraft.GetValueOrDefault(name, preferences.ForItem(name));
        itemRuleDraft[name] = enabled ? current | flag : current & ~flag;
        recycleRejected.Clear(); SettingsChanged();
    }

    private void FillItemRuleRows()
    {
        if (itemRuleTable == null) return;
        var owned = State.User?.Inventory.Concat(State.User.QuestInventory).Where(item => item?.Info != null).Select(item => item.Info.FriendlyName).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        itemRuleTable.Clear();
        var root = itemRuleTable.CreateItem();
        var names = (itemRuleOwnedOnly.ButtonPressed ? owned : new[] { "金币" }.Concat(Assets.ItemCatalog.Select(item => item.Name)))
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => name.Contains(itemRuleSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase)).OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var row = itemRuleTable.CreateItem(root);
            var flags = itemRuleDraft.GetValueOrDefault(name, preferences.ForItem(name));
            for (var column = 0; column < 3; column++)
            {
                row.SetCellMode(column, TreeItem.TreeCellMode.Check); row.SetEditable(column, true);
                row.SetChecked(column, flags.HasFlag((ItemActions)(1 << column)));
                row.SetTextAlignment(column, HorizontalAlignment.Center);
            }
            row.SetText(3, name); row.SetTooltipText(3, name);
        }
    }
}
