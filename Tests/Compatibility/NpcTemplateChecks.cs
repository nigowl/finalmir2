using System.Globalization;
using System.Runtime.CompilerServices;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

internal static class NpcTemplateChecks
{
    private sealed class TemplateMonster(MonsterInfo info) : MonsterObject(info);
    public static void Run(Action<bool, string> check)
    {
        var world = Envir.Main;
        var maps = world.MapList; var guilds = world.Guilds; var conquests = world.Conquests; var lights = world.Lights;
        try
        {
            world.MapList = []; world.Guilds = []; world.Conquests = []; world.Lights = LightSetting.Night;
            var map = new Map(new MapInfo { Index = 901, FileName = "Template Map", Title = "模板地图" });
            world.MapList.Add(map);
            var npc = (NPCObject)RuntimeHelpers.GetUninitializedObject(typeof(NPCObject));
            npc.Info = new NPCInfo { Name = "模板_商人" }; map.NPCs.Add(npc);
            var segment = new NPCSegment(new NPCPage("[@MAIN]"), [], [], [], [], []);
            var player = Player(map);
            segment.AddVariable(player, "A10", "7"); segment.AddVariable(player, "A11", "2");
            segment.AddVariable(player, "A12", ""); segment.AddVariable(player, "A13", "<$HP>");
            segment.AddVariable(player, "A14", map.Info.FileName);
            CheckText(segment, player, check);
            CheckEquipment(segment, player, check);
            CheckConquests(segment, player, check);
            CheckMonster(segment, map, check);
            CheckCallers(player, map, check);
        }
        finally { world.MapList = maps; world.Guilds = guilds; world.Conquests = conquests; world.Lights = lights; }
    }

    private static PlayerObject Player(Map map)
    {
        var player = new PlayerObject
        {
            Info = new CharacterInfo { Name = "模板检查", Class = MirClass.Warrior, Level = 25, HP = 37, MP = 19, PKPoints = 9 },
            Account = new AccountInfo { Gold = 1234, Credit = 56 },
            Stats = new Stats { [Stat.HP] = 100, [Stat.MP] = 60 }, CurrentMap = map, CurrentLocation = new(12, 34)
        };
        player.Info.Mount = new MountInfo(player);
        return player;
    }

    private static void CheckText(NPCSegment segment, PlayerObject player, Action<bool, string> check)
    {
        var world = Envir.Main;
        var values = new Dictionary<string, string>
        {
            ["USERNAME"] = "模板检查", ["NPCNAME"] = "模板 商人", ["LEVEL"] = "25", ["CLASS"] = "Warrior",
            ["MAP"] = "Template Map", ["X_COORD"] = "12", ["Y_COORD"] = "34", ["HP"] = "37", ["MAXHP"] = "100",
            ["MP"] = "19", ["MAXMP"] = "60", ["GAMEGOLD"] = "1234", ["CREDIT"] = "56", ["PKPOINT"] = "9",
            ["DATE"] = world.Now.ToShortDateString(), ["MAPLIGHT"] = "Night", ["USERCOUNT"] = world.PlayerCount.ToString(),
            ["GUILDWARTIME"] = Settings.Guild_WarTime.ToString(), ["GUILDWARFEE"] = Settings.Guild_WarCost.ToString(),
            ["PARCELAMOUNT"] = "0", ["GUILDNAME"] = Text(ServerTextKeys.NoGuild), ["AGITGUILDNAME"] = Text(ServerTextKeys.UpperNoGuild),
            ["ROLLRESULT"] = Text(ServerTextKeys.NotRolled), ["MOUNT"] = Text(ServerTextKeys.NoMount),
            ["MOUNTLOYALTY"] = Text(ServerTextKeys.NoMount), ["GUILDEXTENDFEE"] = Text(ServerTextKeys.None),
            ["GUILDRENTFEE"] = Settings.BuyGTGold.ToString(), ["GUILDGTRENTALDAYSLEFT"] = "0",
            ["OUTPUT(A10)"] = "7", ["OUTPUT(%a10)"] = "7", ["OUTPUT(A12)"] = "", ["MONSTERCOUNT(Template Map)"] = "0",
            ["MONSTERCOUNT(A14)"] = "0", ["MONSTERCOUNT(不存在)"] = "N/A", ["CASTLEWARDATE"] = Text(ServerTextKeys.NoWarScheduled)
        };
        foreach (var (key, value) in values)
        {
            check(segment.ReplaceValue(player, "前<$" + key + ">后") == "前" + value + "后", "NPC模板错误或丢失周围文本：" + key);
            check(segment.ReplaceValue(player, "<$" + key.ToLowerInvariant() + ">") == value, "NPC模板大小写错误：" + key);
        }
        check(segment.ReplaceValue(player, "生命<$HP>/<$MAXHP>，魔力<$MP>/<$MAXMP>。") == "生命37/100，魔力19/60。",
            "同一中文行上的多个NPC模板未逐个替换");
        check(segment.ReplaceValue(player, "<$HP><$HP><$MP>") == "373719", "重复或紧邻模板错误");
        check(segment.ReplaceValue(player, "<$OUTPUT(A13)><$HP>") == "<$HP>37", "替换值被再次当成模板执行");
        var original = new List<string> { "{<$USERNAME>/KHAKI}，我就是{<$NPCNAME>/KHAKI}。",
            "生命{<$HP>/<$MAXHP>/RED}，魔力{<$MP>/<$MAXMP>/ROYALBLUE}", "<购买/@Buy> <$ITEM:2147483647> <$HP>", "" };
        var parsed = segment.ParseSay(player, original);
        check(parsed.SequenceEqual(new[] { "{模板检查/KHAKI}，我就是{模板 商人/KHAKI}。", "生命{37/100/RED}，魔力{19/60/ROYALBLUE}",
            "<购买/@Buy> [ITEM:2147483647] 37", "" }), "颜色/按钮/实体链接与模板混排被破坏");
        foreach (var token in new[] { "<$UNKNOWN>", "<$OUTPUT()>", "<$OUTPUT(A10,A11)>", "<$OUTPUT(HP)>", "<$OUTPUT(A10junk)>",
            "<$OUTPUT((A10))>", "<$HP(1)>", "<$CONQUESTGUARD(7)>", "<$CONQUESTOWNER(7,2)>", "<$CONQUESTOWNER(bad)>", "<$CONQUESTOWNER(7%)>",
            "<$MONSTERCOUNT()>", "<$MONSTERCOUNT( )>", "<$MONSTERCOUNT(Template Map,1)>", "<$HP", "<$OUTPUT(A999)>" })
        {
            var expected = token == "<$OUTPUT(A999)>" ? "%A999" : token;
            check(segment.ReplaceValue(player, "前" + token + "后<$HP>") == "前" + expected + "后37", "畸形或未知模板误替换或阻断后续模板：" + token);
        }
        check(segment.ReplaceValue(player, "") == "" && segment.ReplaceValue(player, null!) == null, "空文本模板异常");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR"); segment.AddVariable(player, "i10", "11");
            check(segment.ReplaceValue(player, "<$credit>") == "56" && segment.ReplaceValue(player, "<$OUTPUT(i10)>") == "11",
                "模板或变量受到土耳其语大小写规则影响");
        }
        finally { CultureInfo.CurrentCulture = culture; }
        player.NPCData["NPCRollResult"] = 6;
        check(segment.ReplaceValue(player, "<$ROLLRESULT>") == "6", "掷点结果未读取原NPC会话");
    }

    private static void CheckEquipment(NPCSegment segment, PlayerObject player, Action<bool, string> check)
    {
        var slots = new (string Name, EquipmentSlot Slot, ServerTextKeys Empty)[]
        {
            ("ARMOUR", EquipmentSlot.Armour, ServerTextKeys.NoArmour), ("WEAPON", EquipmentSlot.Weapon, ServerTextKeys.NoWeapon),
            ("USERWEAPON", EquipmentSlot.Weapon, ServerTextKeys.NoWeapon), ("RING_L", EquipmentSlot.RingL, ServerTextKeys.NoRing),
            ("RING_R", EquipmentSlot.RingR, ServerTextKeys.NoRing), ("BRACELET_L", EquipmentSlot.BraceletL, ServerTextKeys.NoBracelet),
            ("BRACELET_R", EquipmentSlot.BraceletR, ServerTextKeys.NoBracelet), ("NECKLACE", EquipmentSlot.Necklace, ServerTextKeys.NoNecklace),
            ("BELT", EquipmentSlot.Belt, ServerTextKeys.NoBelt), ("BOOTS", EquipmentSlot.Boots, ServerTextKeys.NoBoots),
            ("HELMET", EquipmentSlot.Helmet, ServerTextKeys.NoHelmet), ("AMULET", EquipmentSlot.Amulet, ServerTextKeys.NoAmulet),
            ("STONE", EquipmentSlot.Stone, ServerTextKeys.NoStone), ("TORCH", EquipmentSlot.Torch, ServerTextKeys.NoTorch)
        };
        foreach (var (name, _, empty) in slots)
            check(segment.ReplaceValue(player, "<$" + name + ">") == Text(empty), "未穿戴装备模板错误：" + name);
        check(segment.ReplaceValue(player, "<$UPGRADEWEAPONFEE>") == "0", "未装备武器的费用错误");
        foreach (var (_, slot, _) in slots)
            player.Info.Equipment[(int)slot] = new UserItem(new ItemInfo { Index = 900 + (int)slot, Name = "装备" + slot, RequiredAmount = 22 });
        foreach (var (name, slot, _) in slots)
            check(segment.ReplaceValue(player, "<$" + name + ">") == player.Info.Equipment[(int)slot].FriendlyName, "已穿戴装备模板错误：" + name);
        check(segment.ReplaceValue(player, "左：<$RING_L>右：<$RING_R>") == "左：装备RingL右：装备RingR", "左右装备紧邻模板错误");
        check(segment.ReplaceValue(player, "<$UPGRADEWEAPONFEE>") == (22 * 10 * Settings.RefineCost).ToString(), "强化费用未与原精炼规则一致");
        player.Info.Equipment[(int)EquipmentSlot.Mount] = new UserItem(new ItemInfo { Name = "测试坐骑" }) { CurrentDura = 123, MaxDura = 456 };
        check(segment.ReplaceValue(player, "<$MOUNT>/<$MOUNTLOYALTY>") == "测试坐骑/123 (456)", "已装备坐骑或忠诚度错误");
    }

    private static void CheckConquests(NPCSegment segment, PlayerObject player, Action<bool, string> check)
    {
        var world = Envir.Main;
        var info = (GuildInfo)RuntimeHelpers.GetUninitializedObject(typeof(GuildInfo));
        info.Name = "模板行会"; info.GuildIndex = 901; info.GTRent = world.Now.AddDays(10);
        var guild = new GuildObject(info);
        player.MyGuild = guild;
        check(segment.ReplaceValue(player, "前<$GUILDNAME>/<$AGITGUILDNAME>后") == "前模板行会 " + Text(ServerTextKeys.Guild) + "/模板行会后", "行会模板丢失上下文");
        check(segment.ReplaceValue(player, "<$GUILDEXTENDFEE>") == GameLanguage.ServerTextMap.GetLocalization(ServerTextKeys.ExpireOnExtendFee, guild.GTRent.ToString(), Settings.ExtendGT.ToString()), "领地续租费模板错误");
        check(int.Parse(segment.ReplaceValue(player, "<$GUILDGTRENTALDAYSLEFT>")) is 9 or 10, "领地租期未读取原行会");
        var conquest = new ConquestObject(new ConquestGuildInfo { Info = new ConquestInfo { Index = 7, StartHour = 20, Monday = true, Tuesday = true,
            Wednesday = true, Thursday = true, Friday = true, Saturday = true, Sunday = true }, GoldStorage = 500, NPCRate = 15, AttackerID = -1 });
        world.Conquests.Add(conquest);
        check(segment.ReplaceValue(player, "前<$CONQUESTOWNER(7)>后") == "前" + Text(ServerTextKeys.NoOwner) + "后", "无城主模板丢失上下文");
        conquest.Guild = guild;
        foreach (var (key, value) in new[] { ("CONQUESTOWNER", "模板行会"), ("CONQUESTGOLD", "500"), ("CONQUESTRATE", "15%"), ("CONQUESTSCHEDULE", Text(ServerTextKeys.NoWarScheduled)) })
        {
            check(segment.ReplaceValue(player, "前<$" + key + "(7)>后") == "前" + value + "后", "攻城单参数模板错误：" + key);
            check(segment.ReplaceValue(player, "<$" + key + "( a10 )>") == value, "攻城变量/空格参数错误：" + key);
        }
        foreach (var (key, value) in new[] { ("CONQUESTOWNER", ""), ("CONQUESTGOLD", ""), ("CONQUESTRATE", ""), ("CONQUESTSCHEDULE", Text(ServerTextKeys.ConquestNotFound)) })
            check(segment.ReplaceValue(player, "前<$" + key + "(999)>后") == "前" + value + "后", "不存在城池的空值/提示错误：" + key);
        conquest.ArcherList.Add(new ConquestGuildArcherInfo { Index = 2, Info = new ConquestArcherInfo { Name = "弓箭守卫", RepairCost = 0 } });
        conquest.GateList.Add(new ConquestGuildGateInfo { Index = 2, Info = new ConquestGateInfo { Name = "城门" } });
        conquest.WallList.Add(new ConquestGuildWallInfo { Index = 2, Info = new ConquestWallInfo { Name = "城墙" } });
        conquest.SiegeList.Add(new ConquestGuildSiegeInfo { Index = 2, Info = new ConquestSiegeInfo { Name = "攻城车" } });
        foreach (var (key, name, status) in new[] { ("CONQUESTGUARD", "弓箭守卫", ServerTextKeys.StillAlive), ("CONQUESTGATE", "城门", ServerTextKeys.NoRepairRequired),
            ("CONQUESTWALL", "城墙", ServerTextKeys.NoRepairRequired), ("CONQUESTSIEGE", "攻城车", ServerTextKeys.StillAlive) })
        {
            check(segment.ReplaceValue(player, "前<$" + key + "(7,2)>后") == "前" + name + Text(status) + "后", "攻城双参数模板错误：" + key);
            check(segment.ReplaceValue(player, "<$" + key + "(%a10, a11)>") == name + Text(status), "攻城双变量模板错误：" + key);
            check(segment.ReplaceValue(player, "前<$" + key + "(7,999)>后") == "前" + Text(ServerTextKeys.NotFound) + "后", "缺少守备对象破坏上下文：" + key);
            check(segment.ReplaceValue(player, "<$" + key + "(bad,2)>") == "<$" + key + "(bad,2)>", "错误攻城参数不应产生伪造值：" + key);
        }
        conquest.GuildInfo.AttackerID = guild.Guildindex;
        check(segment.ReplaceValue(player, "<$CONQUESTSCHEDULE(7)>") == guild.Name, "实际攻城申请行会未显示");
        var date = world.Now.Date.AddHours(20); if (date < world.Now) date = date.AddDays(1);
        check(segment.ReplaceValue(player, "<$CASTLEWARDATE>") == date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), "攻城日期未从实际星期/开始时间/申请读取");
        conquest.Info.Monday = conquest.Info.Tuesday = conquest.Info.Wednesday = conquest.Info.Thursday = conquest.Info.Friday = conquest.Info.Saturday = conquest.Info.Sunday = false;
        check(segment.ReplaceValue(player, "<$CASTLEWARDATE>") == Text(ServerTextKeys.NoWarScheduled), "未设置日期时伪造攻城排期");
        conquest.ConquestMap = player.CurrentMap; conquest.StartType = ConquestType.Forced; conquest.WarIsOn = true;
        check(segment.ReplaceValue(player, "<$CASTLEWARDATE>") == conquest.WarStartTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), "强制开战未读取原引擎已开始的实际时间");
        conquest.WarIsOn = false;
    }

    private static void CheckMonster(NPCSegment segment, Map map, Action<bool, string> check)
    {
        var monster = new TemplateMonster(new MonsterInfo { Name = "模板怪物", Level = 8 }) { HP = 17, Stats = new Stats { [Stat.HP] = 70 }, CurrentMap = map, CurrentLocation = new(4, 5) };
        segment.AddVariable(monster, "A10", "42"); segment.AddVariable(monster, "A11", "");
        var values = new Dictionary<string, string> { ["USERNAME"] = "模板怪物", ["LEVEL"] = "8", ["MAP"] = "Template Map", ["X_COORD"] = "4",
            ["Y_COORD"] = "5", ["HP"] = "17", ["MAXHP"] = "70", ["DATE"] = Envir.Main.Now.ToShortDateString(), ["MAPLIGHT"] = "Night",
            ["USERCOUNT"] = Envir.Main.PlayerCount.ToString(), ["GUILDWARTIME"] = Settings.Guild_WarTime.ToString(), ["GUILDWARFEE"] = Settings.Guild_WarCost.ToString(), ["OUTPUT(A10)"] = "42" };
        foreach (var (key, value) in values)
            check(segment.ReplaceValue(monster, "前<$" + key.ToLowerInvariant() + ">后<$HP>") == "前" + value + "后17", "怪物模板错误：" + key);
        check(segment.ReplaceValue(monster, "<$OUTPUT(A11)><$OUTPUT(%A10)>") == "42", "怪物空值/带百分号变量错误");
        check(segment.ReplaceValue(monster, "<$OUTPUT()><$UNKNOWN><$HP>") == "<$OUTPUT()><$UNKNOWN>17", "怪物错误模板阻断正常值");
    }

    private static void CheckCallers(PlayerObject player, Map map, Action<bool, string> check)
    {
        var page = new NPCPage("[@MAIN]");
        var segment = new NPCSegment(page, ["生命<$HP>/<$MAXHP>，魔力<$MP>/<$MAXMP>"], [], [], [], []);
        segment.ParseCheck("CHECKCALC <$HP><$MP> == 3719"); segment.ParseAct(segment.ActList, "MOV A20 <$HP><$MP>");
        check(segment.Check(player) && segment.FindVariable(player, "%A20") == "3719" && player.NPCSpeech.Last() == "生命37/100，魔力19/60",
            "玩家Check/Act/Say没有共同使用完整文本模板解析");
        var monster = new TemplateMonster(new MonsterInfo { Name = "检查怪物" }) { HP = 17, CurrentMap = map };
        segment = new NPCSegment(page, [], [], [], [], []);
        segment.ParseCheck("CHECKCALC <$HP><$HP> == 1717"); segment.ParseAct(segment.ActList, "MOV A20 <$HP><$HP>");
        check(segment.Check(monster) && segment.FindVariable(monster, "%A20") == "1717", "怪物Check/Act没有使用完整文本模板解析");
    }

    private static string Text(ServerTextKeys key) => GameLanguage.ServerTextMap.GetLocalization(key);
}
