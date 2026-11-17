using Crystal.Native;
using S = ServerPackets;

internal static class ClientSkillChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(Enumerable.Range(1, 12).Select(number => SkillKeys.FunctionKey(number)).Distinct().Count() == 12 && SkillKeys.FunctionKey(9) == 25 && SkillKeys.FunctionKey(12) == 28,
            "F1–F12映射重复或覆盖英雄键位");
        check(SkillKeys.IsPlayer(28) && !SkillKeys.IsPlayer(17) && SkillKeys.IsHero(17) && SkillKeys.IsHero(24) && !SkillKeys.IsHero(25) && !SkillKeys.IsPlayer(255),
            "新增功能键放宽未知键或破坏原英雄17–24范围");
        check(SkillKeys.FunctionKey(8, true) == 16 && SkillKeys.FunctionKey(9, true) == 0 && SkillKeys.Label(28) == "F12" && SkillKeys.Label(16) == "Shift+F8", "旧Shift键与新增F12含义错误");
        var magic = new ClientMagic { Spell = Spell.FireBall, Level = 0, Experience = 7, Need1 = 100, Need2 = 200, Need3 = 300 };
        check(ClientSkillRules.ExperienceText(magic) == "0级 · 熟练度 7/100", "零级技能经验上限错误");
        magic.Level = 1; check(ClientSkillRules.ExperienceText(magic).EndsWith("7/200"), "一级技能经验上限错误");
        magic.Level = 2; check(ClientSkillRules.ExperienceText(magic).EndsWith("7/300"), "二级技能经验上限错误");
        magic.Level = 3; check(ClientSkillRules.ExperienceText(magic).Contains("已满级"), "满级技能继续提示升级");
        check(ClientSkillRules.IsPassive(Spell.Fencing) && !ClientSkillRules.IsPassive(Spell.FireBall), "技能被动分类错误");
        check(ClientSkillRules.IsToggle(Spell.Thrusting) && !ClientSkillRules.IsToggle(Spell.FlamingSword) && ClientSkillRules.IsTrigger(Spell.FlamingSword), "蓄力技能被误当可取消开关");
        foreach (var spell in Enum.GetValues<Spell>().Where(spell => (byte)spell is > 0 and < 90))
            check(!ClientSkillRules.Description(spell).StartsWith("具体效果"), $"经典三职业缺少技能介绍：{spell}");
        var described = new ClientMagic { Name = "火球术", Spell = Spell.FireBall, Delay = 2350, BaseCost = 2, LevelCost = 3, Level = 2, Range = 9 };
        var tooltip = ClientSkillRules.Tooltip(described);
        check(tooltip.Contains("发射火球") && tooltip.Contains("冷却：2.35秒") && tooltip.Contains("基础耗蓝：8") && tooltip.Contains("施法距离：9格"), "技能说明没有使用服务端冷却/等级耗蓝/距离");
        described.Spell = Spell.Fencing;
        check(ClientSkillRules.Tooltip(described).Contains("冷却：无（被动生效）") && !ClientSkillRules.Tooltip(described).Contains("基础耗蓝"), "被动技能误显示施法冷却或耗蓝");
        described.Spell = Spell.Thrusting;
        check(ClientSkillRules.Tooltip(described, false).Contains("已关闭") && ClientSkillRules.Tooltip(described, true).Contains("已开启"), "技能介绍未跟随原技能开关");
        described.Spell = Spell.FlamingSword;
        check(ClientSkillRules.Cooldown(described) == 10000 && ClientSkillRules.Tooltip(described).Contains("冷却：10秒"), "烈火说明丢失原10秒准备限制");
        described.Delay = 12000;
        check(ClientSkillRules.Cooldown(described) == 12000, "技能说明覆盖了更长的版本冷却");
        described.Spell = Spell.FastMove;
        check(ClientSkillRules.Tooltip(described).Contains("尚未实现"), "技能介绍把未实现技能标成可用");
        check(ClientSkillRules.Description(Spell.Poisoning).Contains("绿毒") && ClientSkillRules.Description(Spell.Poisoning).Contains("红毒"), "施毒介绍缺少两类毒粉效果");
        var state = new GameState();
        var torch = new UserItem(new ItemInfo { Type = ItemType.Torch, Weight = 3 }) { Count = 1 };
        var armour = new UserItem(new ItemInfo { Type = ItemType.Armour, Weight = 7 }) { Count = 1, Slots = [torch] };
        var sword = new UserItem(new ItemInfo { Type = ItemType.Weapon, Weight = 5 }) { Count = 1 };
        state.Apply(new S.UserInformation { ObjectID = 1, Magics = [magic], Inventory = [], QuestInventory = [], Equipment = [sword, armour, null!] });
        check(state.HandWeight == 8 && state.WearWeight == 7, "穿戴/手持负重未按原物品分类或漏掉嵌入格");
        state.Apply(new S.MagicLeveled { ObjectID = 2, Spell = magic.Spell, Level = 0, Experience = 99 });
        check(magic.Level == 3 && magic.Experience == 7, "其他角色技能经验覆盖本人技能");
        state.Apply(new S.MagicLeveled { ObjectID = 1, Spell = magic.Spell, Level = 1, Experience = 42 });
        check(magic.Level == 1 && magic.Experience == 42, "本人技能升级回执未更新熟练度");
        state.DisabledSkills.Add(magic.Spell);
        state.Apply(new S.MapChanged());
        check(state.DisabledSkills.Contains(magic.Spell), "普通切图丢失本次技能启用选择");
        state.Apply(new S.LogOutSuccess());
        check(state.DisabledSkills.Count == 0 && state.HandWeight == 0 && state.WearWeight == 0, "换角泄漏技能选择或负重");
        state.Apply(new S.ObjectMonster {ObjectID=20,Dead=true,Skeleton=false});
        check(!state.Actors[20].Harvested,"新尸体误判已采集");
        state.Apply(new S.ObjectHarvested {ObjectID=20});
        check(state.Actors[20].Harvested,"采集完成回执没有阻止重复割取");
    }
}
