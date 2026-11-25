using Crystal.Native;
using S = ServerPackets;

internal static class ClassComboEdgeChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var job in Enum.GetValues<MirClass>())
        {
            var state = new GameState { BaseStats = new BaseStats(job) { Stats = [new(Stat.HP) { Base = 100 }, new(Stat.MP) { Base = 100 }] } };
            state.Apply(new S.UserInformation { ObjectID = 1, Class = job, HP = 100, MP = 100, Inventory = new UserItem[80], Equipment = new UserItem[14], QuestInventory = [], Location = new(10, 10) });
            foreach (var spell in Enum.GetValues<Spell>().Where(spell => (byte)spell is > 0 and < 200)) state.User!.Magics.Add(new ClientMagic { Spell = spell, BaseCost = 10 });
            var target = new Actor { Id = 2, Kind = ObjectType.Monster, Location = new(11, 10) }; state.Actors[2] = target;
            var preferences = new ClientPreferences(); const long now = 100000;
            state.User!.MP = 0;
            check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.None && ClassSkillRules.AttackMagic(state, preferences, target, now) == null &&
                !ClassSkillRules.WaitForRanged(state, preferences, target, now), $"{job}缺蓝仍等待法术，无法回退平砍");
            state.User.MP = 100;
            foreach (var magic in state.User.Magics) { magic.CastTime = now; magic.Delay = 1000; }
            check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.None && ClassSkillRules.AttackMagic(state, preferences, target, now) == null, $"{job}绕过独立技能冷却");
            foreach (var magic in state.User.Magics) { magic.CastTime = 0; magic.Delay = 0; }
            state.StorageActive = true;
            check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.None, $"{job}仓库中自动维护技能"); state.StorageActive = false;
            target.AI = 3;
            check(ClassSkillRules.AttackMagic(state, preferences, target, now) == null && ClassSkillRules.Melee(state, MirDirection.Right, Spell.None, true) == Spell.None, $"{job}对仅普攻目标仍发技能"); target.AI = 0;
            preferences.SmartBattle = false;
            check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.None && ClassSkillRules.AttackMagic(state, preferences, target, now) == null, $"{job}关闭智能组合后仍自动施法");
        }
        PoisonCombos(check); ActualRange(check);
    }

    private static void PoisonCombos(Action<bool, string> check)
    {
        var state = new GameState { BaseStats = new BaseStats(MirClass.Archer) { Stats = [new(Stat.HP) { Base = 100 }] } };
        state.Apply(new S.UserInformation { ObjectID = 1, Class = MirClass.Archer, HP = 100, MP = 100, Inventory = new UserItem[80], Equipment = new UserItem[14], QuestInventory = [], Location = new(10, 10) });
        foreach (var spell in new[] { Spell.DoubleShot, Spell.PoisonShot, Spell.CrippleShot, Spell.NapalmShot, Spell.VampireShot, Spell.PoisonSword }) state.User!.Magics.Add(new ClientMagic { Spell = spell, BaseCost = 10 });
        var target = new Actor { Id = 2, Kind = ObjectType.Monster, Location = new(11, 10) }; state.Actors[2] = target;
        var preferences = new ClientPreferences(); const long now = 100000;
        check(ClassSkillRules.AttackMagic(state, preferences, target, now)?.Spell == Spell.PoisonShot, "弓箭手单怪未先铺毒");
        target.Poison = PoisonType.Green;
        check(ClassSkillRules.AttackMagic(state, preferences, target, now)?.Spell == Spell.DoubleShot, "弓箭手已有毒仍反复铺毒");
        state.UserBuffs[BuffType.PoisonShot] = new() { Type = BuffType.PoisonShot };
        check(ClassSkillRules.AttackMagic(state, preferences, target, now)?.Spell == Spell.DoubleShot, "单怪浪费致残箭的范围增益");
        state.Actors[3] = new Actor { Id = 3, Kind = ObjectType.Monster, Location = new(12, 10) };
        check(ClassSkillRules.AttackMagic(state, preferences, target, now)?.Spell == Spell.CrippleShot, "双怪没有消耗毒箭增益打范围毒");
        state.UserBuffs[BuffType.PoisonShot].Paused = true;
        check(ClassSkillRules.AttackMagic(state, preferences, target, now)?.Spell == Spell.NapalmShot, "暂停的毒箭增益仍被当作可触发");
        state.User!.HP = 50;
        check(ClassSkillRules.AttackMagic(state, preferences, target, now)?.Spell == Spell.VampireShot, "低血未优先吸血");
        state.User.HP = 100; state.User.Class = MirClass.Assassin; target.Poison = PoisonType.None;
        check(ClassSkillRules.AttackMagic(state, preferences, target, now) == null, "刺客缺毒仍选择毒剑");
        state.User.Inventory[6] = new(new ItemInfo { Type = ItemType.Amulet, Shape = 2 }) { UniqueID = 10, Count = 10 };
        check(ClassSkillRules.AttackMagic(state, preferences, target, now)?.Spell == Spell.PoisonSword, "刺客未准备背包毒粉");
        target.Poison = PoisonType.Green;
        check(ClassSkillRules.AttackMagic(state, preferences, target, now) == null, "刺客重复消耗毒粉刷新已有毒");
        target.Poison = PoisonType.None; target.Location = new(12, 10);
        check(ClassSkillRules.AttackMagic(state, preferences, target, now) == null, "刺客隔位空放毒剑");
        var aim = ClientSkillRules.Aim(Spell.PoisonSword, state.Me!, target, new(3, 3));
        check(aim?.Location == target.Location, "毒剑未朝实际目标施放");
    }

    private static void ActualRange(Action<bool, string> check)
    {
        var state = new GameState(); state.Apply(new S.UserInformation { ObjectID = 1, Class = MirClass.Wizard, HP = 100, MP = 100,
            Inventory = new UserItem[80], Equipment = new UserItem[14], QuestInventory = [], Location = new(10, 10) });
        var magic = new ClientMagic { Spell = Spell.FireBall, Range = 4, BaseCost = 10 }; state.User!.Magics.Add(magic);
        var target = new Actor { Id = 2, Kind = ObjectType.Monster, Location = new(15, 10) }; state.Actors[2] = target;
        check(ClassSkillRules.AttackMagic(state, new(), target, 100000) == null, "组合忽略版本实际施法距离");
        magic.CastTime = 99500; magic.Delay = 1000;
        check(!ClassSkillRules.WaitForRanged(state, new(), target, 100000), "实际射程外仍原地等待远程冷却");
    }
}
