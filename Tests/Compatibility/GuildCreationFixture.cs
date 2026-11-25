using System.Drawing;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

internal static class GuildCreationFixture
{
    public static void Configure(Envir world, MapInfo map, Point start, string root)
    {
        if (!root.Contains("/mir2-guild-create.", StringComparison.Ordinal)) throw new InvalidOperationException("建会夹具只允许独立版本。");
        foreach (var area in world.MapInfoList) { area.Respawns.Clear(); area.Movements.Clear(); }
        world.AccountList.Clear(); world.CharacterList.Clear(); world.GuildList.Clear(); world.QuestInfoList.Clear(); world.NPCInfoList.Clear();
        var token = new ItemInfo { Index = ++world.ItemIndex, Name = "隔离建会材料", Type = ItemType.CraftingMaterial, StackSize = 10 };
        world.ItemInfoList.Add(token);
        foreach (var name in new[] { "CreateFounder", "CreatePoor", "CreateYoung", "CreateExisting" })
        {
            var account = new AccountInfo { Index = ++world.NextAccountID, AccountID = name, Password = "Native123", Gold = name == "CreatePoor" ? 999U : 5000U,
                CreationDate = DateTime.UtcNow, CreationIP = "127.0.0.1" };
            var role = new CharacterInfo { Index = ++world.NextCharacterID, Name = name, AccountInfo = account, Class = MirClass.Warrior,
                Level = (ushort)(name == "CreateYoung" ? 1 : 22), HP = 200, MP = 50, Inventory = new UserItem[Globals.BaseInventorySize], Equipment = new UserItem[14],
                Heroes = new HeroInfo[1], CurrentMapIndex = map.Index, BindMapIndex = map.Index, CurrentLocation = start, BindLocation = start,
                CreationDate = DateTime.UtcNow, CreationIP = "127.0.0.1" };
            role.Inventory[0] = world.CreateFreshItem(token); role.Inventory[0].Count = 3;
            account.Characters.Add(role); world.AccountList.Add(account); world.CharacterList.Add(role);
            if (name != "CreateExisting") continue;
            var guild = new GuildInfo(new PlayerObject { Info = role }, "已有行会") { GuildIndex = ++world.NextGuildID };
            guild.Ranks[0].Members[0].Player = null; guild.Ranks[0].Members[0].Online = false;
            role.GuildIndex = guild.GuildIndex; world.GuildList.Add(guild); Directory.CreateDirectory(Settings.GuildPath);
            using var writer = new BinaryWriter(File.Create(Path.Combine(Settings.GuildPath, "0.mgd"))); guild.Save(writer);
        }
        world.NPCInfoList.Add(new NPCInfo { Index = ++world.NPCIndex, Name = "行会创建管理员", FileName = "NativeGuildCreate", MapIndex = map.Index,
            Location = new(start.X - 2, start.Y), Image = 0 });
        File.WriteAllText(Path.Combine("Envir", "NPCs", "NativeGuildCreate.txt"), "[@MAIN]\n#SAY\n建会需要22级、1000金币及2份隔离建会材料。\n<创建行会/@CREATEGUILD>\n\n[@CREATEGUILD]\n");
        var settings = new InIReader(Path.Combine("Configs", "GuildSettings.ini")); settings.Write("Guilds", "MinimumLevel", 22);
        settings.Write("Required-0", "ItemName", ""); settings.Write("Required-0", "Amount", 1000);
        settings.Write("Required-1", "ItemName", token.Name); settings.Write("Required-1", "Amount", 2); settings.Write("Required-2", "Amount", 0); settings.Save();
        world.SaveDB(); world.SaveAccounts();
        Console.WriteLine("PASS isolated guild creation fixture: ordinary applicants, original level/1000 gold/two material costs and existing guild.");
    }
}
