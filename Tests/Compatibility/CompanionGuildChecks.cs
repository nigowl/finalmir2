using Mir2.Companions;
using Server;
using Server.ConsoleHost;
using Server.Helpers;
using Server.MirDatabase;
using Server.MirObjects;

internal static class CompanionGuildChecks
{
    public static void Run(Action<bool,string> check)
    {
        foreach (var mode in new[] { "off", "join", "create", "auto" })
        { CompanionValidation.Guilds(new() { Mode = mode }); check(true, "合法行会策略被拒绝"); }
        foreach (var invalid in new CompanionGuildPreferences?[] { null, new() { Mode = "future" }, new() { Name = "ab" },
            new() { Name = new string('名',21) }, new() { Name = "bad\\guild" }, new() { Name = "bad\nguild" }, new() { Name = " Guild" } })
        {
            var rejected = false;
            try { CompanionValidation.Guilds(invalid); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "损坏或不合法的行会策略被接受");
        }
        var oldCosts = Settings.Guild_CreationCostList; var oldLevel = Settings.Guild_RequiredLevel; var oldName = Settings.NewbieGuild;
        var token = new ItemInfo { Index = 99001, Name = "GuildCostCheck", Type = ItemType.CraftingMaterial };
        var account = new AccountInfo { Gold = 1000 };
        var character = new CharacterInfo { Level = 22, AccountInfo = account, Inventory = new UserItem[46] };
        var player = new PlayerObject { Info = character };
        try
        {
            Settings.Guild_RequiredLevel = 22; Settings.NewbieGuild = "ReservedGuild";
            Settings.Guild_CreationCostList = [new() { Amount = 1000 }, new() { Item = token, Amount = 2 }];
            character.Inventory[0] = new UserItem(token) { UniqueID = 99001, Count = 1 };
            check(GuildCreationRules.Failure(player,"CheckGuild") != null, "建会缺材料未阻断");
            character.Inventory[1] = new UserItem(token) { UniqueID = 99002, Count = 1 };
            check(GuildCreationRules.Failure(player,"CheckGuild") == null, "跨堆叠建会材料未计入");
            check(account.Gold == 1000 && character.Inventory.Take(2).Sum(i => i!.Count) == 2, "只读建会预检扣除了资产");
            account.Gold = 999; check(GuildCreationRules.Failure(player,"CheckGuild") != null, "建会金币不足未阻断");
            account.Gold = 1000; character.Level = 21; check(GuildCreationRules.Failure(player,"CheckGuild") != null, "建会等级不足未阻断");
            account.AdminAccount = true; check(GuildCreationRules.Failure(player,"CheckGuild") != null, "管理员绕过了原等级条件");
            character.Level = 22; account.Gold = 0; character.Inventory = new UserItem[46];
            check(GuildCreationRules.Failure(player,"ReservedGuild") == null, "原管理员费用/保留名称规则被改变");
            account.AdminAccount = false; account.Gold = 1000;
            check(GuildCreationRules.Failure(player,"ReservedGuild") != null, "普通角色占用了新手行会保留名称");
            var ore = new ItemInfo { Index = 99003, Name = "GuildOreCheck", Type = ItemType.Ore };
            Settings.Guild_CreationCostList = [new() { Item = ore, Amount = 3 }];
            character.Inventory[0] = new UserItem(ore) { Count = 1, CurrentDura = 3999 };
            check(GuildCreationRules.Failure(player,"CheckGuild") != null, "原矿石纯度严格边界被放宽");
            character.Inventory[0].CurrentDura = 4000;
            check(GuildCreationRules.Failure(player,"CheckGuild") == null, "原矿石纯度充足分支被破坏");
        }
        finally { Settings.Guild_CreationCostList = oldCosts; Settings.Guild_RequiredLevel = oldLevel; Settings.NewbieGuild = oldName; }
    }
}
