using Godot;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private OriginalButton GameArt(string hint) => Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Single(button => button.TooltipText == hint);

    private async Task VerifyTypography()
    {
        stage = "consistent typography, button padding and pixel-aligned dialog headers";
        var bag = GameArt("关闭背包").GetParent<Control>(); var equipment = GameArt("关闭角色").GetParent<Control>();
        var bagPosition = bag.Position; var equipmentPosition = equipment.Position;
        var bagVisible = bag.Visible; var equipmentVisible = equipment.Visible;
        var tabs = equipment.GetChildren().OfType<OriginalButton>().Where(button => button.ToggleMode).OrderBy(button => button.Position.X).ToArray();
        var previousTab = Array.FindIndex(tabs, tab => tab.ButtonPressed);
        try
        {
            bag.Show(); tabs[0].EmitSignal(BaseButton.SignalName.Pressed); Client.OpenQuestJournal();
            var journal = GameArt("关闭任务").GetParent<Control>();
            bag.Position = new Vector2(32, 44); journal.Position = new Vector2(364, 44); equipment.Position = new Vector2(700, 180);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            CheckDialogTypography(bag, journal, equipment, tabs);
            await VerifyTabMouseStates(tabs);
            await Screenshot("typography-overview");
            var task = bag.GetNode<OriginalButton>("InventoryQuestItems"); var rect = task.GetRect();
            await MoveMouse(task.GetGlobalRect().GetCenter());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (task.GetRect() != rect) throw new Exception("任务物品入口悬停后移动或改变尺寸。");
            await Screenshot("typography-buttons-hover");
            tabs[1].EmitSignal(BaseButton.SignalName.Pressed);
            if (tabs.Count(tab => tab.ButtonPressed) != 1 || !tabs[1].ButtonPressed) throw new Exception("角色页签没有唯一的选中高亮。");
            await Screenshot("typography-attributes");
            await MoveMouse(tabs[2].GetGlobalRect().GetCenter());
            await Screenshot("typography-tab-hover");
            await VerifyGenericSkinSizes();
            foreach (var popup in Client.FindChildren("*", "PopupMenu", true, false).OfType<PopupMenu>())
                if (popup.GetThemeFont("font").GetInstanceId() != Client.Theme.DefaultFont.GetInstanceId() || popup.GetThemeFontSize("font_size") != ClientUi.Body)
                    throw new Exception("弹出菜单没有使用统一中文字体和正文字号。");
            var password = GameArt("修改仓库密码"); var invite = GameArt("邀请加入行会");
            var recruit = invite.GetParent().GetChildren().OfType<LineEdit>().Single();
            if (password.Text != "密码" || password.Size != new Vector2(48, ClientUi.ButtonHeight) || invite.Text != "邀请" ||
                invite.Size != new Vector2(60, ClientUi.ButtonHeight) || invite.GetRect().GetCenter().Y != recruit.GetRect().GetCenter().Y)
                throw new Exception("仓库密码/行会邀请仍使用烘焙文字或未与输入框对齐。");
        }
        finally
        {
            GameArt("关闭任务").EmitSignal(BaseButton.SignalName.Pressed);
            if (previousTab >= 0) tabs[previousTab].EmitSignal(BaseButton.SignalName.Pressed);
            bag.Position = bagPosition; equipment.Position = equipmentPosition; bag.Visible = bagVisible; equipment.Visible = equipmentVisible;
        }
        GD.Print("PASS native typography: shared CJK font, 12/11/10/9 roles, aligned inventory actions/headers, fixed three-state padding, bounded selected tabs and composed dialog screenshots.");
    }

    private void CheckDialogTypography(Control bag, Control journal, Control equipment, OriginalButton[] tabs)
    {
        if (Client.Theme.DefaultFontSize != ClientUi.Body || Client.Theme.GetFontSize("title_font_size", "Window") != ClientUi.Title)
            throw new Exception("全局正文和弹窗标题未使用统一字号。");
        var task = bag.GetNode<OriginalButton>("InventoryQuestItems"); var warehouse = bag.GetNode<Button>("InventoryStorage");
        if (task.Size != new Vector2(82, ClientUi.ButtonHeight) || task.Size != warehouse.Size || task.Position.Y != warehouse.Position.Y || warehouse.Position.X - task.GetRect().End.X != ClientUi.Gap)
            throw new Exception($"背包任务/仓库入口的高度、水平基线或4px间距不一致：{task.GetRect()} / {warehouse.GetRect()}。");
        var bagArt = bag.GetChildren().OfType<TextureRect>().Single();
        if (bag.GetThemeStylebox("panel") is not StyleBoxEmpty || bagArt.Size != bagArt.Texture.GetSize() || bagArt.Size != new Vector2(313, 235))
            throw new Exception("背包仍在原边框下面叠画通用外框，或拉伸了带透明留白的原图。");
        foreach (var title in new[] { bag.GetNode<Label>("WindowTitle"), journal.GetNode<Label>("QuestWindowTitle"), equipment.GetNode<Label>("WindowTitle") })
            if (title.GetThemeFontSize("font_size") != ClientUi.Title || title.Position.Y != 4 || title.Size.Y != 24 || title.VerticalAlignment != VerticalAlignment.Center)
                throw new Exception("背包/任务/角色标题的字体或基线不一致。");
        var actions = Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Where(button => button.Text.Length > 0).Cast<Button>().Append(warehouse);
        foreach (var button in actions)
        {
            if (button.GetThemeFontSize("font_size") != ClientUi.Body || button.GetThemeFont("font").GetInstanceId() != Client.Theme.DefaultFont.GetInstanceId())
                throw new Exception($"{button.TooltipText}未使用统一中文字体或按钮字号。");
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
            {
                var verticalPadding = button.ThemeTypeVariation == "GameTab" ? 1 : 3;
                if (button.GetThemeStylebox(state) is not StyleBoxTexture style || style.ContentMarginLeft != 8 || style.ContentMarginRight != 8 || style.ContentMarginTop != verticalPadding || style.ContentMarginBottom != verticalPadding)
                    throw new Exception($"{button.TooltipText}/{state}未使用原版三态或固定内边距。");
            }
        }
        if (tabs.Length != 4 || tabs.Count(tab => tab.ButtonPressed) != 1 || tabs.Any(tab => tab.Size != new Vector2(59, ClientUi.TabHeight) || tab.Position.Y != 68 || tab.ThemeTypeVariation != "GameTab") ||
            tabs[0].Position.X != 11 || tabs[^1].GetRect().End.X != equipment.Size.X - 11)
            throw new Exception("角色四个页签的尺寸、对齐或选中状态不一致。");
        for (var i = 1; i < tabs.Length; i++)
            if (tabs[i].Position.X - tabs[i - 1].GetRect().End.X != 2) throw new Exception("角色页签没有保持2px间距或发生重叠。");
        var selectedStyle = (StyleBoxTexture)tabs[0].GetThemeStylebox("pressed");
        var normalStyle = (StyleBoxTexture)warehouse.GetThemeStylebox("normal");
        var tabButtons = Client.FindChildren("*", "Button", true, false).OfType<Button>().Where(button => button.ThemeTypeVariation == "GameTab").ToArray();
        if (tabButtons.Length != 13 || tabButtons.Any(button => button.Size.Y != ClientUi.TabHeight) || selectedStyle.Texture.GetInstanceId() == normalStyle.Texture.GetInstanceId())
            throw new Exception("人物/任务/仓库/行会页签没有与普通操作按钮区分外观或保持紧凑高度。");
        CheckOriginalTabArtwork(equipment, tabs[0]);
    }

    private void CheckOriginalTabArtwork(Control equipment, Button tab)
    {
        var active = (StyleBoxTexture)tab.GetThemeStylebox("pressed");
        var inactive = (StyleBoxTexture)tab.GetThemeStylebox("normal");
        if (Client.Theme.GetStylebox("tab_selected", "TabContainer").GetInstanceId() != active.GetInstanceId())
            throw new Exception("游戏设置没有共用角色原素材的页签样式。");
        using var selectedSource = Client.Assets.Sprite("Title", 500)!.Texture.GetImage();
        using var panelSource = Client.Assets.Sprite("Title", 504)!.Texture.GetImage();
        using var outline = active.Texture.GetImage();
        foreach (var (style, source, origin, width) in new[] { (active, selectedSource, new Vector2I(1, 0), 60), (inactive, panelSource, new Vector2I(9, 70), 60) })
        {
            using var image = style.Texture.GetImage();
            if (image.GetSize() != new Vector2I(17, 20) || style.TextureMarginLeft != 8 || style.TextureMarginRight != 8)
                throw new Exception("通用页签没有保留完整高度和独立边饰，或把边框拉进了中间填充区。");
            for (var y = 0; y < 20; y++)
            for (var x = 0; x < 8; x++)
                if (!SamePixel(image.GetPixel(x, y), style == inactive && outline.GetPixel(x, y).A == 0 ? Colors.Transparent : source.GetPixel(origin.X + x, origin.Y + y)) ||
                    !SamePixel(image.GetPixel(x + 9, y), style == inactive && outline.GetPixel(x + 9, y).A == 0 ? Colors.Transparent : source.GetPixel(origin.X + width - 8 + x, origin.Y + y)) ||
                    !SamePixel(image.GetPixel(8, y), style == inactive && outline.GetPixel(8, y).A == 0 ? Colors.Transparent : source.GetPixel(origin.X + 8, origin.Y + y)))
                    throw new Exception("去字损坏了原角色页签的圆角、金边或渐变。");
            if (Enumerable.Range(0, 17).Any(x => image.GetPixel(x, 10).A != 1))
                throw new Exception("页签中线仍有状态相关的透明留白，导致选中后可见间隔不同。");
        }
        static bool SamePixel(Color actual, Color expected) => actual == expected || actual.A == 0 && expected.A == 0;
        var background = equipment.GetChildren().OfType<TextureRect>().Single();
        using var clean = background.Texture.GetImage();
        if (clean.GetPixel(3, 3) != panelSource.GetPixel(3, 3) || clean.GetPixel(32, 78) != panelSource.GetPixel(132, 100))
            throw new Exception("角色底图清字损坏外框，或仍残留烘焙页签文字。");
        for (var y = 68; y < 100; y++)
        foreach (var x in Enumerable.Range(0, 11).Concat(Enumerable.Range(253, 11)))
            if (!SamePixel(clean.GetPixel(x, y), panelSource.GetPixel(x, 100)))
                throw new Exception("角色页签两侧的原边框断开，或仍残留旧页签折线。");
    }
}
