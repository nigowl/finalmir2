using System.Text.RegularExpressions;
using Crystal.Native;
using Godot;
using C = ClientPackets;
using S = ServerPackets;

public partial class Main
{
    private ConfirmationDialog heroCreation = null!;
    private LineEdit heroName = null!;
    private OptionButton heroClass = null!, heroGender = null!;
    private Label heroCreationNotice = null!;
    private Control heroManagement = null!;
    private ItemList managedHeroes = null!;
    private Button changeHero = null!, deleteHero = null!;
    private ConfirmationDialog heroDeletion = null!;
    private int heroDeletionPending;
    private long heroDeletionSent;
    private bool heroCreationPending, heroCreationUnknown;
    private long heroCreationSent;

    private void BuildHeroManagement()
    {
        heroCreation = new ConfirmationDialog { Name = "HeroCreation", Title = "创建英雄", DialogHideOnOk = false,
            Exclusive = true, Transient = true, Unresizable = true, Size = new Vector2I(320, 225) };
        game.AddChild(heroCreation); heroCreation.GetOkButton().Text = "创建"; heroCreation.GetCancelButton().Text = "取消";
        var fields = new VBoxContainer { CustomMinimumSize = new Vector2(288, 180) }; fields.AddThemeConstantOverride("separation", 8); heroCreation.AddChild(fields);
        heroName = AccountField(fields, "英雄名称", "HeroName", "3–15位中文、字母或数字", Globals.MaxCharacterNameLength);
        OptionButton Choice(string caption, string name)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); fields.AddChild(row);
            row.AddChild(new Label { Text = caption, CustomMinimumSize = new Vector2(80, 28), VerticalAlignment = VerticalAlignment.Center });
            var choice = new OptionButton { Name = name, CustomMinimumSize = new Vector2(0, 28), SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ActionMode = BaseButton.ActionModeEnum.Release, FocusMode = FocusModeEnum.None }; row.AddChild(choice); return choice;
        }
        heroClass = Choice("职业", "HeroClass"); heroGender = Choice("性别", "HeroGender");
        heroGender.AddItem("男", (int)MirGender.Male); heroGender.AddItem("女", (int)MirGender.Female);
        heroCreationNotice = new Label { Name = "HeroCreationNotice", CustomMinimumSize = new Vector2(288, 40), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        heroCreationNotice.AddThemeFontSizeOverride("font_size", ClientUi.Small); fields.AddChild(heroCreationNotice);
        heroCreation.Confirmed += CreateHero; heroName.TextSubmitted += _ => CreateHero();
        heroCreation.Canceled += heroCreation.Hide; heroCreation.CloseRequested += heroCreation.Hide;

        heroManagement = Panel(game, 342, 220, 340, 280); heroManagement.Name = "HeroManagement"; heroManagement.Hide();
        WindowTitle(heroManagement, "英雄管理", 280); ArtButton(heroManagement, "Prguse2", 360, 314, 3, "关闭英雄管理", heroManagement.Hide);
        managedHeroes = new ItemList { Name = "ManagedHeroes", Position = new Vector2(12, 34), Size = new Vector2(316, 172), AllowReselect = true };
        heroManagement.AddChild(managedHeroes);
        managedHeroes.ItemSelected += _ => UpdateHeroManagementButtons();
        Text(heroManagement, "最多5个英雄；删除前请先卸下全部装备。", 12, 211, 316).AddThemeFontSizeOverride("font_size", ClientUi.Small);
        changeHero = Button(heroManagement, "设为当前", 12, 241, 94, () =>
        {
            if (heroItemPending != null || heroDeletionPending != 0 || managedHeroes.GetSelectedItems().Length != 1) return;
            var slot = managedHeroes.GetItemMetadata(managedHeroes.GetSelectedItems()[0]).AsInt32();
            if (slot <= 0) return; changeHero.Disabled = true; Send(new C.ChangeHero { ListIndex = slot });
        });
        deleteHero = Button(heroManagement, "删除", 123, 241, 94, ConfirmHeroDeletion);
        heroDeletion = new ConfirmationDialog { Name = "HeroDeletion", Title = "删除英雄", Exclusive = true, Transient = true, Unresizable = true };
        game.AddChild(heroDeletion); heroDeletion.GetOkButton().Text = "删除"; heroDeletion.GetCancelButton().Text = "取消";
        heroDeletion.Confirmed += () =>
        {
            var index = heroDeletion.GetMeta("hero_index").AsInt32();
            if (heroDeletionPending != 0 || heroItemPending != null || State.User == null || !heroManagement.Visible ||
                ManagedHero(index) == null) return;
            heroDeletionPending = index; heroDeletionSent = Environment.TickCount64; UpdateHeroManagementButtons();
            Send(new C.DeleteHeroRequest { HeroIndex = index });
        };
        Button(heroManagement, "关闭", 234, 241, 94, heroManagement.Hide); Draggable(heroManagement);
    }

    private ClientHeroInformation? ManagedHero(int index) => State.ActiveHero?.Index == index ? State.ActiveHero :
        State.StoredHeroes.FirstOrDefault(hero => hero?.Index == index);

    private void UpdateHeroManagementButtons()
    {
        var selected = managedHeroes.GetSelectedItems();
        var slot = selected.Length == 1 ? managedHeroes.GetItemMetadata(selected[0]).AsInt32() : -1;
        changeHero.Disabled = heroDeletionPending != 0 || slot <= 0;
        deleteHero.Disabled = !Assets.SupportsHeroDeletion || heroDeletionPending != 0 || slot < 0 ||
            (slot == 0 ? State.ActiveHero : State.StoredHeroes.ElementAtOrDefault(slot - 1)) == null;
    }

    private void ConfirmHeroDeletion()
    {
        if (deleteHero.Disabled || heroItemPending != null || managedHeroes.GetSelectedItems().Length != 1) return;
        var slot = managedHeroes.GetItemMetadata(managedHeroes.GetSelectedItems()[0]).AsInt32();
        var selected = slot == 0 ? State.ActiveHero : State.StoredHeroes.ElementAtOrDefault(slot - 1);
        if (selected == null) return;
        heroDeletion.SetMeta("hero_index", selected.Index);
        heroDeletion.DialogText = $"确定永久删除英雄“{selected.Name}”（{selected.Level}级{selected.Class.ToLocalizedString()}）？\n等级、技能及熟练度将无法恢复。请先卸下装备；共用角色背包不受影响。";
        heroDeletion.PopupCentered(new Vector2I(390, 170));
    }

    private void CreateHero()
    {
        if (heroCreationPending || State.User == null || heroClass.Selected < 0) return;
        var name = heroName.Text.Trim();
        if (!Regex.IsMatch(name, $@"^[\u4e00-\u9fa5_A-Za-z0-9]{{{Globals.MinCharacterNameLength},{Globals.MaxCharacterNameLength}}}$"))
        { heroCreationNotice.Text = "名称须为3–15位中文、字母、数字或下划线。"; return; }
        heroCreationPending = true; heroCreationUnknown = false; heroCreationSent = Environment.TickCount64;
        LockHeroCreation(); heroCreationNotice.Text = "正在创建，请等待服务端确认…";
        Send(new C.NewHero { Name = name, Class = (MirClass)heroClass.GetSelectedId(), Gender = (MirGender)heroGender.GetSelectedId() });
    }

    private void LockHeroCreation()
    {
        heroCreation.GetOkButton().Disabled = heroCreationPending || heroClass.ItemCount == 0;
        heroName.Editable = !heroCreationPending; heroClass.Disabled = heroGender.Disabled = heroCreationPending;
    }

    private void HeroManagementPacket(Packet packet)
    {
        switch (packet)
        {
            case S.HeroCreateRequest request:
                if (!heroCreationPending)
                {
                    heroClass.Clear(); heroName.Clear();
                    foreach (var job in Enum.GetValues<MirClass>())
                        if ((int)job < request.CanCreateClass.Length && request.CanCreateClass[(int)job]) heroClass.AddItem(job.ToLocalizedString(), (int)job);
                    heroCreationNotice.Text = heroClass.ItemCount == 0 ? "当前没有开放的英雄职业。" : "选择职业、性别并填写英雄名称。";
                }
                LockHeroCreation(); heroCreation.PopupCentered(); break;
            case S.NewHero result:
                heroCreationPending = heroCreationUnknown = false; LockHeroCreation();
                var text = result.Result switch { 0 => "当前未开放英雄创建。", 1 => "英雄名称不符合要求。", 2 => "请选择有效性别。", 3 => "该英雄职业尚未开放。",
                    4 => "英雄数量已达上限。", 5 => "该角色或英雄名称已被使用。", 6 => "背包空间或负重不足，无法领取封印英雄。",
                    7 => "英雄使者会话已失效，请靠近使者重新办理。", 10 => State.User?.Inventory.Any(item => item?.Info.Type == ItemType.SealedHero) == true ?
                    "英雄创建成功，封印英雄已放入背包。请双击使用以激活，激活后显示右下角英雄栏。" :
                    "英雄创建成功，请在英雄管理中查看。", _ => $"英雄创建失败（{result.Result}）。" };
                heroCreationNotice.Text = text; Log(text, result.Result == 10 ? ChatType.System : ChatType.Hint);
                if (result.Result == 10) heroCreation.Hide(); break;
            case S.ManageHeroes:
                RenderHeroManagement(); if (State.NpcId != 0) heroManagement.Show(); break;
            case S.ChangeHero:
                RenderHeroManagement(); break;
            case S.DeleteHeroResult result when result.HeroIndex == heroDeletionPending:
                heroDeletionPending = 0; heroDeletion.Hide(); Log(result.Message, result.Success ? ChatType.System : ChatType.Hint);
                RenderHeroManagement(); break;
            case S.MapChanged or S.Death:
                heroCreation.Hide(); heroManagement.Hide(); heroDeletion.Hide(); break;
            case S.UserInformation when packet is not S.HeroInformation:
            case S.LogOutSuccess or S.ReturnToLogin or S.Disconnect:
                heroCreationPending = heroCreationUnknown = false; heroDeletionPending = 0;
                heroCreation.Hide(); heroManagement.Hide(); heroDeletion.Hide(); break;
        }
    }

    private void RenderHeroManagement()
    {
        managedHeroes.Clear(); changeHero.Disabled = deleteHero.Disabled = true;
        void Add(ClientHeroInformation hero, int slot)
        {
            var row = managedHeroes.AddItem($"{(slot == 0 ? "当前" : "备用")} · {hero.Name} · {hero.Level}级{hero.Class.ToLocalizedString()} · {(hero.Gender == MirGender.Male ? "男" : "女")}");
            managedHeroes.SetItemMetadata(row, slot);
        }
        if (State.ActiveHero is { } active) Add(active, 0);
        for (var i = 0; i < State.StoredHeroes.Length; i++) if (State.StoredHeroes[i] is { } hero) Add(hero, i + 1);
        if (managedHeroes.ItemCount == 0)
        { var row = managedHeroes.AddItem("暂无已激活英雄；请先创建并使用封印英雄。"); managedHeroes.SetItemMetadata(row, -1); }
    }

    private void ProcessHeroCreation()
    {
        if (State.User == null)
        { heroCreationPending = heroCreationUnknown = false; heroDeletionPending = 0; heroCreation.Hide(); heroManagement.Hide(); heroDeletion.Hide(); return; }
        if (heroDeletionPending != 0 && Environment.TickCount64 - heroDeletionSent >= 10000 && heroDeletionSent != 0)
        { heroDeletionSent = 0; Log("删除英雄回执未知，请重连核对；不会自动重发。", ChatType.Hint); }
        if (!heroCreationPending || heroCreationUnknown || Environment.TickCount64 - heroCreationSent < 10000) return;
        heroCreationUnknown = true; heroCreationNotice.Text = "创建回执未知，请重连核对；不会自动重发。"; Log(heroCreationNotice.Text, ChatType.Hint);
    }
}
