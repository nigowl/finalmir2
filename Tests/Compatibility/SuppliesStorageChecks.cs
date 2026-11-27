using Crystal.Native;
using Mir2.Companions;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using S = ServerPackets;

internal static class SuppliesStorageChecks
{
    internal static void Run(Action<bool, string> check)
    {
        int[] levels = [1, 11, 21, 31, 41, 51, 66];
        for (var i = 0; i < levels.Length; i++) check(BasicDropBalance.Tier(levels[i], 1) == i, "等级分档边界错误");
        check(BasicDropBalance.Tier(1, 501) == 3 && BasicDropBalance.Tier(10, 50) == 0 && BasicDropBalance.Tier(25, 155) == 2, "生命没有参与补给分档");
        var gear = new ItemInfo { Type = ItemType.Armour, RequiredClass = RequiredClass.Warrior | RequiredClass.Wizard };
        check(BasicDropBalance.GeneralEquipment(gear), "多职业装备没有归入通用装备");
        gear.RequiredClass = RequiredClass.Archer; check(!BasicDropBalance.GeneralEquipment(gear), "单职业装备限制被取消");
        gear.RequiredClass = (RequiredClass)31; check(!BasicDropBalance.GeneralEquipment(gear), "已无限制装备重复改写");
        gear.Type = ItemType.Book; gear.RequiredClass = (RequiredClass)7;
        check(!BasicDropBalance.GeneralEquipment(gear), "技能书被误改为不限职业");
        var state = new GameState { User = new S.UserInformation { Class = MirClass.Archer, Equipment = new UserItem[14], Inventory = new UserItem[46], Magics = [] } };
        gear.Type = ItemType.Armour; gear.RequiredClass = (RequiredClass)31;
        check(!ItemPresentation.Requirements(new UserItem(gear), state).Any(requirement => requirement.Text.Contains("职业")), "不限职业装备仍显示职业限制");
        PotionChecks(check); PageChecks(check); UnusedChecks(check); PersonalStorageChecks.Run(check);
    }
    private static void PotionChecks(Action<bool, string> check)
    {
        UserItem Potion(ulong id, int hp) => new(new ItemInfo { Type = ItemType.Potion, Shape = 0, Stats = new Stats { [Stat.HP] = hp } }) { UniqueID = id };
        var small = Potion(1, 30); var medium = Potion(2, 150); var large = Potion(3, 500);
        var state = new GameState { BaseStats = new BaseStats(MirClass.Warrior) { Stats = [new BaseStat(Stat.HP) { Base = 500 }] } };
        state.Apply(new S.UserInformation { ObjectID = 1, Class = MirClass.Warrior, HP = 100, Inventory = [small, medium, large], Equipment = new UserItem[14] });
        check(new ClientAutomation().NextItem(state, new ClientPreferences { AutoHp = true }, 1000) == large, "低血仍优先消耗首格小药");
        state.User!.HP = 350;
        check(new ClientAutomation().NextItem(state, new ClientPreferences { AutoHp = true, HpPercent = 90 }, 1000) == medium, "可补齐时仍浪费超量药");
    }
    private static void PageChecks(Action<bool, string> check)
    {
        var world = Envir.Main; var quests = world.QuestInfoList; var config = Settings.StorageQuestIds;
        try
        {
            Settings.StorageQuestIds = [70001, 70002, 70003];
            world.QuestInfoList = Settings.StorageQuestIds.Select(id => new QuestInfo { Index = id }).ToList();
            var account = new AccountInfo(); var character = new CharacterInfo(); account.Characters.Add(character);
            check(account.Storage.Length == 80 && account.IsValidStorageIndex(79) && !account.IsValidStorageIndex(-1) && !account.IsValidStorageIndex(80), "默认仓库一权限或索引边界错误");
            character.CompletedQuests.Add(70002);
            check(!account.UnlockQuestStorage(), "跳过前置开放了仓库三");
            character.CompletedQuests.Add(70001);
            check(account.UnlockQuestStorage() && account.Storage.Length == 240 && account.IsValidStorageIndex(239) && !account.IsValidStorageIndex(240), "实际完成集合没有逐级开通二/三");
            var item = new UserItem(new ItemInfo { Index = 1 }) { UniqueID = 123 }; account.Storage[239] = item;
            character.CompletedQuests.Add(70003); check(account.UnlockQuestStorage() && account.Storage[239] == item && account.IsValidStorageIndex(319) && !account.IsValidStorageIndex(320), "仓库四扩容丢物或越界");
            check(!account.UnlockQuestStorage() && account.ExpandedStorageExpiryDate == DateTime.MaxValue, "永久开通重复改写或到期");
            var state = new GameState { User = new S.UserInformation { HasExpandedStorage = true } };
            state.Apply(new S.UserStorage { Storage = account.Storage }); check(state.StorageCapacity == 320, "客户端仍限制为两页");
            state.User.HasExpandedStorage = false; check(state.StorageCapacity == 80, "失效扩展仓库仍允许操作");
        }
        finally { world.QuestInfoList = quests; Settings.StorageQuestIds = config; }
    }
    private static void UnusedChecks(Action<bool, string> check)
    {
        UserItem Ring(ulong id, int value) => new(new ItemInfo { Index = (int)id, Type = ItemType.Ring, Durability = 100, Stats = new Stats { [Stat.MaxDC] = value } })
            { UniqueID = id, CurrentDura = 100, MaxDura = 100 };
        var worn = new UserItem[14]; worn[7] = Ring(1, 5); worn[8] = Ring(2, 5);
        var items = new[] { Ring(3, 4), Ring(4, 3), Ring(5, 2), Ring(6, 1) };
        UserItem[] Candidates(HashSet<int> protect) => CompanionInventoryRules.StorageUnused(items, worn, protect, MirClass.Warrior, MirGender.Male, 30, new HashSet<Spell>(), false);
        check(Candidates([]).Select(item => item.UniqueID).SequenceEqual(new ulong[] { 5, 6 }), "不同定义闲置装备仍满包或没有保留最好双槽备装");
        check(Candidates([5]).Select(item => item.UniqueID).SequenceEqual(new ulong[] { 6 }), "任务物品被存入仓库");
        items[3].Info.Bind = BindMode.DontStore; check(Candidates([5]).Length == 0, "禁存物品仍被规划存入");
        items[2].Info.Stats[Stat.MaxDC] = 10; check(!Candidates([]).Any(item => item.UniqueID == 5), "可升级装备被先存仓");
    }
}
