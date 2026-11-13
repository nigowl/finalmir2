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
