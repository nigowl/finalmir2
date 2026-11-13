using System.Text.Json;
using Crystal.Native;
using Point = System.Drawing.Point;
using C = ClientPackets;
using S = ServerPackets;

internal static class ClassSkillChecks
{
    public static void Run(Action<bool, string> check)
    {
        var preferences = new ClientPreferences();
        check(!preferences.AutoSwapAmulet && !preferences.SmartHalfMoon && !preferences.SmartThrusting && !preferences.AutoPoison &&
            !preferences.AutoSummon && !preferences.AutoHeal && preferences.AutoSkills.Count == 0, "职业辅助默认开启");
        preferences = new() { AutoSwapAmulet = true, AutoHeal = true, HealPercent = 999, SummonSkill = Spell.FireBall,
            AutoSkills = [Spell.FireBall, Spell.MagicShield, Spell.FlamingSword, Spell.Haste, Spell.Concentration] };
        preferences.Normalize();
        var saved = JsonSerializer.Deserialize<ClientPreferences>(JsonSerializer.Serialize(preferences))!;
        check(saved.AutoSwapAmulet && saved.AutoHeal && saved.HealPercent == 95 && saved.AutoSkills.SetEquals([Spell.MagicShield, Spell.FlamingSword]) &&
            saved.SummonSkill == Spell.None, "职业设置保存或三职业技能白名单错误");
        var state = new GameState { BaseStats = new BaseStats(MirClass.Taoist) { Stats = [new BaseStat(Stat.HP) { Base = 100 }, new BaseStat(Stat.MP) { Base = 100 }] } };
        state.Apply(new S.UserInformation { ObjectID = 1, Class = MirClass.Taoist, HP = 100, MP = 100, Location = new Point(10, 10),
            Inventory = new UserItem[10], Equipment = new UserItem[14], QuestInventory = [] });
        var now = Environment.TickCount64;
        ClientMagic Learn(Spell spell) { var magic = new ClientMagic { Spell = spell, BaseCost = 10, CastTime = now - 20000 }; state.User!.Magics.Add(magic); return magic; }
        preferences = new() { AutoSkills = Enum.GetValues<MirClass>().SelectMany(ClassSkillRules.Maintained).ToHashSet() };
        foreach (var spell in preferences.AutoSkills) Learn(spell);
        var target = new Actor { Id = 2, Kind = ObjectType.Monster, Location = new Point(11, 10) }; state.Actors[2] = target;
        foreach (var job in Enum.GetValues<MirClass>())
        {
            state.User!.Class = job;
            var next = ClassSkillRules.Next(state, preferences, target, true, now);
            check(next == ClassSkillRules.Maintained(job).FirstOrDefault(), $"{job}选择了其他职业辅助技能");
        }
        state.User!.Class = MirClass.Wizard;
        state.Me!.MagicShield = true;
        check(ClassSkillRules.Next(state, preferences, target, false, now) == Spell.MagicBooster, "已存在魔法盾仍反复施放");
        state.DisabledSkills.Add(Spell.MagicBooster);
        check(ClassSkillRules.Next(state, preferences, target, false, now) == Spell.None, "职业辅助绕过技能关闭");
        state.DisabledSkills.Clear(); state.Me.MagicShield = false;
        state.User.MP = 9;
        check(ClassSkillRules.Next(state, preferences, target, false, now) == Spell.None, "职业辅助缺蓝仍施法");
        state.User.MP = 10;
        check(ClassSkillRules.Ready(state, state.User.Magics.First(m => m.Spell == Spell.MagicShield), now) &&
            !ClassSkillRules.Ready(state, state.User.Magics.First(m => m.Spell == Spell.FlamingSword), now), "蓄力与普通法术等额MP边界不符合原服");
        state.User.MP = 100; state.User.Class = MirClass.Warrior;
        var flame = state.User.Magics.First(m => m.Spell == Spell.FlamingSword); flame.CastTime = now - 9999;
        check(!ClassSkillRules.Ready(state, flame, now), "自动烈火绕过10秒准备冷却");
        state.User.Class = MirClass.Taoist; preferences = new() { AutoSummon = true, SummonSkill = Spell.SummonSkeleton };
        Learn(Spell.SummonSkeleton); Learn(Spell.SummonHolyDeva);
        check(ClassSkillRules.Next(state, preferences, null, false, now) == Spell.SummonSkeleton, "自动召唤忽略用户指定技能");
        check(!ClassSkillRules.Confirmed(state, Spell.SummonSkeleton, 0, 0, 100), "召唤未出现宠物即算成功");
        state.Actors[3] = new Actor { Id = 3, Kind = ObjectType.Monster, MasterObjectId = 1, Hidden = true };
        check(ClassSkillRules.Next(state, preferences, null, false, now) == Spell.None && ClassSkillRules.Confirmed(state, Spell.SummonSkeleton, 0, 0, 100), "已有存活宠物仍召唤");
        state.Actors[3].Dead = true;
        check(ClassSkillRules.Next(state, preferences, null, false, now) == Spell.SummonSkeleton, "宠物死亡后无法补召");
        state.Actors.Remove(3); preferences = new() { AutoHeal = true, HealPercent = 70 }; var heal = Learn(Spell.Healing); state.User.HP = 70;
        check(ClassSkillRules.Next(state, preferences, null, false, now) == Spell.Healing, "自疗生命阈值不生效");
        heal.CastTime = now - 4999;
        check(ClassSkillRules.Next(state, preferences, null, false, now) == Spell.None, "自疗没有5秒最短间隔");
        check(!ClassSkillRules.Confirmed(state, Spell.Healing, 1, 0, 70), "自疗没有实际回血即算成功");
        state.User.HP = 71; check(ClassSkillRules.Confirmed(state, Spell.Healing, 1, 0, 70), "实际自疗回血未确认");
        state.StorageActive = true;
        check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.None, "仓库操作中仍自动施法"); state.StorageActive = false;
        state.TradePartner = "隔离交易";
        check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.None, "交易中仍自动施法"); state.TradePartner = "";
        Materials(check, state, target, now, Learn);
        SmartMelee(check, state, Learn);
    }

    private static void Materials(Action<bool, string> check, GameState state, Actor target, long now, Func<Spell, ClientMagic> learn)
    {
        UserItem Item(short shape, ulong id, ushort count) => new(new ItemInfo { Type = ItemType.Amulet, Shape = shape }) { UniqueID = id, Count = count };
        var red = Item(2, 101, 10); var green = Item(1, 102, 10); var amulet = Item(0, 103, 10);
        state.User!.Inventory[0] = red; state.User.Inventory[1] = green; state.User.Inventory[2] = amulet;
        var materials = SkillMaterials.Required(state, Spell.Poisoning, target);
        check(materials.Single().Shape == 2 && SkillMaterials.NextChange(state, materials) is C.EquipItem { UniqueID: 101, To: 9 }, "首轮红毒未正确选择原护符装备包");
        check(red.Count == 10 && state.User.Equipment[9] == null, "自动换装在服务端回执前修改资产");
        state.User.Equipment[9] = red; state.User.Inventory[0] = null!; target.Poison = PoisonType.Red;
        materials = SkillMaterials.Required(state, Spell.Poisoning, target);
        check(materials.Single().Shape == 1 && !SkillMaterials.Equipped(state, materials.Single()) && SkillMaterials.NextChange(state, materials) is C.EquipItem { UniqueID: 102, To: 9 }, "红毒生效后没有切换绿毒");
        state.User.Equipment[6] = red; state.User.Equipment[9] = green; state.User.Inventory[1] = null!;
        check(!SkillMaterials.Equipped(state, new(1, 1)) && SkillMaterials.NextChange(state, materials) is C.RemoveItem { UniqueID: 101 }, "忽略原引擎第一份毒粉优先顺序");
        for (var i = 0; i < state.User.Inventory.Length; i++) state.User.Inventory[i] ??= amulet;
        check(SkillMaterials.NextChange(state, materials) == null, "无背包空位仍卸下优先毒粉");
        Array.Clear(state.User.Inventory); Array.Clear(state.User.Equipment);
        state.User.Equipment[9] = amulet; state.User.Inventory[0] = green;
        materials = SkillMaterials.Required(state, Spell.PoisonCloud, target);
        check(materials.Length == 2 && materials.All(m => m.Count == 5) && SkillMaterials.NextChange(state, materials) is C.EquipItem { UniqueID: 102, To: 6 }, "毒云双材料没有使用原合法右腕位");
        green.Count = 4;
        check(SkillMaterials.Missing(state, materials).Contains("5 份绿毒粉"), "毒云数量不足没有可见错误"); green.Count = 10;
        state.User.Equipment[6] = green;
        check(materials.All(m => SkillMaterials.Equipped(state, m)), "毒云双耗材已装备仍反复换装");
        learn(Spell.Poisoning); var preferences = new ClientPreferences { AutoPoison = true };
        check(ClassSkillRules.Next(state, preferences, target, false, now) == Spell.None && ClassSkillRules.Next(state, preferences, target, true, now) == Spell.Poisoning, "自动施毒没有限制为挂机战斗");
        target.Kind = ObjectType.Merchant;
        check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.None, "自动施毒误打NPC"); target.Kind = ObjectType.Monster;
        target.MasterObjectId = state.User.ObjectID;
        check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.None, "自动施毒误打召唤宠物"); target.MasterObjectId = 0;
        target.Poison |= PoisonType.Green;
        check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.None && ClassSkillRules.Confirmed(state, Spell.Poisoning, target.Id, 1, 0), "红绿毒已齐仍重复消耗");
        target.Poison = PoisonType.None; target.AI = 3;
        check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.None, "对原Tree持续放无效施毒"); target.AI = 0;
        state.Apply(new S.ObjectPoisoned { ObjectID = target.Id, Poison = PoisonType.Red });
        check(target.Poison == PoisonType.Red, "原中毒回执未更新目标状态");
    }

    private static void SmartMelee(Action<bool, string> check, GameState state, Func<Spell, ClientMagic> learn)
    {
        state.Actors.Remove(2); state.User!.MP = 100; learn(Spell.HalfMoon);
        state.Actors[4] = new Actor { Id = 4, Kind = ObjectType.Monster, Location = new Point(11, 10) };
        check(ClassSkillRules.SmartMelee(state, MirDirection.Right) == Spell.None, "单怪触发智能半月");
        state.Actors[5] = new Actor { Id = 5, Kind = ObjectType.Monster, Location = new Point(10, 11) };
        check(ClassSkillRules.SmartMelee(state, MirDirection.Right) == Spell.HalfMoon, "真实半月四方向范围内多怪未触发");
        state.Actors[4].AI = 3;
        check(ClassSkillRules.SmartMelee(state, MirDirection.Right) == Spell.None, "智能半月对原Tree目标额外耗蓝"); state.Actors[4].AI = 0;
        state.Actors[5].MasterObjectId = 1;
        check(ClassSkillRules.SmartMelee(state, MirDirection.Right) == Spell.None, "宠物被统计为半月敌人");
        state.Actors[5].MasterObjectId = 0; state.Actors[5].Location = new Point(9, 10);
        check(ClassSkillRules.SmartMelee(state, MirDirection.Right) == Spell.None, "背后目标误算半月扇区");
        learn(Spell.CrossHalfMoon);
        check(ClassSkillRules.SmartMelee(state, MirDirection.Right) == Spell.CrossHalfMoon, "圆月未覆盖背后多怪");
        state.DisabledSkills.UnionWith([Spell.HalfMoon, Spell.CrossHalfMoon]);
        check(ClassSkillRules.SmartMelee(state, MirDirection.Right) == Spell.None, "智能半月绕过技能关闭");
    }
}
