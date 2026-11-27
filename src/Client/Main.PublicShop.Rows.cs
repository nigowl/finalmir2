using Godot;

public partial class Main
{
    private readonly HashSet<string> publicShopExpanded = new(StringComparer.Ordinal);

    private void RenderPublicShopGroups()
    {
        var offers = (State.PublicShop?.Items ?? []).Where(offer => offer.Category == publicShopCategory && State.Items.ContainsKey(offer.ItemIndex));
        foreach (var group in offers.GroupBy(offer => PublicShopRules.GroupName(State.Items[offer.ItemIndex]))
            .OrderBy(group => MagicBloodStone.IsStone(State.Items[group.First().ItemIndex]) ? 0 : 1))
        {
            if (group.Count() == 1) { PublicShopVariant(publicShopRows, group.Single()); continue; }
            var prices = group.Select(offer => offer.Price).ToArray();
            var price = prices.Min() == prices.Max() ? $"{prices[0]:N0}" : $"{prices.Min():N0}–{prices.Max():N0}";
            var info = State.Items[group.First().ItemIndex];
            var header = new Button { Name = "PublicShopGroup", Text = $"{(publicShopExpanded.Contains(group.Key) ? "－" : "＋")} {group.Key} · {price} 金币",
                Alignment = HorizontalAlignment.Left, ToggleMode = true, FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(0, 30), SizeFlagsHorizontal = SizeFlags.ExpandFill, ExpandIcon = true };
            CellStyle(header, 5); header.AddThemeConstantOverride("icon_max_width", 24); publicShopRows.AddChild(header);
            var sample = new UserItem(info) { Count = PublicShopRules.UnitCount(info) }; icons.Add((header, sample));
            header.SetPressedNoSignal(publicShopExpanded.Contains(group.Key));
            var entries = new VBoxContainer { Name = "PublicShopVariants", Visible = header.ButtonPressed, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            entries.AddThemeConstantOverride("separation", 4); publicShopRows.AddChild(entries);
            header.Toggled += expanded =>
            {
                if (expanded) publicShopExpanded.Add(group.Key); else publicShopExpanded.Remove(group.Key);
                entries.Visible = expanded; header.Text = $"{(expanded ? "－" : "＋")} {group.Key} · {price} 金币";
            };
            foreach (var entry in group.OrderBy(entry => entry.Price).ThenBy(entry => entry.ItemIndex)) PublicShopVariant(entries, entry);
        }
    }

    private void PublicShopVariant(Control parent, PublicShopEntry entry)
    {
        var info = State.Items[entry.ItemIndex];
        var row = new HBoxContainer { Name = "PublicShopVariant", CustomMinimumSize = new Vector2(0, 38) };
        row.AddThemeConstantOverride("separation", 5); parent.AddChild(row);
        var sample = new UserItem(info) { Count = PublicShopRules.UnitCount(info), CurrentDura = info.Durability, MaxDura = info.Durability };
        var description = new ItemTipButton { Name = "PublicShopItem", Text = $"{info.FriendlyName}\n{entry.Price:N0} 金币  {PublicShopRules.UnitText(info)}",
            Alignment = HorizontalAlignment.Left, SizeFlagsHorizontal = SizeFlags.ExpandFill, ExpandIcon = true,
            ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.Arrow };
        CellStyle(description, 12); description.AddThemeConstantOverride("icon_max_width", 24); row.AddChild(description);
        icons.Add((description, sample)); ItemTooltip(description, sample);
        var quantities = new HBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        quantities.AddThemeConstantOverride("separation", 3); row.AddChild(quantities);
        foreach (var count in new[] { 1, 10, 100 })
        {
            var buy = NpcLink($"x{count}", () => ShowPublicShopPurchase(entry, count)); buy.Name = $"PublicShopBuy{count}";
            buy.AddThemeColorOverride("font_disabled_color", new Color("867c68"));
            buy.CustomMinimumSize = new Vector2(count == 1 ? 18 : count == 10 ? 24 : 30, 26);
            buy.Disabled = publicPurchases.Pending || State.Me?.Dead != false || count > entry.MaxCount || (ulong)entry.Price * (uint)count > (State.User?.Gold ?? 0);
            buy.TooltipText = $"购买 {count}{(info.Type == ItemType.Amulet ? "包" : "件")} · {(ulong)entry.Price * (uint)count:N0} 金币"; quantities.AddChild(buy);
        }
    }

    private void ShowPublicShopPurchase(PublicShopEntry entry, int count)
    {
        if (appraisalPending != null || publicPurchases.Pending || State.Me?.Dead != false) return;
        publicShopSelection = entry;
        publicShopCount.MaxValue = Math.Min(entry.MaxCount, (State.User?.Gold ?? 0) / Math.Max(1, entry.Price));
        publicShopCount.Value = count; RefreshPublicShopConfirmation(); publicShopConfirm.PopupCentered();
    }
}
