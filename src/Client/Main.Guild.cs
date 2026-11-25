using Godot;
using Crystal.Native;
using C = ClientPackets;
using S = ServerPackets;

public partial class Main
{
    private Control guild = null!, guildNoticePage = null!, guildMembersPage = null!, guildRanksPage = null!;
    private Label guildSummary = null!, guildHint = null!;
    private TextEdit guildNotice = null!;
    private LineEdit guildRecruit = null!, guildRankName = null!;
    private VBoxContainer guildMemberRows = null!;
    private OriginalButton guildEditNotice = null!, guildSaveNotice = null!;
    private readonly List<OriginalButton> guildTabs = new();
    private Button guildReloadNotice = null!, guildInviteToggle = null!;
    private OptionButton guildRankSelect = null!;
    private readonly List<CheckButton> guildRankOptions = new();
    private ConfirmationDialog guildInvitation = null!, guildConfirm = null!, guildDiscard = null!;
    private Action? guildConfirmed, guildAfterDiscard;
    private bool editingGuildNotice, savingGuildNotice, renderingGuildRanks;
    private string guildNoticeBaseline = "";
    private int guildRevision = -1, guildPage, guildSelectedRank;
    private long guildRequestTime, guildNoticeSaveTime;
    private static readonly string[] GuildPermissionNames = ["调整职位", "招收成员", "移除成员", "存入物品", "取出物品", "修改联盟", "修改公告", "启用增益"];

    private void BuildGuild()
    {
        guild = Panel(game, 218, 145, 590, 432); guild.Hide();
        Picture(guild, "Prguse", 180, 0, 0, nativeFrame: true); WindowTitle(guild, "行会", 520);
        ArtButton(guild, "Prguse2", 360, 565, 4, "关闭行会 H", CloseGuild);
        guildTabs.Add(ArtButton(guild, "Title", 93, 20, 34, "公告", () => SelectGuildPage(0), "公告"));
        guildTabs.Add(ArtButton(guild, "Title", 99, 92, 34, "成员", () => SelectGuildPage(1), "成员"));
        guildTabs.Add(ArtButton(guild, "Title", 101, 164, 34, "职位与权限", () => SelectGuildPage(2), "权限"));
        guild.AddChild(new VSeparator { Position = new Vector2(355, 62), Size = new Vector2(1, 336), MouseFilter = MouseFilterEnum.Ignore });
        Text(guild, "行会信息", 370, 64, 202).AddThemeFontSizeOverride("font_size", ClientUi.Title);
        guildSummary = Text(guild, "", 370, 94, 202); guildSummary.Size = new Vector2(202, 175); guildSummary.VerticalAlignment = VerticalAlignment.Top; guildSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        guildRecruit = Edit(guild, 372, 285, 130, "邀请角色名"); guildRecruit.MaxLength = Globals.MaxCharacterNameLength;
        guildRecruit.TextSubmitted += _ => InviteGuildMember(guildRecruit.Text);
        var invite = ArtButton(guild, "Title", 356, 510, 286, "邀请加入行会", () => InviteGuildMember(guildRecruit.Text), "邀请");
        invite.Size = new Vector2(60, ClientUi.ButtonHeight);
        guildInviteToggle = Button(guild, "", 368, 321, 204, ToggleGuildInvitations);
        Button(guild, "离开行会", 368, 357, 204, () => ConfirmGuild("离开当前行会？原引擎将检查会长和战争规则。", LeaveGuild));
        guildHint = Text(guild, "", 24, 402, 542); guildHint.Size = new Vector2(542, 18); guildHint.ClipText = true; guildHint.AddThemeFontSizeOverride("font_size", 10);
        guildNoticePage = new Control { Position = new Vector2(0, 60), Size = new Vector2(352, 342) }; guild.AddChild(guildNoticePage);
        guildNotice = new TextEdit { Position = new Vector2(13, 1), Size = new Vector2(322, 298), Editable = false, WrapMode = TextEdit.LineWrappingMode.Boundary }; guildNoticePage.AddChild(guildNotice);
        guildNotice.AddThemeFontSizeOverride("font_size", 11);
        guildNotice.AddThemeColorOverride("font_readonly_color", new Color("eadbb6"));
        guildEditNotice = ArtButton(guildNoticePage, "Prguse", 560, 12, 304, "编辑公告", BeginGuildNoticeEdit, "编辑公告");
        guildSaveNotice = ArtButton(guildNoticePage, "Prguse", 554, 12, 304, "保存公告", SaveGuildNotice, "保存公告");
        guildEditNotice.Size = guildSaveNotice.Size = new Vector2(76, ClientUi.ButtonHeight);
        guildReloadNotice = Button(guildNoticePage, "放弃编辑 / 读取最新公告", 94, 304, 240, () => GuardGuildDraft(ReloadGuildNotice));
        guildMembersPage = new Control { Position = new Vector2(13, 61), Size = new Vector2(330, 339) }; guild.AddChild(guildMembersPage);
        var scroll = new ScrollContainer { Size = new Vector2(330, 338) }; guildMembersPage.AddChild(scroll);
        guildMemberRows = new VBoxContainer { CustomMinimumSize = new Vector2(310, 0) }; scroll.AddChild(guildMemberRows);
        guildRanksPage = new Control { Position = new Vector2(13, 61), Size = new Vector2(330, 339) }; guild.AddChild(guildRanksPage);
        guildRankSelect = new OptionButton { Size = new Vector2(220, 28) }; guildRanksPage.AddChild(guildRankSelect);
        guildRankSelect.ItemSelected += i => { guildSelectedRank = guildRankSelect.GetItemId((int)i); RenderGuildRanks(); };
        Button(guildRanksPage, "新增职位", 230, 0, 96, () => { if (State.HasGuildPermission(GuildRankOptions.CanChangeRank)) Send(new C.EditGuildMember { ChangeType = 4 }); });
        guildRankName = Edit(guildRanksPage, 0, 37, 220, "职位名称（3–20 字符）"); guildRankName.MaxLength = 20;
        Button(guildRanksPage, "改名", 230, 37, 96, RenameGuildRank);
        for (var i = 0; i < 8; i++)
        {
            var option = i; var button = new CheckButton { Text = GuildPermissionNames[i], Position = new Vector2(i % 2 * 164, 78 + i / 2 * 48), Size = new Vector2(162, 36) };
            guildRanksPage.AddChild(button); guildRankOptions.Add(button);
            button.Toggled += enabled => { if (!renderingGuildRanks) SetGuildRankPermission(guildSelectedRank, option, enabled); };
        }
        guildInvitation = new ConfirmationDialog { Title = "行会邀请" }; game.AddChild(guildInvitation); guildInvitation.GetOkButton().Text = "确定"; guildInvitation.GetCancelButton().Text = "取消";
        guildInvitation.Confirmed += () => RespondGuildInvitation(true); guildInvitation.Canceled += () => RespondGuildInvitation(false);
        guildConfirm = new ConfirmationDialog { Title = "确认行会操作" }; game.AddChild(guildConfirm); guildConfirm.GetOkButton().Text = "确定"; guildConfirm.GetCancelButton().Text = "取消";
        guildConfirm.Confirmed += () => { var action = guildConfirmed; guildConfirmed = null; action?.Invoke(); };
        guildDiscard = new ConfirmationDialog { Title = "未保存的行会公告", DialogText = "放弃当前公告草稿？" }; game.AddChild(guildDiscard); guildDiscard.GetOkButton().Text = "确定"; guildDiscard.GetCancelButton().Text = "取消";
        guildDiscard.Confirmed += () => { editingGuildNotice = false; savingGuildNotice = false; var action = guildAfterDiscard; guildAfterDiscard = null; action?.Invoke(); };
        Draggable(guild);
        BuildGuildStorage();
    }
    public void OpenGuild() { if (State.User == null) return; guild.Show(); RenderGuild(); }
    private void CloseGuild() => GuardGuildDraft(() => guild.Hide());
    public void SelectGuildPage(int page) => GuardGuildDraft(() =>
    {
        guildPage = Math.Clamp(page, 0, 3);
        if (guildPage == 3) { CloseStorage(); shopMode = null; npc.Hide(); guild.Position = new Vector2(18, 145); inventory.Position = new Vector2(700, 35); inventory.Show(); }
        RenderGuild();
    });
    private void GuardGuildDraft(Action action)
    {
        if (savingGuildNotice) { Log("正在等待行会公告保存回执。"); return; }
        if (editingGuildNotice && guildNotice.Text != guildNoticeBaseline) { guildAfterDiscard = action; guildDiscard.PopupCentered(); }
        else { editingGuildNotice = false; action(); }
    }
    private void ConfirmGuild(string prompt, Action action) { guildConfirmed = action; guildConfirm.DialogText = prompt; guildConfirm.PopupCentered(); }
    public void ToggleGuildInvitations()
    {
        if (State.User == null) return;
        State.AllowGuildInvitations = !State.AllowGuildInvitations;
        if (!State.AllowGuildInvitations) State.GuildInvitation = "";
        State.GuildRevision++; Send(new C.Chat { Message = "@ALLOWGUILD" });
    }
    public void RespondGuildInvitation(bool accept)
    {
        if (State.GuildInvitation.Length == 0) return;
        State.GuildInvitation = ""; State.GuildRevision++; guildInvitation.Hide();
        Send(new C.GuildInvite { AcceptInvite = accept });
        if (accept) OpenGuild();
    }
    public void InviteGuildMember(string name)
    {
        name = name.Trim();
        if (!State.HasGuildPermission(GuildRankOptions.CanRecruit) || name.Length < Globals.MinCharacterNameLength || name.Length > Globals.MaxCharacterNameLength || name == State.User?.Name) { Log("请填写其他在线角色名，并确认当前职位有招收权限。"); return; }
        Send(new C.EditGuildMember { ChangeType = 0, Name = name });
    }
    public void KickGuildMember(string name) { if (State.HasGuildPermission(GuildRankOptions.CanKick)) Send(new C.EditGuildMember { ChangeType = 1, Name = name }); }
    public void ChangeGuildMemberRank(string name, int rank)
    {
        if (!State.HasGuildPermission(GuildRankOptions.CanChangeRank) || rank < 0 || rank > 254) return;
        Send(new C.EditGuildMember { ChangeType = 2, Name = name, RankIndex = (byte)rank });
    }
    public void LeaveGuild() { if (State.InGuild) Send(new C.Chat { Message = "@LEAVEGUILD" }); }
    public void BeginGuildNoticeEdit()
    {
        if (!State.HasGuildPermission(GuildRankOptions.CanChangeNotice) || State.GuildNoticeDirty) return;
        guildNoticeBaseline = string.Join('\n', State.GuildNotice); guildNotice.Text = guildNoticeBaseline;
        editingGuildNotice = true; RenderGuild(); guildNotice.GrabFocus();
    }
    public void SetGuildNoticeDraft(string value) { if (editingGuildNotice) guildNotice.Text = value; }
    public string GuildNoticeDraft => guildNotice.Text;
    public void SaveGuildNotice()
    {
        if (!editingGuildNotice || savingGuildNotice || !State.HasGuildPermission(GuildRankOptions.CanChangeNotice)) return;
        // shortcut: original notice packets have no revision; concurrent saves need a conditional protocol extension.
        if (string.Join('\n', State.GuildNotice) != guildNoticeBaseline) { Log("公告已被其他成员更新。请放弃草稿并读取最新内容。"); return; }
        var value = guildNotice.Text.Replace("\r", ""); var lines = value.Split('\n');
        if (value.Length > Globals.MaxGuildNoticeLength || lines.Length > Globals.MaxGuildNoticeLines || value.Contains('\0')) { Log("公告最多 200 行、20000 字符。"); return; }
        savingGuildNotice = true; guildNoticeSaveTime = Environment.TickCount64;
        Send(new C.EditGuildNotice { notice = lines.ToList() }); RenderGuild();
    }
    public void ReloadGuildNotice() { editingGuildNotice = false; guildNotice.Text = string.Join('\n', State.GuildNotice); RenderGuild(); }
    private bool CanEditGuildRank() => State.HasGuildPermission(GuildRankOptions.CanChangeRank) && guildSelectedRank > State.Guild!.MyRankId && State.GuildRanks.Any(r => r.Index == guildSelectedRank);
    public void SetGuildRankPermission(int rank, int option, bool enabled)
    {
        if (!State.HasGuildPermission(GuildRankOptions.CanChangeRank) || rank <= State.Guild!.MyRankId || rank > 254 || option is < 0 or > 7 || !State.GuildRanks.Any(r => r.Index == rank)) return;
        Send(new C.EditGuildMember { ChangeType = 5, RankIndex = (byte)rank, RankName = option.ToString(), Name = enabled ? "true" : "false" });
    }
    private void RenameGuildRank()
    {
        var name = guildRankName.Text.Trim();
        if (!State.HasGuildPermission(GuildRankOptions.CanChangeRank) || guildSelectedRank < State.Guild!.MyRankId || name.Length is < 3 or > 20 || name.Any(char.IsControl) || name.Contains('\\')) return;
        Send(new C.EditGuildMember { ChangeType = 3, RankIndex = (byte)guildSelectedRank, RankName = name });
    }
    private void GuildPacket(Packet packet)
    {
        GuildStoragePacket(packet);
        if (packet is S.GuildNoticeChange { update: >= 0 } && savingGuildNotice) { savingGuildNotice = false; editingGuildNotice = false; }
        if (packet is S.GuildStatus && savingGuildNotice && !State.HasGuildPermission(GuildRankOptions.CanChangeNotice)) savingGuildNotice = false;
        if (packet is S.GuildStatus && !State.InGuild) { editingGuildNotice = savingGuildNotice = false; guildConfirm.Hide(); }
    }
    private void ProcessGuild()
    {
        ProcessGuildStorage();
        if (savingGuildNotice && Environment.TickCount64 - guildNoticeSaveTime > 10000)
        {
            savingGuildNotice = false; Log("未收到公告保存回执，草稿已保留，请读取最新公告后重试。"); RenderGuild();
        }
        if (State.InGuild && Environment.TickCount64 >= guildRequestTime && (State.GuildNoticeDirty || State.GuildMembersDirty))
        {
            guildRequestTime = Environment.TickCount64 + 1000;
            if (State.GuildNoticeDirty) Send(new C.RequestGuildInfo { Type = 0 });
            if (State.GuildMembersDirty) Send(new C.RequestGuildInfo { Type = 1 });
        }
        if (guildRevision != State.GuildRevision) { guildRevision = State.GuildRevision; RenderGuild(); }
    }
    private void RenderGuild()
    {
        if (State.User == null) return;
        var indices = new[] { 93, 99, 101, 105 };
        for (var page = 0; page < guildTabs.Count; page++)
        {
            guildTabs[page].Index = indices[page] + (guildPage == page ? 1 : 0);
            guildTabs[page].HoverIndex = guildTabs[page].Index; guildTabs[page].PressedIndex = indices[page] + 1;
            guildTabs[page].SetPressedNoSignal(guildPage == page);
        }
        guildNoticePage.Visible = State.InGuild && guildPage == 0; guildMembersPage.Visible = State.InGuild && guildPage == 1; guildRanksPage.Visible = State.InGuild && guildPage == 2;
        guildSummary.Text = State.Guild is { } g ? $"{g.GuildName}\n职位：{g.GuildRankName}\n等级：{g.Level}\n成员：{g.MemberCount}/{g.MaxMembers}\n经验：{g.Experience}/{g.MaxExperience}\n资金：{g.Gold:N0}\n\n!~内容 发送行会聊天" : "尚未加入行会。\n允许邀请后，由有招收权限的成员邀请你。\n\n满足原服条件后，可向行会管理员申请建会。";
        guildRecruit.Editable = State.HasGuildPermission(GuildRankOptions.CanRecruit);
        guildInviteToggle.Text = State.AllowGuildInvitations ? "拒绝新的行会邀请" : "允许行会邀请";
        if (!editingGuildNotice) guildNotice.Text = string.Join('\n', State.GuildNotice);
        guildNotice.Editable = editingGuildNotice && !savingGuildNotice && State.HasGuildPermission(GuildRankOptions.CanChangeNotice);
        guildEditNotice.Visible = !editingGuildNotice && State.HasGuildPermission(GuildRankOptions.CanChangeNotice);
        guildSaveNotice.Visible = editingGuildNotice; guildSaveNotice.Disabled = savingGuildNotice || !guildNotice.Editable;
        guildReloadNotice.Visible = editingGuildNotice;
        guildHint.Text = savingGuildNotice ? "公告正在保存…" : editingGuildNotice && string.Join('\n', State.GuildNotice) != guildNoticeBaseline ? "公告已有新版本；草稿已保留，请放弃后读取最新内容。" : "行会操作由原引擎检查职位、会长与战争规则。";
        if (State.GuildInvitation.Length == 0) guildInvitation.Hide();
        else { guildInvitation.DialogText = $"{State.GuildInvitation} 邀请你加入行会，是否接受？"; if (!guildInvitation.Visible) guildInvitation.PopupCentered(); }
        if (guildMembersPage.Visible) RenderGuildMembers();
        if (guildRanksPage.Visible) RenderGuildRanks();
        RenderGuildStorage();
    }
    private void RenderGuildMembers()
    {
        Clear(guildMemberRows);
        foreach (var rank in State.GuildRanks.OrderBy(r => r.Index))
        foreach (var member in rank.Members)
        {
            var row = new HBoxContainer(); guildMemberRows.AddChild(row);
            var name = new Label { Text = member.Name, CustomMinimumSize = new Vector2(94, 24), TooltipText = member.Online ? "在线" : $"最近在线：{member.LastLogin:g}" }; row.AddChild(name);
            var select = new OptionButton { CustomMinimumSize = new Vector2(102, 24) }; row.AddChild(select);
            foreach (var choice in State.GuildRanks.OrderBy(r => r.Index))
            {
                select.AddItem(choice.Name, choice.Index);
                var i = select.ItemCount - 1;
                select.SetItemDisabled(i, State.Guild!.MyRankId != 0 && choice.Index <= State.Guild.MyRankId);
                if (choice.Index == rank.Index) select.Selected = i;
            }
            select.Disabled = !State.HasGuildPermission(GuildRankOptions.CanChangeRank) || (State.Guild!.MyRankId != 0 && rank.Index <= State.Guild.MyRankId);
            select.ItemSelected += i => { var target = select.GetItemId((int)i); ConfirmGuild($"将 {member.Name} 调整为 {State.GuildRanks.Single(r => r.Index == target).Name}？", () => ChangeGuildMemberRank(member.Name, target)); };
            row.AddChild(new Label { Text = member.Online ? "在线" : "离线", CustomMinimumSize = new Vector2(34, 24) });
            if (member.Name != State.User!.Name && State.HasGuildPermission(GuildRankOptions.CanKick) && (State.Guild!.MyRankId == 0 || rank.Index > State.Guild.MyRankId))
            {
                var kick = new Button { Text = "移除" }; row.AddChild(kick); kick.Pressed += () => ConfirmGuild($"从行会移除 {member.Name}？", () => KickGuildMember(member.Name));
            }
        }
    }
    private void RenderGuildRanks()
    {
        renderingGuildRanks = true;
        guildRankSelect.Clear();
        foreach (var rank in State.GuildRanks.OrderBy(r => r.Index)) guildRankSelect.AddItem(rank.Name, rank.Index);
        var selected = State.GuildRanks.FirstOrDefault(r => r.Index == guildSelectedRank) ?? State.GuildRanks.FirstOrDefault();
        if (selected != null)
        {
            guildSelectedRank = selected.Index; guildRankSelect.Selected = guildRankSelect.GetItemIndex(selected.Index);
            if (!guildRankName.HasFocus()) guildRankName.Text = selected.Name;
            for (var i = 0; i < 8; i++) { guildRankOptions[i].ButtonPressed = selected.Options.HasFlag((GuildRankOptions)(1 << i)); guildRankOptions[i].Disabled = !CanEditGuildRank(); }
        }
        renderingGuildRanks = false;
    }
    private void CloseGuildUi() { guild.Hide(); guildInvitation.Hide(); guildConfirm.Hide(); guildDiscard.Hide(); ResetGuildStorageUi(); editingGuildNotice = savingGuildNotice = false; guildConfirmed = guildAfterDiscard = null; guildRequestTime = 0; }
}
