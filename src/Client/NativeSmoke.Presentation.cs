using Godot;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyLoginPresentation()
    {
        stage = "aligned login actions";
        var buttons = Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Where(button => button.Library == "Title").ToArray();
        if (buttons.Any(button => button.Text.Length == 0)) throw new Exception("界面文字按钮仍直接使用烘焙文字素材。");
        await Until(() => Client.Theme.GetStylebox("normal", "Button") is StyleBoxTexture);
        foreach (var (state, index) in new[] { ("normal", 276), ("hover", 277), ("pressed", 278) })
        {
            if (Client.Theme.GetStylebox(state, "Button") is not StyleBoxTexture style || style.Texture.GetSize() != new Vector2(24, 25))
                throw new Exception("Text actions did not reuse the marked original button states.");
            using var actual = style.Texture.GetImage(); using var source = Client.Assets.Sprite("Title", index)!.Texture.GetImage();
            if (actual.GetPixel(3, 3) != source.GetPixel(3, 3) || actual.GetPixel(21, 5) != source.GetPixel(source.GetWidth() - 3, 5))
                throw new Exception("Processing a button damaged its original border pixels.");
        }
        var register = buttons.Single(button => button.Index == 323); var quit = buttons.Single(button => button.Index == 329);
        if (register.Position.Y != quit.Position.Y || register.Position.Y != 176 || register.GetParent<Control>().GetRect().Size.Y < quit.GetRect().End.Y)
            throw new Exception("Login register/quit art did not share a horizontal baseline inside the lower panel.");
        var connect = Client.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button.Text == "连接");
        var address = connect.GetParent().GetChildren().OfType<LineEdit>().Single();
        if (connect.Size.Y != ClientUi.ButtonHeight || connect.GetRect().GetCenter().Y != address.GetRect().GetCenter().Y)
            throw new Exception($"连接地址和按钮没有对齐：{address.GetRect()} / {connect.GetRect()}。");
        await Screenshot("login-layout");
        GD.Print("PASS native login: original register/quit actions align horizontally within the login frame.");
    }

    private async Task VerifyInventoryPresentation()
    {
        stage = "inventory task hover, gold, weight and free slots";
        var button = Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Single(button => button.TooltipText == "任务物品");
        var bag = button.GetParent<Control>(); bag.Show();
        if (button.Text != "任务物品" || button.GetThemeFontSize("font_size") != ClientUi.Body || button.GetThemeStylebox("normal", "Button") is not StyleBoxTexture)
            throw new Exception("任务入口未使用统一字体及原版棕底金边按钮。");
        var weight = bag.GetNode<Label>("BagWeight"); var free = bag.GetNode<Label>("BagFree");
        var expected = $"{State.InventoryWeight:0.##}/{State.CalculateStats()[Stat.BagWeight]}";
        if (weight.Text != expected || free.Text != State.User!.Inventory.Count(item => item == null).ToString()) throw new Exception("Inventory weight or free-cell count is missing its current value.");
        var gold = bag.FindChildren("*", "Label", true, false).Cast<Label>().Single(label => label.Position == new Vector2(40, 212));
        if (gold.Text != State.User.Gold.ToString() || gold.GetRect().End.X > 151) throw new Exception("Inventory gold overlapped its original icon/field.");
        using var motion = new InputEventMouseMotion { Position = button.GetGlobalRect().GetCenter(), GlobalPosition = button.GetGlobalRect().GetCenter() };
        GetViewport().PushInput(motion, true);
        await Screenshot("inventory-task-hover");
        var warehouse = bag.GetNode<Button>("InventoryStorage");
        if (warehouse.Position.X <= button.Position.X || warehouse.GetRect().End.X > 289) throw new Exception("仓库物品按钮没有放在任务物品旁边。");
        warehouse.EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var storage = Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Single(button => button.TooltipText == "关闭仓库").GetParent<Control>();
        if (!storage.Visible || Client.CanUseStorage || State.StorageActive) throw new Exception("背包仓库入口没有打开预览或绕过了NPC会话。");
        await Screenshot("inventory-storage-preview"); warehouse.EmitSignal(BaseButton.SignalName.Pressed);
        if (storage.Visible) throw new Exception("背包仓库入口再次点击没有关闭。");
        bag.Hide();
        await VerifyItemComparison();
        GD.Print("PASS native inventory: matching original task hover art, aligned gold and populated weight/free-cell fields.");
    }

    private async Task VerifyItemComparison()
    {
        var item = new UserItem(new ItemInfo { Name = "对比测试长剑", Type = ItemType.Weapon, RequiredClass = RequiredClass.Wizard,
            RequiredGender = RequiredGender.Female, RequiredAmount = 100, Stats = new Stats { [Stat.MinDC] = 3, [Stat.MaxDC] = 9, [Stat.MinMC] = 2, [Stat.MaxMC] = 5 } })
            { UniqueID = ulong.MaxValue, Count = 1, CurrentDura = 10000, MaxDura = 10000 };
        var details = Client.BuildItemTooltip(item);
        var wrapper = new PanelContainer { Position = new Vector2(260, 210), MouseFilter = Control.MouseFilterEnum.Ignore };
        wrapper.AddThemeStyleboxOverride("panel", Client.Theme.GetStylebox("panel", "GameItemTooltip")); wrapper.AddChild(details); Client.AddChild(wrapper);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var content = details.GetNode<HBoxContainer>("ItemComparison");
        var columns = content.GetChildren().OfType<VBoxContainer>().Select(column => column.GetChildren().OfType<RichTextLabel>().First()).ToArray();
        if (columns.Length != 2 || !columns[0].GetParsedText().Contains("攻击 3–9") || !columns[0].GetParsedText().Contains("魔法 2–5") ||
            columns[0].GetParsedText().Contains("×1") || !columns[1].GetParsedText().Contains("已装备") || wrapper.GetRect().End.X > 1024)
            throw new Exception("装备对比缺少区间、已有装备或超出逻辑画布。");
        if (content.GetChildren().OfType<VSeparator>().Count() != 1 || details.FindChildren("*", "HSeparator", true, false).Count != 1 ||
            details.FindChild("ComparisonDifference", true, false) is not RichTextLabel diff || !diff.GetParsedText().Contains("换上左侧装备后"))
            throw new Exception("装备对比列间或底部差异缺少独立分隔线。");
        await Screenshot("equipment-comparison"); wrapper.QueueFree();
        await VerifyItemTooltipScroll(item);
        GD.Print("PASS native item presentation: quantity labels, red unmet requirements, stat ranges, equipped comparison and NPC-protected storage preview.");
    }
}
