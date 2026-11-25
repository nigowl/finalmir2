using System.Drawing;
using System.Text.Json;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

internal static class GuildWarFixture
{
    public static void Configure(Envir world, MapInfo map, Point start, string root)
    {
        if (!root.Contains("/mir2-guild-war.", StringComparison.Ordinal)) throw new InvalidOperationException("行会战争夹具仅允许独立临时版本。");
        foreach (var area in world.MapInfoList) { area.Respawns.Clear(); area.Movements.Clear(); area.SafeZones.Clear(); area.NoFight = false; }
        var safe = new Point(start.X - 3, start.Y - 3);
        map.SafeZones.Add(new SafeZoneInfo { Info = map, Location = safe, Size = 0, StartPoint = true });
        world.QuestInfoList.Clear(); world.AccountList.Clear(); world.CharacterList.Clear(); world.GuildList.Clear();
        world.NPCInfoList.Clear();
        world.NPCInfoList.Add(new NPCInfo { Index = ++world.NPCIndex, MapIndex = map.Index, FileName = "NativeGuildWar", Name = "行会战争管理员",
            Image = 0, Location = new(start.X - 2, start.Y - 1) });
        File.WriteAllText(Path.Combine("Envir", "NPCs", "NativeGuildWar.txt"), "[@MAIN]\n#SAY\n宣战消耗行会资金3000金币，仅会长可办理。\n<行会宣战/@REQUESTWAR>\n\n[@REQUESTWAR]\n");
        var weapon = world.ItemInfoList.First(item => item.Type == ItemType.Weapon && item.Shape == 1);
        weapon.RequiredClass = RequiredClass.Warrior; weapon.RequiredAmount = 0; weapon.Weight = 0;
        weapon.Stats[Stat.MinDC] = weapon.Stats[Stat.MaxDC] = 80; weapon.Stats[Stat.Accuracy] = 100;
        var roles = new List<CharacterInfo>();
        var positions = Enumerable.Range(0, 16).Select(i => new Point(start.X + i % 4, start.Y + i / 4)).ToArray();
        (positions[1], positions[4]) = (positions[4], positions[1]);
        (positions[6], positions[4]) = (positions[4], positions[6]);
        (positions[12], positions[5]) = (positions[5], positions[12]);
        for (var i = 0; i < 16; i++)
        {
            var name = $"WarClient{i:00}";
            var account = new AccountInfo { Index = ++world.NextAccountID, AccountID = name, Password = "Native123", Gold = 50000,
                CreationDate = DateTime.UtcNow, CreationIP = "127.0.0.1" };
            var position = positions[i];
            var role = new CharacterInfo { Index = ++world.NextCharacterID, Name = name, AccountInfo = account,
                Class = MirClass.Warrior, Gender = MirGender.Male, Level = 50, HP = 900, MP = 100, Inventory = new UserItem[Globals.BaseInventorySize],
                Equipment = new UserItem[14], Heroes = new HeroInfo[1], CurrentMapIndex = map.Index, BindMapIndex = map.Index,
                CurrentLocation = position, BindLocation = safe, CreationDate = DateTime.UtcNow, CreationIP = "127.0.0.1" };
            role.Equipment[(int)EquipmentSlot.Weapon] = world.CreateFreshItem(weapon);
            roles.Add(role); account.Characters.Add(role); world.AccountList.Add(account); world.CharacterList.Add(role);
        }
        foreach (var (offset, name) in new[] { (0, "WarGuildA"), (6, "WarGuildB"), (12, "WarGuildC") })
        {
            var leader = roles[offset];
            var info = new GuildInfo(new PlayerObject { Info = leader }, name) { GuildIndex = ++world.NextGuildID, Gold = offset == 12 ? 2999U : 10000U, MemberCap = 30 };
            info.Ranks[0].Members[0].Player = null; info.Ranks[0].Members[0].Online = false;
            var members = new GuildRank { Index = 1, Name = "会员" }; info.Ranks.Add(members);
            foreach (var role in roles.Skip(offset).Take(offset == 12 ? 4 : 6))
            {
                role.GuildIndex = info.GuildIndex;
                if (role != leader) { members.Members.Add(new GuildMember { Id = role.Index, Name = role.Name }); info.Membercount++; }
            }
            world.GuildList.Add(info);
            Directory.CreateDirectory(Settings.GuildPath);
            using var writer = new BinaryWriter(File.Create(Path.Combine(Settings.GuildPath, (world.GuildList.Count - 1) + ".mgd"))); info.Save(writer);
        }
        var settings = new InIReader(Path.Combine("Configs", "GuildSettings.ini"));
        settings.Write("Guilds", "WarCost", 3000); settings.Write("Guilds", "WarTime", 1); settings.Save();
        var setup = new InIReader(Path.Combine("Configs", "Setup.ini")); setup.Write("Network", "MaxIP", 40); setup.Save();
        world.SaveDB(); world.SaveAccounts();
        File.WriteAllText(Path.Combine(root, "guild-war.json"), JsonSerializer.Serialize(new { Start = start, Safe = safe, Count = roles.Count }));
        Console.WriteLine("PASS isolated guild war fixture: 16 original TCP accounts, three guilds, real bank fees and one-minute original war timer.");
    }
}
