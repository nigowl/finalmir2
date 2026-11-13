using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyAttackModes()
    {
        stage = "attack mode submenu and authoritative TCP selection";
        var menu = (PopupMenu)Client.FindChild("GameMenu", true, false);
        if (Enumerable.Range(0, menu.ItemCount).Count(menu.IsItemSeparator) != 4 || Enumerable.Range(0, menu.ItemCount).Any(i => menu.GetItemText(i).Contains("回城复活")) ||
            menu.GetItemText(menu.GetItemIndex(2)) != "切换角色" || menu.GetItemAccelerator(menu.GetItemIndex(7)) != (Key)((long)KeyModifierMask.MaskCtrl | (long)Key.Z) ||
            Client.FindChild("QuickBars", true, false).GetChildren().OfType<Label>().Any()) throw new Exception("菜单分组/快捷键或快捷栏说明清理不正确。");
        void KeyPress(Key code, bool control = true)
        { using var key = new InputEventKey { PhysicalKeycode = code, CtrlPressed = control, Pressed = true }; Client._UnhandledInput(key); }
        KeyPress(Key.Z);
        if (menu.GetItemText(menu.GetItemIndex(7)) != "停止挂机") throw new Exception("Ctrl+Z未开始挂机。");
        var chat = Client.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().Single(input => input.PlaceholderText.StartsWith("聊天"));
        chat.GrabFocus(); KeyPress(Key.Z); chat.ReleaseFocus();
        if (menu.GetItemText(menu.GetItemIndex(7)) != "停止挂机") throw new Exception("聊天输入未忽略挂机快捷键。");
        KeyPress(Key.Z);
        if (menu.GetItemText(menu.GetItemIndex(7)) != "开始挂机") throw new Exception("Ctrl+Z未停止挂机。");
        KeyPress(Key.L);
        var session = (ConfirmationDialog)Client.FindChild("SessionConfirmation", true, false);
        if (!session.Visible || session.Title != "切换角色") throw new Exception("切换角色快捷键未显示确认。");
        session.GetCancelButton().EmitSignal(BaseButton.SignalName.Pressed);
        var modes = menu.GetNode<PopupMenu>("AttackModes");
        if (menu.GetItemSubmenuNode(menu.GetItemIndex(8)) != modes || modes.ItemCount != 8)
            throw new Exception("游戏菜单未显示完整攻击模式二级菜单。");
        foreach (var mode in Enum.GetValues<AttackMode>())
        {
            packets.RemoveAll(packet => packet is S.ChangeAMode);
            modes.EmitSignal(PopupMenu.SignalName.IdPressed, (long)mode);
            if ((await Packet<S.ChangeAMode>()).Mode != mode || State.CurrentAttackMode != mode ||
                !modes.IsItemChecked(modes.GetItemIndex((int)mode))) throw new Exception("攻击模式未按服务器回执更新选中状态。");
        }
        packets.RemoveAll(packet => packet is S.Chat);
        Client.Send(new C.ChangeAMode { Mode = (AttackMode)255 });
        await Until(() => packets.OfType<S.Chat>().Any(chat => chat.Message.Contains("攻击模式无效")));
        if (State.CurrentAttackMode != AttackMode.Mentor) throw new Exception("非法模式改变了服务器/客户端状态。");
        modes.EmitSignal(PopupMenu.SignalName.IdPressed, (long)AttackMode.Peace); await Packet<S.ChangeAMode>();
        KeyPress(Key.H); await Packet<S.ChangeAMode>();
        if (State.CurrentAttackMode != AttackMode.Group) throw new Exception("Ctrl+H未按服务端回执轮换攻击模式。");
        modes.EmitSignal(PopupMenu.SignalName.IdPressed, (long)AttackMode.Peace); await Packet<S.ChangeAMode>();
        menu.PopupCentered(); modes.Popup(new Rect2I(menu.Position.X + menu.Size.X, menu.Position.Y, 0, 0));
        await Screenshot("attack-mode-menu"); modes.Hide(); menu.Hide();
        GD.Print("PASS native attack modes: actual submenu, eight Chinese modes, server-acknowledged radio selection and invalid mode rejection.");
    }
}
