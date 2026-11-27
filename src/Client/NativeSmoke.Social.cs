using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifySocial()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-social.", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new Exception("社交实测须使用独立版本和端口。");
        stage = "social fixture login";
        await Until(() => Client.HandshakeReady);
        Client.Login("TradeLeft", "Trade123");
        Client.Send(new C.StartGame { CharacterIndex = (await Packet<S.LoginSuccess>()).Characters.Single().Index });
        await Until(() => State.User != null && !Client.World.Loading);
        var shortcuts = new[] { "Group", "Guild", "Friend", "Mail", "Pet", "Hero" }
            .Select(name => (Button)Client.FindChild(name + "Shortcut", true, false)).ToArray();
        await Until(() => shortcuts.Where(icon => icon != shortcuts[4]).All(icon => icon is OriginalButton original ? Client.Assets.Sprite(original.Library, original.Index) != null : icon is HeroPortraitButton hero ? hero.PortraitReady : icon.Icon != null));
        if (shortcuts[4].Visible) throw new Exception("没有召唤宠物时仍显示宠物头像。");
        var position = State.User!.Location;
        var panel = (Control)Client.FindChild("SocialWindow", true, false);
        var title = panel.GetNode<Label>("WindowTitle");
        for (var index = 0; index < 2; index++)
        {
            var window = GameArt(index == 0 ? "关闭组队 P" : "关闭行会 H").GetParent<Control>();
            await ClickSelectionControl(shortcuts[index]); await Until(() => window.Visible);
            await ClickSelectionControl(shortcuts[index]); await Until(() => !window.Visible);
        }
        for (var index = 2; index < 5; index++)
        {
            if (index == 4) ((PopupMenu)Client.FindChild("GameMenu", true, false)).EmitSignal(PopupMenu.SignalName.IdPressed, 17);
            else await ClickSelectionControl(shortcuts[index]);
            await Until(() => panel.Visible);
            if (!title.Text.StartsWith(new[] { "好友", "消息", "宠物", "英雄" }[index - 2], StringComparison.Ordinal)) throw new Exception("HUD图标打开了错误窗口。");
        }
        await ClickSelectionControl(shortcuts[^1]);
        if (State.User.Location != position) throw new Exception("点击功能图标同时移动了角色。");
        await Screenshot("social-hud");
        await VerifySocialIconStates(shortcuts[0]);
        if (OS.GetCmdlineUserArgs().Contains("--social-layout-only"))
        {
            await VerifyCompanionHud(shortcuts, panel);
            await VerifyHeroItems(shortcuts, panel);
            await VerifyChatChannelTabs((Control)Client.FindChild("ChatChannels", true, false));
            GD.Print("PASS focused social layout: square social icons, enlarged hero with two bottom controls, hidden absent pets, chat-edge tabs and expanded-chat spacing.");
            await Client.Quit(); return;
        }
        await VerifyHeroItems(shortcuts, panel);
        await VerifyCompanionHud(shortcuts, panel);

        stage = "social peer and TCP replies";
        await ToSignal(GetTree().CreateTimer(5.2), SceneTreeTimer.SignalName.Timeout);
        using var peer = new GameConnection(); var incoming = new List<Packet>();
        async Task<T> Next<T>(Func<T, bool> match) where T : Packet
        {
            T? found = null;
            await Until(() =>
            {
                while (peer.Received.TryDequeue(out var p)) incoming.Add(p);
                if (peer.Errors.TryDequeue(out var error)) throw new IOException(error);
                found = incoming.OfType<T>().FirstOrDefault(match); return found != null;
            });
            incoming.Remove(found!); return found!;
        }
        await peer.Connect(Client.Assets.Host, Client.Assets.GamePort);
        await Next<S.Connected>(_ => true); await peer.Send(new C.ClientVersion { VersionHash = [] }); await Next<S.ClientVersion>(_ => true);
        await peer.Send(new C.Login { AccountID = "TradeRight", Password = "Trade123" });
        await peer.Send(new C.StartGame { CharacterIndex = (await Next<S.LoginSuccess>(_ => true)).Characters.Single().Index });
        await Next<S.UserInformation>(p => p.Name == "TradeRight");

        await ClickSelectionControl(shortcuts[2]);
        var nameInput = (LineEdit)panel.FindChild("FriendName", true, false); nameInput.Text = "TradeRight";
        var entries = (ItemList)panel.FindChild("SocialEntries", true, false);
        Button SocialButton(string caption) => panel.GetChildren().OfType<Button>().Single(button => button.Visible && button.Text == caption);
        await ClickSelectionControl(SocialButton("添加"));
        await Until(() => State.Friends.Any(friend => friend.Name == "TradeRight" && friend.Online) && entries.ItemCount == 1);
        await Screenshot("friends");
        entries.Select(0); entries.EmitSignal(ItemList.SignalName.ItemSelected, 0);
        await ClickSelectionControl(SocialButton("私信"));
        var input = Client.FindChildren("*", "LineEdit", true, false).Cast<LineEdit>().Single(edit => edit.MaxLength == Globals.MaxChatLength);
        var selector = (Control)Client.FindChild("ChatChannels", true, false);
        var messages = (RichTextLabel)Client.FindChild("ChatMessages", true, false);
        var whisper = (LineEdit)Client.FindChild("WhisperTarget", true, false);
        if (!selector.GetNode<Button>("ChatChannelWhisper").ButtonPressed || whisper.Text != "TradeRight") throw new Exception("好友入口没有预填私信对象。");
        void SendText(string text) { input.Text = text; input.EmitSignal(LineEdit.SignalName.TextSubmitted, text); }
        SendText("私信实测");
        await Next<S.Chat>(p => p.Type == ChatType.WhisperIn && p.Message.Contains("私信实测"));
        await peer.Send(new C.Chat { Message = "/TradeLeft 私信回信" });
        await Until(() => messages.GetParsedText().Contains("私信回信"));
        await Screenshot("whisper");
        await VerifyChatChannelTabs(selector);
        SendText("公共频道实测");
        await Next<S.ObjectChat>(p => p.Type == ChatType.Normal && p.Text.Contains("公共频道实测"));
        await Until(() => messages.GetParsedText().Contains("公共频道实测"));
        if (messages.GetParsedText().Contains("私信回信") || whisper.Visible) throw new Exception("公共频道仍泄漏私信或显示私信输入框。");

        stage = "social mail and server read receipt";
        var previousMail = State.Mail.Select(mail => mail.MailID).ToHashSet();
        await peer.Send(new C.SendMail { Name = "TradeLeft", Message = "邮件内容实测" });
        if ((await Next<S.MailSent>(_ => true)).Result != 1) throw new Exception("测试邮件未成功寄出。");
        var badge = (Label)Client.FindChild("UnreadMailBadge", true, false);
        await Until(() => State.Mail.Any(mail => !previousMail.Contains(mail.MailID) && !mail.Opened) && badge.Visible);
        var sentMail = State.Mail.Single(mail => !previousMail.Contains(mail.MailID));
        await ClickSelectionControl(shortcuts[3]);
        var mailRow = Enumerable.Range(0, entries.ItemCount).Single(row => entries.GetItemMetadata(row).AsString() == sentMail.MailID.ToString());
        entries.Select(mailRow); entries.EmitSignal(ItemList.SignalName.ItemSelected, mailRow);
        await Until(() => State.Mail.Single(mail => mail.MailID == sentMail.MailID).Opened && !badge.Visible);
        if (!((TextEdit)panel.FindChild("SocialDetail", true, false)).Text.Contains("邮件内容实测")) throw new Exception("邮件正文未显示。");
        await Screenshot("mail");
        GD.Print("PASS native social: six HUD icon clicks, original assets, public/private via real TCP, friend add/open whisper, mailbox unread, original ReadMail acknowledgment.");
        await Client.Quit();
    }

    private async Task VerifySocialIconStates(Button button)
    {
        var point = button.GetGlobalRect().GetCenter();
        using var motion = new InputEventMouseMotion { Position = point, GlobalPosition = point };
        GetViewport().PushInput(motion, true);
        await Until(button.IsHovered); await Screenshot("social-icons-hover");
        using var press = new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = true };
        GetViewport().PushInput(press, true);
        await Until(button.IsPressed); await Screenshot("social-icons-pressed");
        if (button.Size != new Vector2(20, 20)) throw new Exception("社交按钮状态切换改变了尺寸。");
        using var release = new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left };
        GetViewport().PushInput(release, true);
        var group = GameArt("关闭组队 P").GetParent<Control>();
        await Until(() => group.Visible); await ClickSelectionControl(button); await Until(() => !group.Visible);
    }

    private bool AssetsPortIsDefault() => Argument("--server") == "http://127.0.0.1:5080" || Argument("--server").Length == 0;

    private async Task VerifyChatChannelTabs(Control selector)
    {
        var tabs = selector.GetChildren().OfType<Button>().ToArray(); var rectangles = tabs.Select(button => button.GetRect()).ToArray();
        var frame = (Control)Client.FindChild("ChatFrame", true, false);
        if (selector.GetRect().End.Y != frame.Position.Y || selector.Position.X != frame.Position.X)
            throw new Exception("频道页签没有紧贴聊天框并与左边缘对齐。");
        foreach (var channel in Enum.GetValues<ChatChannel>())
        {
            var tab = selector.GetNode<Button>("ChatChannel" + channel); await ClickSelectionControl(tab);
            if (!tab.ButtonPressed || tabs.Count(button => button.ButtonPressed) != 1 || tabs.Any(button => button.Size != new Vector2(48, 20)) ||
                !tabs.Select(button => button.GetRect()).SequenceEqual(rectangles))
                throw new Exception("聊天频道页签没有唯一选中、统一尺寸或切换后发生位置偏移。");
        }
        await ClickSelectionControl(selector.GetNode<Button>("ChatChannelPublic")); await Screenshot("channels");
    }

    private async Task VerifyCompanionHud(Button[] shortcuts, Control panel)
    {
        stage = "HUD portrait modes, companion details and resized chat geometry";
        const uint skeleton = 0x70005001, shinsu = 0x70005002, deva = 0x70005003;
        State.Apply(new S.ObjectMonster { ObjectID = skeleton, Image = Monster.BoneFamiliar, Name = "骷髅头像检查", MasterObjectId = State.User!.ObjectID, Location = State.User.Location });
        State.Apply(new S.ObjectMonster { ObjectID = shinsu, Image = Monster.Shinsu1, Name = "神兽头像检查", MasterObjectId = State.User.ObjectID, Location = State.User.Location });
        State.Apply(new S.ObjectMonster { ObjectID = deva, Image = Monster.HolyDeva, Name = "第三头像检查", MasterObjectId = State.User.ObjectID, Location = State.User.Location });
        await Until(() => shortcuts[4].Visible && shortcuts[4].Icon != null);
        var toggle = (Button)Client.FindChild("SummonHero", true, false);
        var heroMode = (OptionButton)Client.FindChild("HeroPortraitMode", true, false);
        if (shortcuts.Take(4).Any(button => button.Size != new Vector2(20, 20)) || shortcuts[4].Size != Vector2.One * ClientUi.CompanionPortraitSize ||
            shortcuts[5] is not HeroPortraitButton || shortcuts[5].Size != new Vector2(108, 78) || shortcuts[5].Position.Y != 516 ||
            toggle.Position.X != shortcuts[5].Position.X || toggle.GetRect().End.X + 4 != heroMode.Position.X ||
            toggle.Position.Y != shortcuts[5].GetRect().End.Y + 6 || toggle.Position.Y != heroMode.Position.Y || toggle.Size != heroMode.Size ||
            heroMode.GetRect().End.X != shortcuts[5].GetRect().End.X || heroMode.GetRect().End.Y > 635 ||
            ((Control)Client.FindChild("PetPortraitMode", true, false)).GetRect().End.Y + 4 != shortcuts[5].Position.Y)
            throw new Exception($"社交20px方形按钮或英雄环形头像/底部双控件布局错误：{string.Join(';', shortcuts.Select(button => button.GetRect()))}; toggle={toggle.GetRect()}, mode={heroMode.GetRect()}");
        foreach (var (name, value) in new[] { ("HeroPortraitMode", (int)HeroBehaviour.Follow), ("PetPortraitMode", (int)PetMode.MoveOnly) })
        {
            var selector = (OptionButton)Client.FindChild(name, true, false);
            var portrait = name == "HeroPortraitMode" ? shortcuts[5] : shortcuts[4];
            if (name == "PetPortraitMode" && (selector.Size.X != portrait.Size.X || selector.Position.X != portrait.Position.X || selector.Position.Y != portrait.GetRect().End.Y + 2))
                throw new Exception("宠物头像下方模式控件没有等宽对齐。");
            await ClickSelectionControl(selector); await Until(() => selector.GetPopup().Visible);
            selector.GetPopup().EmitSignal(PopupMenu.SignalName.IndexPressed, selector.GetItemIndex(value)); selector.GetPopup().Hide();
            await Until(() => selector.GetSelectedId() == value && (name == "HeroPortraitMode" ? (int)State.User!.HeroBehaviour : (int)State.CurrentPetMode) == value);
        }
        Client.Send(new C.ChangePMode { Mode = PetMode.Both }); Client.Send(new C.SetHeroBehaviour { Behaviour = HeroBehaviour.Attack });
        await Until(() => State.CurrentPetMode == PetMode.Both && State.User!.HeroBehaviour == HeroBehaviour.Attack);
        var resize = Client.GetType().GetMethod("SetChatHeight", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        resize.Invoke(Client, [136f]);
        var channels = (Control)Client.FindChild("ChatChannels", true, false);
        var bar = (Control)Client.FindChild("ItemShortcut1", true, false);
        var chat = (Control)Client.FindChild("ChatFrame", true, false);
        if (channels.GetRect().End.Y != chat.Position.Y || channels.Position.X != chat.Position.X ||
            bar.GetGlobalRect().End.Y >= channels.GetGlobalRect().Position.Y ||
            shortcuts.Take(4).Any(button => button.GetGlobalRect().Intersects(bar.GetGlobalRect()) || button.GetGlobalRect().Intersects(channels.GetGlobalRect())))
            throw new Exception("拉高聊天框后社交按钮与快捷栏或频道控件重叠。");
        if (shortcuts[5].GetRect().End.X != shortcuts[4].GetRect().End.X || shortcuts[4].GetRect().End.Y >= shortcuts[5].Position.Y || shortcuts.Take(4).Any(button => button.Position.Y < 690))
            throw new Exception("宠物没有在英雄上方对齐，或社交按钮未移至底部。");
        await Screenshot("social-chat-expanded"); resize.Invoke(Client, [68f]);

        // 仅注入显示样本，核对两只战斗召唤物的头像和选择，不创建或改动服务器宠物。
        try
        {
            var second = (Button)Client.FindChild("PetPortrait2", true, false);
            await Until(() => second.Visible && second.Icon != null && shortcuts[4].Icon != null);
            await ClickSelectionControl(shortcuts[4]);
            var detail = (TextEdit)panel.FindChild("SocialDetail", true, false);
            await Until(() => panel.Visible && detail.Text.Contains("骷髅头像检查"));
            await ClickSelectionControl(second);
            await Until(() => panel.Visible && detail.Text.Contains("神兽头像检查"));
            State.Apply(new S.ObjectHealth { ObjectID = shinsu, Percent = 61 });
            await Until(() => detail.Text.Contains("61%"));
            await Screenshot("summon-portraits");
            await ClickSelectionControl(second); await Until(() => !panel.Visible);
            var third = (Button)Client.FindChild("PetPortrait3", true, false);
            resize.Invoke(Client, [136f]);
            if (!third.Visible || Client.FindChildren("SkillShortcut*", "Button", true, false).Cast<Button>().Any(button => button.GetGlobalRect().Intersects(third.GetGlobalRect())))
                throw new Exception("聊天框拉高后第三个宠物头像与技能快捷栏重叠。");
            resize.Invoke(Client, [68f]);
        }
        finally { State.Actors.Remove(skeleton); State.Actors.Remove(shinsu); State.Actors.Remove(deva); }
        GD.Print("PASS native companion HUD: real portrait dropdown opening and server mode replies, resized-chat spacing, two original summon portraits, individual details and live health display.");
    }

    private async Task VerifyHeroItems(Button[] shortcuts, Control panel)
    {
        stage = "shared hero original equipment roundtrip";
        await Until(() => State.Hero != null && State.HeroState == HeroSpawnState.Summoned);
        var hero = State.Hero!; var player = State.User!;
        var item = player.Inventory.First(item => item?.Info.Type == ItemType.Weapon);
        var original = player.Inventory.Concat(hero.Equipment).Where(item => item != null).Select(item => $"{item.UniqueID}:{item.Count}").Order().ToArray();
        await ClickSelectionControl(shortcuts[5]);
        var window = GameArt("关闭角色").GetParent<Control>();
        await Until(() => window.Visible);
        packets.RemoveAll(packet => packet is S.EquipItem); Client.UseItem(item.UniqueID);
        if (!(await Packet<S.EquipItem>()).Success || hero.Equipment[0]?.UniqueID != item.UniqueID || player.Equipment[0] != null)
            throw new Exception("共用背包装备没有穿给英雄。");
        await Screenshot("hero-equipment");
        packets.RemoveAll(packet => packet is S.RemoveItem);
        Client.GetType().GetMethod("RemoveEquipment", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(Client, [0]);
        if (!(await Packet<S.RemoveItem>()).Success || hero.Equipment[0] != null) throw new Exception("英雄装备未卸下到共用背包。");
        var final = player.Inventory.Concat(hero.Equipment).Where(item => item != null).Select(item => $"{item.UniqueID}:{item.Count}").Order().ToArray();
        if (!original.SequenceEqual(final) || !ReferenceEquals(hero.Inventory, player.Inventory)) throw new Exception("英雄换装改变物品编号、数量或建立第二背包。");
        await ClickSelectionControl(shortcuts[5]);
        GD.Print("PASS native shared hero paperdoll and original equip/remove replies, preserved item IDs/counts.");
    }
}
