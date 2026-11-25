using Crystal.Native;
using Godot;
using C = ClientPackets;

public partial class Main
{
    private Control social = null!;
    private Label socialTitle = null!, mailBadge = null!;
    private ItemList socialList = null!;
    private TextEdit socialDetail = null!;
    private LineEdit friendName = null!;
    private Button[] socialActions = [];
    private int socialPage, socialRevision = -1, socialInventoryRevision = -1, selectedFriend = -1;
    private ulong selectedMail;
    private IntelligentCreatureType selectedPet = IntelligentCreatureType.None;
    private uint selectedSummon;
    private long heroToggleAt;

    private void BuildHudShortcuts()
    {
        void Add(string name, string hint, string library, int image, int x, int y, Action action)
        {
            var button = new HudShortcutButton { Name = name, Assets = Assets, Library = library, Index = image,
                Position = new Vector2(x, y), Size = new Vector2(20, 20), TooltipText = hint };
            game.AddChild(button); button.Pressed += () => { action(); _ = PlaySound(10103); };
        }
        Add("GroupShortcut", "组队 P", "Prguse", 1991, 110, 692, () => MenuAction(4));
        Add("GuildShortcut", "行会 H", "Prguse", 1994, 133, 692, () => MenuAction(6));
        Add("FriendShortcut", "好友", "Prguse", 1982, 156, 692, () => ToggleSocial(0));
        Add("MailShortcut", "消息 / 邮件", "Prguse", 2099, 179, 692, () => ToggleSocial(1));
        BuildCompanionPortraits();
        mailBadge = Text(game, "", 190, 683, 18);
        mailBadge.Name = "UnreadMailBadge"; mailBadge.Size = new Vector2(18, 14);
        mailBadge.AddThemeFontSizeOverride("font_size", ClientUi.Small);
        mailBadge.AddThemeColorOverride("font_color", new Color("ffdb91"));
        mailBadge.AddThemeColorOverride("font_outline_color", Colors.Black); mailBadge.AddThemeConstantOverride("outline_size", 4);
        mailBadge.MouseFilter = MouseFilterEnum.Ignore; mailBadge.Hide();
    }

    private void BuildSocial()
    {
        social = Panel(game, 350, 234, 324, 290); social.Name = "SocialWindow"; social.Hide();
        socialTitle = WindowTitle(social, "", 270);
        ArtButton(social, "Prguse2", 360, 298, 3, "关闭社交窗口", CloseSocial);
        socialList = new ItemList { Name = "SocialEntries", Position = new Vector2(12, 34), Size = new Vector2(300, 121), AllowReselect = true };
        socialList.AddThemeFontSizeOverride("font_size", ClientUi.Body); social.AddChild(socialList);
        socialList.ItemSelected += index => SelectSocialEntry((int)index);
        socialDetail = new TextEdit { Name = "SocialDetail", Position = new Vector2(12, 162), Size = new Vector2(300, 90), Editable = false,
            WrapMode = TextEdit.LineWrappingMode.Boundary, FocusMode = FocusModeEnum.None };
        socialDetail.AddThemeFontSizeOverride("font_size", ClientUi.Body); social.AddChild(socialDetail);
        friendName = Edit(social, 12, 223, 300, "输入角色名添加好友"); friendName.Name = "FriendName"; friendName.MaxLength = Globals.MaxCharacterNameLength;
        friendName.TextSubmitted += _ => AddSocialFriend();
        socialActions = Enumerable.Range(0, 4).Select(i => Button(social, "", 12 + 76 * i, 256, 72, () => SocialAction(i))).ToArray();
        BuildCompanionControls();
        BuildHeroManagement();
        Draggable(social);
    }

    private void ToggleSocial(int page)
    {
        if (State.User == null) return;
        if (page == 3) { ToggleHeroWindow(); return; }
        var close = social.Visible && socialPage == page;
        CloseSocial();
        if (close) return;
        socialPage = page; social.Show(); RenderSocial();
        if (page == 0) Send(new C.RefreshFriends());
    }

    private void CloseSocial()
    {
        social.Hide(); friendName.ReleaseFocus(); friendName.Clear();
        selectedFriend = -1; selectedMail = 0; selectedPet = IntelligentCreatureType.None; selectedSummon = 0;
    }

    private void ProcessSocial()
    {
        ProcessHeroCreation();
        if (heroItemPending != null && !heroItemTimedOut && Environment.TickCount64 - heroItemSent >= 5000)
        { heroItemTimedOut = true; Log("物品回执未知，已锁定物品操作。请重连核对，客户端不会重发。", ChatType.Hint); }
        if (socialRevision != State.SocialRevision || socialInventoryRevision != State.InventoryRevision)
        {
            socialRevision = State.SocialRevision;
            socialInventoryRevision = State.InventoryRevision;
            var unread = State.Mail.Count(mail => !mail.Opened);
            mailBadge.Text = unread > 99 ? "99+" : unread.ToString(); mailBadge.Visible = unread > 0;
            if (social.Visible) RenderSocial();
        }
    }

    private void RenderSocial()
    {
        var user = State.User!;
        socialTitle.Text = socialPage switch { 0 => "好友", 1 => "消息 · 邮件", 2 => "宠物", _ => "英雄" };
        socialList.Visible = true; friendName.Visible = socialPage == 0;
        socialList.Clear(); socialDetail.Position = new Vector2(12, socialPage == 3 ? 42 : 162);
        socialDetail.Size = new Vector2(300, socialPage switch { 0 => 54, 3 => 202, _ => 90 });
        RenderCompanionControls();
        switch (socialPage)
        {
            case 0:
                foreach (var friend in State.Friends.OrderBy(friend => friend.Blocked).ThenByDescending(friend => friend.Online).ThenBy(friend => friend.Name))
                {
                    var row = socialList.AddItem($"{friend.Name}   ·   {(friend.Blocked ? "已屏蔽" : friend.Online ? "在线" : "离线")}");
                    socialList.SetItemMetadata(row, friend.Index); if (friend.Index == selectedFriend) socialList.Select(row);
                }
                break;
            case 1:
                foreach (var mail in State.Mail.OrderByDescending(mail => mail.DateSent))
                {
                    var row = socialList.AddItem($"{(mail.Opened ? "已读" : "未读")}   {mail.SenderName}   {mail.DateSent:MM-dd HH:mm}");
                    socialList.SetItemMetadata(row, mail.MailID.ToString()); if (mail.MailID == selectedMail) socialList.Select(row);
                }
                break;
            case 2:
                foreach (var pet in user.IntelligentCreatures)
                {
                    var row = socialList.AddItem(pet.CustomName + (user.CreatureSummoned && user.SummonedCreatureType == pet.PetType ? "   ·   已召唤" : ""));
                    socialList.SetItemMetadata(row, (int)pet.PetType); if (pet.PetType == selectedPet) socialList.Select(row);
                }
                foreach (var summon in State.Actors.Values.Where(actor => actor.Kind == ObjectType.Monster && !actor.Dead && !actor.Hidden && actor.MasterObjectId == user.ObjectID && actor.Image < 10000).OrderBy(actor => actor.Image).ThenBy(actor => actor.Id))
                {
                    var row = socialList.AddItem(summon.Name + "   ·   召唤物");
                    // 负数区分场景对象编号，避免与拾取宠物类型编号相撞。
                    socialList.SetItemMetadata(row, -(long)summon.Id - 1); if (summon.Id == selectedSummon) socialList.Select(row);
                }
                break;
        }
        UpdateSocialDetail();
    }

    private void SelectSocialEntry(int index)
    {
        var key = socialList.GetItemMetadata(index);
        if (socialPage == 0) selectedFriend = key.AsInt32();
        else if (socialPage == 1 && ulong.TryParse(key.AsString(), out selectedMail))
        {
            if (State.Mail.Any(mail => mail.MailID == selectedMail && !mail.Opened)) Send(new C.ReadMail { MailID = selectedMail });
        }
        else if (socialPage == 2)
        {
            var value = key.AsInt64(); selectedSummon = value < 0 ? (uint)(-value - 1) : 0;
            selectedPet = value < 0 ? IntelligentCreatureType.None : (IntelligentCreatureType)value;
        }
        UpdateSocialDetail();
    }

    private void UpdateSocialDetail()
    {
        foreach (var button in socialActions) button.Hide();
        void ActionButton(int index, string text, bool enabled = true)
        {
            var button = socialActions[index]; button.Text = text; button.Disabled = !enabled; button.Show();
        }
        var user = State.User!;
        switch (socialPage)
        {
            case 0:
                var friend = State.Friends.FirstOrDefault(friend => friend.Index == selectedFriend);
                socialDetail.Text = friend == null ? "选择好友后可直接私信。" : string.IsNullOrWhiteSpace(friend.Memo) ? friend.Name : $"{friend.Name}\n{friend.Memo}";
                ActionButton(0, "添加"); ActionButton(1, "私信", friend is { Blocked: false }); ActionButton(2, "移除", friend != null); ActionButton(3, "刷新");
                break;
            case 1:
                var mail = State.Mail.FirstOrDefault(mail => mail.MailID == selectedMail);
                socialDetail.Text = mail == null ? (State.Mail.Count > 0 ? "请选择邮件阅读。" : "暂无邮件。") :
                    $"来自：{mail.SenderName}\n{mail.Message}" + (!mail.Collected && (mail.Items.Count != 0 || mail.Gold != 0) ? "\n有附件，请前往邮局领取。" : "");
                if (mail?.CanReply == true) ActionButton(0, "私信发件人");
                break;
            case 2:
                if (selectedSummon != 0 && State.Actors.GetValueOrDefault(selectedSummon) is { Dead: false, Hidden: false } summon)
                { socialDetail.Text = $"{summon.Name} · 召唤物\n生命 {summon.Health}% · 位置 {summon.Location.X},{summon.Location.Y}"; break; }
                var pet = user.IntelligentCreatures.FirstOrDefault(pet => pet.PetType == selectedPet);
                var active = pet != null && user.CreatureSummoned && user.SummonedCreatureType == pet.PetType;
                var summons = State.Actors.Values.Where(actor => actor.Kind == ObjectType.Monster && !actor.Dead && actor.MasterObjectId == user.ObjectID).Select(actor => actor.Name);
                socialDetail.Text = pet == null ? $"{(user.IntelligentCreatures.Count == 0 ? "暂无拾取宠物" : "请选择拾取宠物")}。\n在场召唤物：{string.Join("、", summons.DefaultIfEmpty("无"))}" :
                    $"{pet.CustomName} · {(active ? "已召唤" : "未召唤")}\n{(pet.Expire == DateTime.MinValue ? "永久" : $"到期：{pet.Expire:yyyy-MM-dd HH:mm}")}";
                if (pet != null) ActionButton(0, active ? "收回" : "召唤", active || !user.CreatureSummoned);
                break;
        }
    }

    private void AddSocialFriend()
    {
        var name = friendName.Text.Trim();
        if (name.Length == 0 || name.Any(char.IsWhiteSpace)) { Log("请填写好友的角色名称。", ChatType.Hint); return; }
        if (name.Equals(State.User?.Name, StringComparison.OrdinalIgnoreCase)) { Log("不能添加自己为好友。", ChatType.Hint); return; }
        Send(new C.AddFriend { Name = name }); friendName.ReleaseFocus();
    }

    private void SocialAction(int action)
    {
        if (State.User is not { } user || socialActions[action].Disabled) return;
        switch (socialPage, action)
        {
            case (0, 0): AddSocialFriend(); break;
            case (0, 1) when State.Friends.FirstOrDefault(friend => friend.Index == selectedFriend && !friend.Blocked) is { } friend:
                CloseSocial(); OpenWhisper(friend.Name); break;
            case (0, 2): Send(new C.RemoveFriend { CharacterIndex = selectedFriend }); break;
            case (0, 3): Send(new C.RefreshFriends()); break;
            case (1, 0) when State.Mail.FirstOrDefault(mail => mail.MailID == selectedMail && mail.CanReply) is { } mail:
                CloseSocial(); OpenWhisper(mail.SenderName); break;
            case (2, 0) when user.IntelligentCreatures.FirstOrDefault(pet => pet.PetType == selectedPet) is { } pet:
                var active = user.CreatureSummoned && user.SummonedCreatureType == pet.PetType;
                if (active || !user.CreatureSummoned) Send(new C.UpdateIntelligentCreature { Creature = pet, SummonMe = !active, UnSummonMe = active });
                break;
            case (3, 0) when user.HasHero && heroItemPending == null && Environment.TickCount64 >= heroToggleAt:
                SetHeroSummoned(State.HeroState != HeroSpawnState.Summoned); break;
        }
    }
}
