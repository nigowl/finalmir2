using Godot;
using Point = System.Drawing.Point;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSpellSmoke
{
    private async Task LeaveCharacter()
    {
        if (State.User != null) Client.SetDestination(State.User.Location);
        for (var attempt = 0; attempt < 20 && State.User != null; attempt++)
        {
            Client.Send(new C.LogOut()); await Pause(1100);
        }
        if (State.User != null) throw new Exception("隔离角色未能正常退出。");
    }

    private Control ClassPanel()
    {
        Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Single(button => button.TooltipText == "游戏设置").EmitSignal(BaseButton.SignalName.Pressed);
        var tabs = Client.FindChildren("*", "TabContainer", true, false).Cast<TabContainer>().Single();
        if (tabs.GetTabCount() != 5 || tabs.GetTabTitle(3) != "职业") throw new Exception("缺少第四个职业页签。");
        tabs.CurrentTab = 3; return tabs.GetChild<Control>(3);
    }

    private static CheckBox ClassOption(Control panel, string prefix) => panel.FindChildren("*", "CheckBox", true, false).Cast<CheckBox>().Single(option => option.Text.StartsWith(prefix));
    private void CloseClassPanel() => Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Single(button => button.TooltipText == "关闭设置").EmitSignal(BaseButton.SignalName.Pressed);
    private int MaterialCount(short shape) => State.User!.Inventory.Concat(State.User.Equipment).Where(item => item?.Info is { Type: ItemType.Amulet } info && info.Shape == shape).Sum(item => item.Count);

    private async Task VerifyClassSkills()
    {
        foreach (var job in new[] { MirClass.Wizard, MirClass.Taoist })
        {
            stage = $"{job}职业页与原TCP辅助"; packets.Clear();
            await Client.Connect(Argument("--server")); await Until(() => Client.HandshakeReady);
            Client.Login($"Native{job}", "Native123"); await Until(() => State.Characters.Any(character => character.Class == job));
            Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single(character => character.Class == job).Index });
            await Until(() => State.User?.Class == job && !Client.World.Loading);
            var panel = ClassPanel();
            ClassOption(panel, "智能组合").ButtonPressed = false;
            var expected = ClassSkillRules.Maintained(job).Select(spell => "自动" + spell.ToLocalizedString()).ToArray();
            foreach (var title in expected)
            {
                var option = ClassOption(panel, title);
                if (option.ButtonPressed) throw new Exception($"{job}辅助初始意外开启：{title}");
                if (option.GetThemeIcon("checked").GetSize() != option.GetThemeIcon("unchecked").GetSize()) throw new Exception("职业复选框选中尺寸改变。");
                if (job != MirClass.Taoist) option.ButtonPressed = true;
            }
            var allTitles = panel.FindChildren("*", "CheckBox", true, false).Cast<CheckBox>().Select(option => option.Text).ToArray();
            foreach (var other in Enum.GetValues<MirClass>().Where(other => other != job))
                if (ClassSkillRules.Maintained(other).Any(spell => allTitles.Contains("自动" + spell.ToLocalizedString()))) throw new Exception("显示了其他职业的辅助选项。");
            if (job == MirClass.Taoist) await VerifyTaoist(panel);
            else
            {
                await Shot("settings-" + job); CloseClassPanel();
                await Until(() => ClassSkillRules.Maintained(job).All(spell => ClassSkillRules.Active(State, spell)));
                var count = packets.OfType<S.Magic>().Count(packet => ClassSkillRules.Maintained(job).Contains(packet.Spell));
                await Pause(2100);
                if (packets.OfType<S.Magic>().Count(packet => ClassSkillRules.Maintained(job).Contains(packet.Spell)) != count) throw new Exception("Buff存在时辅助仍重复施法。");
                await Shot("active-" + job);
            }
            await LeaveCharacter();
        }
        GD.Print("PASS native class skills: three-class scope, dynamic panels, dark checkbox geometry, acknowledged automatic shields/buffs/pets, original poison/amulet equipment swaps, automatic red/green poison, no duplicate summon, rejection pause and cross-map cleanup.");
    }

    private async Task VerifyTaoist(Control panel)
    {
        ClassOption(panel, "自动毒符切换").ButtonPressed = true; ClassOption(panel, "自动召唤").ButtonPressed = true;
        var summons = (OptionButton)panel.FindChild("ClassSummonSkill", true, false);
        summons.Select(summons.GetItemIndex((int)Spell.SummonSkeleton)); summons.EmitSignal(OptionButton.SignalName.ItemSelected, (long)summons.Selected);
        var count = MaterialCount(0); await Shot("settings-Taoist"); CloseClassPanel();
        await Until(() => ClassSkillRules.HasPet(State));
        if (!packets.OfType<S.EquipItem>().Any(result => result.Success && result.To == (int)EquipmentSlot.Amulet) || MaterialCount(0) != count - 1)
            throw new Exception("召唤没有经过真实换装或未恰好消耗一份护符。");
        var summonCount = packets.OfType<S.Magic>().Count(packet => packet.Spell == Spell.SummonSkeleton);
        await Pause(2400);
        if (packets.OfType<S.Magic>().Count(packet => packet.Spell == Spell.SummonSkeleton) != summonCount) throw new Exception("有宠物仍重复召唤。");
        var target = ClientAutomation.Targets(State).First();
        panel = ClassPanel(); ClassOption(panel, "自动施毒").ButtonPressed = true; CloseClassPanel();
        var menu = (PopupMenu)Client.FindChild("GameMenu", true, false);
        menu.EmitSignal(PopupMenu.SignalName.IdPressed, 7L); Client.SelectTarget(target.Id);
        foreach (short shape in new short[] { 2, 1 })
        {
            stage = $"道士{(shape == 2 ? "红" : "绿")}毒自动切换/实际生效";
            var before = MaterialCount(shape);
            await Until(() => State.Actors[target.Id].Poison.HasFlag(shape == 2 ? PoisonType.Red : PoisonType.Green));
            if (MaterialCount(shape) != before - 1) throw new Exception("红绿毒未各消耗一份服务端确认耗材。");
        }
        menu.EmitSignal(PopupMenu.SignalName.IdPressed, 7L);
        await Until(() => packets.OfType<S.DamageIndicator>().Any(packet => packet.ObjectID == target.Id && packet.Damage < 0));
        await Shot("taoist-poison-and-pet");
        stage = "毒符切回灵魂火符/单份耗材";
        count = MaterialCount(0); Client.World.Selected = target.Id; await Pause(1900); Client.Cast(Skill(Spell.SoulFireBall));
        await Until(() => packets.OfType<S.Magic>().Any(packet => packet.Spell == Spell.SoulFireBall && packet.Cast));
        if (MaterialCount(0) != count - 1) throw new Exception("毒转符没有正确耗材。");
        stage = "道士毒云双耗材/原右腕装备位";
        var green = MaterialCount(1); count = MaterialCount(0); await Pause(1900); Client.Cast(Skill(Spell.PoisonCloud));
        await Until(() => packets.OfType<S.Magic>().Any(packet => packet.Spell == Spell.PoisonCloud && packet.Cast));
        if (MaterialCount(0) != count - 5 || MaterialCount(1) != green - 5 || State.User!.Equipment[(int)EquipmentSlot.BraceletR]?.Info?.Shape != 1)
            throw new Exception("毒云没有经过原右腕换装并各消耗5份护符/绿毒。");
        await VerifyTaoistBuffs();
        panel = ClassPanel(); ClassOption(panel, "自动召唤").ButtonPressed = false; CloseClassPanel();
        var start = State.User!.Location; Client.SetDestination(new Point(start.X + 4, start.Y));
        await Until(() => State.Map?.MapIndex == 2 && !Client.World.Loading);
        stage = "禁宠地图拒绝后暂停且不重发";
        packets.Clear(); panel = ClassPanel(); ClassOption(panel, "自动召唤").ButtonPressed = true; CloseClassPanel();
        await Until(() => packets.OfType<S.Chat>().Any(packet => packet.Message.Contains("无法召唤")));
        await Pause(8500);
        if (packets.OfType<S.Magic>().Count(packet => packet.Spell == Spell.SummonSkeleton) != 1 ||
            !((RichTextLabel)Client.FindChild("ChatMessages", true, false)).GetParsedText().Contains("职业辅助已暂停")) throw new Exception("禁宠拒绝后没有暂停，或重复消耗。");
    }

    private async Task VerifyTaoistBuffs()
    {
        stage = "道士自动幽灵盾/神圣战甲术真实Buff";
        var panel = ClassPanel();
        var buffs = ClassSkillRules.Maintained(MirClass.Taoist).Where(spell => ClassSkillRules.Buff(spell) != BuffType.None && spell != Spell.PetEnhancer).ToArray();
        foreach (var spell in buffs) ClassOption(panel, "自动" + spell.ToLocalizedString()).ButtonPressed = true;
        CloseClassPanel();
        await Until(() => buffs.All(spell => ClassSkillRules.Active(State, spell)));
        panel = ClassPanel(); ClassOption(panel, "自动治愈").ButtonPressed = true; CloseClassPanel();
        var injury = State.User!.Inventory.Single(item => item?.Info?.Name == "隔离自疗测试药");
        var mp = State.User.MP;
        packets.Clear(); Client.UseItem(injury.UniqueID);
        await Until(() => packets.OfType<S.UseItem>().Any(packet => packet.UniqueID == injury.UniqueID && packet.Success));
        var hp = State.User.HP;
        stage = "道士低血自动治愈真实施法/MP/HP";
        await Until(() => packets.OfType<S.Magic>().Any(packet => packet.Spell == Spell.Healing && packet.Cast) && State.User.MP < mp && State.User.HP > hp + 50);
        panel = ClassPanel(); ClassOption(panel, "自动治愈").ButtonPressed = false; CloseClassPanel();
        await Shot("taoist-buffs-and-healing");
    }
}
