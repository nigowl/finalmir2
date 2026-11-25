using Godot;
using Crystal.Native;
using C = ClientPackets;

public partial class Main
{
    private ConfirmationDialog deathDialog = null!, sessionDialog = null!;
    private int sessionAction;
    private const Key ControlKey = (Key)KeyModifierMask.MaskCtrl;

    private void BuildGameMenu()
    {
        gameMenu = new PopupMenu { Name = "GameMenu" }; game.AddChild(gameMenu);
        var title = new Label { Text = "菜单", Position = new Vector2(28, 4), Size = new Vector2(180, 24), VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        title.AddThemeFontSizeOverride("font_size", ClientUi.Title); gameMenu.AddChild(title);
        var close = new OriginalButton { Name = "GameMenuClose", Assets = Assets, Library = "Prguse2", Index = 360,
            HoverIndex = 360, PressedIndex = 362, TooltipText = "关闭菜单", Size = new Vector2(24, 24) };
        gameMenu.AddChild(close); close.Pressed += () => gameMenu.Hide();
        gameMenu.SizeChanged += () => close.Position = new Vector2(gameMenu.Size.X - 28, 4);
        gameMenu.AddSeparator("战斗", 100);
        AddMenuItem("开始挂机", 7, ControlKey | Key.Z);
        BuildAttackModes();
        AddMenuItem("拾取", 0, Key.G);
        gameMenu.AddSeparator("界面", 101);
        AddMenuItem("背包", 9, Key.I); AddMenuItem("角色属性", 10, Key.C); AddMenuItem("技能", 11, Key.K);
        AddMenuItem("任务", 12, Key.Q); AddMenuItem("地图", 14, Key.M); AddMenuItem("游戏设置", 13, ControlKey | Key.O);
        gameMenu.AddSeparator("社交", 102);
        AddMenuItem("组队", 4, Key.P); AddMenuItem("交易", 5, Key.T); AddMenuItem("行会", 6, Key.H);
        gameMenu.AddItem("好友", 15); gameMenu.AddItem("消息", 16); gameMenu.AddItem("宠物", 17); gameMenu.AddItem("英雄", 18);
        gameMenu.AddSeparator("角色", 103);
        AddMenuItem("切换角色", 2, ControlKey | Key.L); AddMenuItem("退出游戏", 3, ControlKey | Key.Q);
        gameMenu.IdPressed += id => MenuAction((int)id);
        gameMenu.AboutToPopup += RefreshBattleMenu;
        gameMenuButton = new MenuIconButton { Name = "GameMenuButton", Assets = Assets, Library = "Prguse", Index = 1960,
            Position = new Vector2(972, 654), ArtSize = ClientUi.HudActionSize, Size = Vector2.One * ClientUi.HudActionSize, TooltipText = "菜单 Ctrl+E" };
        game.AddChild(gameMenuButton); gameMenuButton.Pressed += ToggleGameMenu;
        deathDialog = new ConfirmationDialog { Name = "DeathConfirmation", Title = "角色阵亡", DialogText = "您已阵亡，是否回城复活？\n取消后可按 R 再次打开。" };
        game.AddChild(deathDialog); deathDialog.GetOkButton().Text = "回城复活"; deathDialog.GetCancelButton().Text = "暂不复活";
        deathDialog.Confirmed += () => { if (scene == "game" && State.Me?.Dead == true) Send(new C.TownRevive()); };
        sessionDialog = new ConfirmationDialog { Name = "SessionConfirmation" }; game.AddChild(sessionDialog);
        sessionDialog.GetOkButton().Text = "确定"; sessionDialog.GetCancelButton().Text = "取消";
        sessionDialog.Confirmed += () => { if (sessionAction == 2) Send(new C.LogOut()); else _ = Quit(); };
    }

    private void AddMenuItem(string text, int id, Key key) { gameMenu.AddItem(text, id); gameMenu.SetItemAccelerator(gameMenu.GetItemIndex(id), key); }
    private void RefreshBattleMenu() => gameMenu.SetItemText(gameMenu.GetItemIndex(7), autoBattleEnabled ? "停止挂机" : "开始挂机");
    private void ToggleGameMenu() { if (gameMenu.Visible) gameMenu.Hide(); else gameMenu.PopupCentered(); }
    private void ShowDeathDialog() { if (scene == "game" && State.Me?.Dead == true) deathDialog.PopupCentered(); }

    private void MenuAction(int id)
    {
        switch (id)
        {
            case 0: Send(new C.PickUp()); break;
            case 2: case 3:
                sessionAction = id; sessionDialog.Title = id == 2 ? "切换角色" : "退出游戏";
                sessionDialog.DialogText = id == 2 ? "返回角色选择界面，切换其他角色？" : "确定退出游戏？";
                sessionDialog.PopupCentered(); break;
            case 4: if (group.Visible) group.Hide(); else OpenGroup(); break;
            case 5: OpenTrade(); break;
            case 6: if (guild.Visible) CloseGuild(); else OpenGuild(); break;
            case 7: ToggleAutoBattle(); break;
            case 9: inventory.Visible = !inventory.Visible; break;
            case 10: ToggleEquipmentPage(0); break;
            case 11: ToggleEquipmentPage(3); break;
            case 12: if (questList.Visible) CloseQuests(); else OpenQuestJournal(); break;
            case 13: if (settingsDialog.Visible) CloseSettings(); else OpenSettings(); break;
            case 14: MiniMap.SetExpanded(!MiniMap.Expanded); break;
            case >= 15 and <= 18: ToggleSocial(id - 15); break;
        }
    }

    private bool MenuShortcut(InputEventKey key)
    {
        if (key.CtrlPressed)
        {
            switch (key.PhysicalKeycode)
            {
                case Key.Z: MenuAction(7); break;
                case Key.H: Send(new C.ChangeAMode { Mode = (AttackMode)(((int)State.CurrentAttackMode + 1) % Assets.AttackModes) }); break;
                case Key.O: MenuAction(13); break;
                case Key.E: ToggleGameMenu(); break;
                case Key.L: MenuAction(2); break;
                case Key.Q: MenuAction(3); break;
            }
            return true;
        }
        if (key.PhysicalKeycode == Key.R && State.Me?.Dead == true) { ShowDeathDialog(); return true; }
        return false;
    }
}
