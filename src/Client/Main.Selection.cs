using System.Text.RegularExpressions;
using Godot;
using C = ClientPackets;

public partial class Main
{
    private Label selectionNotice = null!;
    private Button createCharacterButton = null!;
    private long characterCreateSentAt;

    private void BuildCharacterCreation()
    {
        selectionNotice = Text(selection, "", 105, 704, 810);
        selectionNotice.Name = "SelectionNotice";
        var create = characterCreation = Panel(selection, 105, 630, 810, 70); create.Hide();
        characterName = Edit(create, 15, 18, 210, "新角色名称（3–15 位）");
        characterName.MaxLength = Globals.MaxCharacterNameLength;
        characterName.TextSubmitted += _ => CreateCharacter();
        characterClass = new OptionButton { Position = new Vector2(240, 18), Size = new Vector2(120, 28) };
        foreach (var job in Enum.GetValues<MirClass>()) characterClass.AddItem(job.ToLocalizedString());
        create.AddChild(characterClass); characterClass.Size = new Vector2(120, 28);
        gender = new OptionButton { Position = new Vector2(375, 18), Size = new Vector2(90, 28) };
        gender.AddItem("男"); gender.AddItem("女"); create.AddChild(gender); gender.Size = new Vector2(90, 28);
        createCharacterButton = Button(create, "创建角色", 485, 19, 120, CreateCharacter);
        Button(create, "取消", 665, 19, 120, () => create.Hide());
    }

    private void OpenCharacterCreation()
    {
        if (pendingCharacterDeletion >= 0) return;
        if (State.Characters.Count >= Globals.MaxCharacterCount)
        { selectionNotice.Text = $"该账号最多可创建{Globals.MaxCharacterCount}个角色。"; return; }
        characterCreation.Show();
        if (characterCreateSentAt == 0) { selectionNotice.Text = ""; characterName.GrabFocus(); }
    }

    private void CreateCharacter()
    {
        if (scene != "selection" || characterCreateSentAt != 0 || pendingCharacterDeletion >= 0) return;
        if (!HandshakeReady || !Connection.Connected) { selectionNotice.Text = "连接已断开，请重新登录。"; return; }
        var name = characterName.Text.Trim();
        if (!Regex.IsMatch(name, @"^[\u4e00-\u9fa5_A-Za-z0-9]{" + Globals.MinCharacterNameLength + "," + Globals.MaxCharacterNameLength + "}$"))
        { selectionNotice.Text = "角色名须为3–15位中文、英文字母、数字或下划线。"; characterName.GrabFocus(); return; }
        characterName.Text = name;
        characterCreateSentAt = Environment.TickCount64;
        SetCharacterCreationBusy(true);
        selectionNotice.Text = "正在创建角色，请稍候…";
        Send(new C.NewCharacter { Name = name, Class = (MirClass)characterClass.Selected, Gender = (MirGender)gender.Selected });
    }

    private void SetCharacterCreationBusy(bool busy)
    {
        createCharacterButton.Disabled = characterClass.Disabled = gender.Disabled = busy;
        deleteCharacterButton.Disabled = busy || pendingCharacterDeletion >= 0 || State.Characters.Count == 0;
        characterName.Editable = !busy;
    }

    internal void ProcessCharacterCreation(long now)
    {
        if (pendingCharacterDeletion >= 0 && now - characterDeleteSentAt >= 10000)
            selectionNotice.Text = "等待删角回执超时，请重新登录核对角色列表；不会自动重复提交。";
        if (characterCreateSentAt != 0 && now - characterCreateSentAt >= 10000)
            selectionNotice.Text = "等待创建回执超时，请退出后重新登录核对角色列表；不会自动重复提交。";
    }

    private void ResetCharacterCreation()
    {
        ResetCharacterDeletion();
        characterCreateSentAt = 0;
        SetCharacterCreationBusy(false);
        characterCreation.Hide();
        selectionNotice.Text = "";
    }

    private void CharacterCreated(SelectInfo character)
    {
        ResetCharacterCreation();
        characterName.Text = "";
        selectedCharacter = State.Characters.FindIndex(info => info.Index == character.Index);
        RenderCharacters();
        selectionNotice.Text = $"角色“{character.Name}”创建成功，可点击进入游戏。";
    }

    private void CharacterCreationFailed(byte result)
    {
        characterCreateSentAt = 0;
        SetCharacterCreationBusy(false);
        selectionNotice.Text = result switch
        {
            0 => "服务器暂不允许创建角色，或当前 IP 创建过于频繁，请稍后重试或联系管理员。",
            1 => "角色名不符合服务器规则或属于保留名称，请更换。",
            2 => "所选性别无效，请重新选择。",
            3 => "服务器未开放所选职业，请更换职业。",
            4 => $"该账号已达到角色数量上限（{Globals.MaxCharacterCount}个）。",
            5 => "角色名已被使用，请换一个昵称。",
            _ => $"角色创建失败（代码 {result}），请联系管理员。"
        };
    }
}
