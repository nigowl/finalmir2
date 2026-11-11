using Godot;
using C = ClientPackets;

public partial class Main
{
    private Control group = null!, groupTarget = null!;
    private LineEdit groupName = null!;
    private Label groupStatus = null!, groupPrompt = null!;
    private OriginalButton groupSwitch = null!, groupAdd = null!, groupRemove = null!;
    private readonly List<Button> groupRows = new();
    private ConfirmationDialog groupInvitation = null!, groupLeave = null!;
    private bool removingGroupMember;
    private int groupRevision = -1;
    private bool GroupLeader => State.GroupMembers.Count == 0 || State.GroupMembers[0].Equals(State.User?.Name, StringComparison.OrdinalIgnoreCase);

    private void BuildGroup()
    {
        group = Panel(game, 18, 290, 228, 250); group.Hide();
        Picture(group, "Prguse", 120, 0, 0); Picture(group, "Title", 5, 18, 8);
        ArtButton(group, "Prguse2", 360, 206, 3, "关闭组队 P", () => group.Hide());
        for (var i = 0; i < Globals.MaxGroup; i++)
        {
            var index = i;
            var row = new Button { Position = i == 0 ? new Vector2(16, 32) : new Vector2((i + 1) % 2 * 100 + 16, 54 + (i - 1) / 2 * 20), Size = new Vector2(96, 20), FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left };
            row.AddThemeFontSizeOverride("font_size", 11); CellStyle(row); group.AddChild(row); groupRows.Add(row);
            row.Pressed += () => { if (index < State.GroupMembers.Count) ShowGroupTarget(true, State.GroupMembers[index]); };
        }
        groupStatus = Text(group, "", 12, 194, 210); groupStatus.AddThemeFontSizeOverride("font_size", 11);
        groupSwitch = ArtButton(group, "Prguse", 114, 25, 219, "开启 / 关闭组队（关闭会离队）", ToggleGroupMode);
        groupAdd = ArtButton(group, "Title", 130, 70, 219, "创建队伍 / 邀请成员", () => ShowGroupTarget(false));
        groupRemove = ArtButton(group, "Title", 136, 140, 219, "移除成员（队长）", () => ShowGroupTarget(true));
        groupTarget = Panel(game, 355, 275, 314, 155); groupTarget.Hide();
        groupPrompt = Text(groupTarget, "", 14, 10, 286);
        groupName = Edit(groupTarget, 14, 43, 286, "角色名称"); groupName.MaxLength = Globals.MaxCharacterNameLength;
        groupName.TextSubmitted += _ => SubmitGroupTarget();
        Button(groupTarget, "确认", 14, 93, 132, SubmitGroupTarget);
        Button(groupTarget, "取消", 168, 93, 132, () => groupTarget.Hide());
        groupInvitation = new ConfirmationDialog { Title = "组队邀请" }; game.AddChild(groupInvitation);
        groupInvitation.Confirmed += () => RespondGroupInvitation(true);
        groupInvitation.Canceled += () => RespondGroupInvitation(false);
        groupLeave = new ConfirmationDialog { Title = "退出队伍", DialogText = "关闭组队并离开当前队伍？组队地图可能将你传送离开。" }; game.AddChild(groupLeave);
        groupLeave.Confirmed += () => SetGroupMode(false);
        Draggable(group); Draggable(groupTarget);
    }
    public void OpenGroup() { if (State.User == null) return; group.Show(); RenderGroup(); }
    public void SetGroupMode(bool enabled) { if (State.User != null) Send(new C.SwitchGroup { AllowGroup = enabled }); }
    private void ToggleGroupMode() { if (State.AllowGroup && State.GroupMembers.Count > 0) groupLeave.PopupCentered(); else SetGroupMode(!State.AllowGroup); }
    private void ShowGroupTarget(bool remove, string name = "")
    {
        if (!GroupLeader) { Log("只有队长可以邀请或移除成员。"); return; }
        removingGroupMember = remove; groupPrompt.Text = remove ? "移除队伍成员" : "邀请玩家组队";
        groupName.Text = name != "" ? name : State.Actors.TryGetValue(World.Selected, out var actor) && actor.Kind == ObjectType.Player ? actor.Name : "";
        groupTarget.Show(); groupName.GrabFocus();
    }
    private void SubmitGroupTarget()
    {
        if (removingGroupMember) RemoveGroupMember(groupName.Text); else InviteGroup(groupName.Text);
        groupTarget.Hide();
    }
    public void InviteGroup(string name)
    {
        name = name.Trim();
        if (State.User == null || !GroupLeader || State.GroupMembers.Count >= Globals.MaxGroup || name.Length < Globals.MinCharacterNameLength || name.Length > Globals.MaxCharacterNameLength || name.Equals(State.User.Name, StringComparison.OrdinalIgnoreCase)) { Log("请填写其他在线玩家的角色名；队伍已满或你不是队长时无法邀请。"); return; }
        Send(new C.AddMember { Name = name });
    }
    public void RemoveGroupMember(string name)
    {
        if (State.User != null && GroupLeader && State.GroupMembers.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase)) Send(new C.DelMember { Name = name.Trim() });
    }
    public void RespondGroupInvitation(bool accept)
    {
        if (State.GroupInvitation.Length == 0) return;
        State.GroupInvitation = ""; State.GroupRevision++; groupInvitation.Hide();
        Send(new C.GroupInvite { AcceptInvite = accept });
        if (accept) OpenGroup();
    }
    private void ProcessGroup()
    {
        if (State.GroupRevision != groupRevision) { groupRevision = State.GroupRevision; RenderGroup(); }
    }
    private void RenderGroup()
    {
        if (State.User == null) return;
        groupSwitch.Index = State.AllowGroup ? 117 : 114; groupSwitch.HoverIndex = groupSwitch.Index + 1; groupSwitch.PressedIndex = groupSwitch.Index + 2;
        groupAdd.Index = State.GroupMembers.Count > 0 ? 133 : 130; groupAdd.HoverIndex = groupAdd.Index + 1; groupAdd.PressedIndex = groupAdd.Index + 2;
        groupAdd.Visible = GroupLeader; groupRemove.Visible = GroupLeader && State.GroupMembers.Count > 0;
        groupStatus.Text = State.GroupMembers.Count == 0 ? (State.AllowGroup ? "允许组队 · 等待邀请" : "组队已关闭") : $"{State.GroupMembers.Count}/{Globals.MaxGroup} 人 · !!内容 发送队聊";
        for (var i = 0; i < groupRows.Count; i++)
        {
            var row = groupRows[i]; row.Visible = i < State.GroupMembers.Count;
            if (!row.Visible) continue;
            var name = State.GroupMembers[i]; row.Text = (i == 0 ? "★ " : "") + name;
            var map = State.GroupMaps.GetValueOrDefault(name, "未知地图");
            row.TooltipText = name + " · " + map + (State.GroupLocations.TryGetValue(name, out var point) ? $" ({point.X},{point.Y})" : "");
        }
        if (State.GroupInvitation == "") groupInvitation.Hide();
        else { groupInvitation.DialogText = $"{State.GroupInvitation} 邀请你加入队伍，是否接受？"; if (!groupInvitation.Visible) groupInvitation.PopupCentered(); }
    }
    private void CloseGroup() { group.Hide(); groupTarget.Hide(); groupInvitation.Hide(); groupLeave.Hide(); }
}
