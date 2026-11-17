using System.Drawing;
using Mir2.Companions;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

internal static class CompanionGuildSiegeChecks
{
    public static void Run(Action<bool,string> check)
    {
        var world = Envir.Main;
        var oldMaps = world.MapInfoList; var oldLoaded = world.MapList;
        var oldGuilds = world.Guilds; var oldWars = world.Conquests;
        try
        {
            var field = Map(99100); var palace = Map(99101);
            field.Info.Movements.Add(new MovementInfo { Source = new(11,10), MapIndex = palace.Info.Index, Destination = new(10,10) });
            world.MapInfoList = [field.Info,palace.Info]; world.MapList = [field,palace]; world.Guilds = []; world.Conquests = [];
            var player = Player(99100,field); var enemy = Player(99101,field); var other = Player(99102,field);
            var own = Guild(player,99100); var hostile = Guild(enemy,99101); var third = Guild(other,99102);
            var info = new ConquestInfo { Index = 99100, Name = "SiegeCheck", MapIndex = field.Info.Index, PalaceIndex = palace.Info.Index,
                Location = new(10,10), Size = 2, KingLocation = new(10,10), KingSize = 1, Game = ConquestGame.Classic };
            var war = new ConquestObject(new ConquestGuildInfo { Info = info, Owner = hostile.Guildindex, AttackerID = own.Guildindex })
                { ConquestMap = field, StartType = ConquestType.Forced };
            field.Conquest.Add(war); world.Conquests.Add(war); war.WarIsOn = true;
            KnownGuildPlan? Plan(ISet<string>? blocked = null) => CompanionGuildSieges.Plan(player,blocked);
            check(Plan()?.Goal?.MapId == palace.Info.Index, "经典攻城没有前往原引擎实际判定胜负的王宫");
            war.GameType = ConquestGame.CapturePalace;
            check(Plan()?.Goal?.MapId == palace.Info.Index, "王宫占领模式未选择王宫");
            war.GameType = ConquestGame.ControlPoints;
            check(Plan()?.Goal?.MapId == field.Info.Index, "控制点模式错误进入王宫");
            war.StartType = ConquestType.Request; war.GuildInfo.AttackerID = third.Guildindex;
            check(Plan() == null, "申请制攻城允许无资格的第三方参战");
            war.GuildInfo.AttackerID = own.Guildindex;
            check(Plan() != null, "申请制攻城阻断了指定攻方");
            own.Conquest = new ConquestObject(new ConquestGuildInfo());
            check(Plan() == null, "已有其他城池的行会仍发起攻城");
            war.GuildInfo.Owner = own.Guildindex;
            check(Plan() != null, "原守方有城池时不能守城");
            own.Conquest = null; war.GuildInfo.Owner = hostile.Guildindex;
            var record = new CompanionRecord { Profile = new CompanionProfile { Guilds = new() { Mode = "join", Siege = true } } };
            check(CompanionGuildDevelopment.Read(player,record,[],null,false)?.Action != "siege", "PK总开关关闭仍规划攻城");
            record.Profile.Guilds.Siege = false;
            check(CompanionGuildDevelopment.Read(player,record,[],null,true)?.Action != "siege", "角色未同意参战仍规划攻城");
            record.Profile.Guilds.Mode = "off";
            check(CompanionGuildDevelopment.Read(player,record,[],null,true) == null, "行会策略关闭仍规划活动");
            check(Plan(new HashSet<string> { "guild/siege/99100" }) == null, "攻城忽略暂不可达排除");
            field.Info.NoFight = true;
            check(Plan() == null, "攻城目标绕过地图禁战"); field.Info.NoFight = false;
            player.AMode = AttackMode.Guild; player.Node = new LinkedListNode<MapObject>(player);
            player.CurrentLocation = new(10,10); enemy.CurrentLocation = new(11,10); other.CurrentLocation = new(10,11);
            field.Players.AddRange([player,enemy,other]);
            check(Plan()!.Targets!.SequenceEqual([enemy.ObjectID]), "申请制错误攻击非指定守方");
            enemy.CurrentLocation = new(13,10);
            check(!Plan()!.Targets!.Contains(enemy.ObjectID), "攻城仍追打已经离开当前战区的玩家");
            enemy.CurrentLocation = new(11,10); own.AllyGuilds.Add(hostile);
            check(!Plan()!.Targets!.Contains(enemy.ObjectID), "攻城误伤友会成员"); own.AllyGuilds.Clear();
            enemy.InSafeZone = true;
            check(!Plan()!.Targets!.Contains(enemy.ObjectID), "攻城绕过目标安全区"); enemy.InSafeZone = false;
            enemy.Dead = true;
            check(!Plan()!.Targets!.Contains(enemy.ObjectID), "攻城仍选择已死亡目标"); enemy.Dead = false;
            player.AMode = AttackMode.Peace;
            check(Plan()!.Targets!.Length == 0, "攻城绕过原和平攻击模式"); player.AMode = AttackMode.Guild;
            player.CurrentLocation = new(13,10);
            check(Plan()!.Targets!.Length == 0, "战区外仍向陪玩提供攻击目标");
            player.CurrentLocation = new(10,10); player.InSafeZone = true;
            check(Plan()!.Targets!.Length == 0, "安全区内仍向陪玩提供攻击目标"); player.InSafeZone = false;
            field.Players.Clear(); war.GameType = ConquestGame.KingOfHill;
            war.Info.KingSize = 0; field.Cells[10,10].Attribute = CellAttribute.HighWall;
            check(Plan() == null, "山丘占领点不可通行时把计分范围外当作驻守点");
            field.Cells[10,10].Attribute = CellAttribute.Walk; war.Info.KingSize = 1; player.CurrentLocation = new(9,10);
            var holding = Plan()!.Goal!;
            check(new Point(holding.X,holding.Y) == player.CurrentLocation, "已在有效计分范围内仍强迫所有陪玩挤向中心");
            player.CurrentLocation = new(8,10); field.Cells[10,10].Objects.Add(other);
            holding = Plan()!.Goal!;
            check(new Point(holding.X,holding.Y) != new Point(10,10), "驻守点没有避开已占用中心");
            field.Cells[10,10].Objects.Clear(); player.CurrentLocation = new(11,10);
            holding = Plan()!.Goal!;
            check(new Point(holding.X,holding.Y) != player.CurrentLocation, "驻守在公开切图出口上反复切图");
            player.CurrentLocation = new(8,10);
            for (var x = 9; x <= 11; x++) for (var y = 9; y <= 11; y++) field.Cells[x,y].Attribute = CellAttribute.HighWall;
            field.Cells[11,10].Attribute = CellAttribute.Walk;
            check(Plan() == null, "仅有切图出口可通行时仍规划驻守");
            field.Cells[9,10].Attribute = CellAttribute.Walk;
            holding = Plan()!.Goal!;
            check(new Point(holding.X,holding.Y) == new Point(9,10), "环形搜索遗漏了东西方向唯一有效驻守格");
            field.Cells[9,10].Attribute = CellAttribute.HighWall;
            check(Plan() == null, "整个计分范围不可用仍向外规划无效驻守");
            war.Info.FullMap = true; war.Info.KingLocation = new(60,0); war.Info.KingSize = 1000; player.CurrentLocation = new(0,0);
            holding = Plan()!.Goal!;
            check(new Point(holding.X,holding.Y) == player.CurrentLocation, "将32格搜索预算错误当成当前版本真实计分范围");
            war.WarIsOn = false;
            check(Plan() == null, "战争结束仍保留攻城计划");
        }
        finally { world.MapInfoList = oldMaps; world.MapList = oldLoaded; world.Guilds = oldGuilds; world.Conquests = oldWars; }
    }

    private static Map Map(int id)
    {
        var map = new Map(new MapInfo { Index = id }) { Width = 30, Height = 30, Cells = new Cell[30,30] };
        for (var x = 0; x < 30; x++) for (var y = 0; y < 30; y++) map.Cells[x,y] = new Cell { Attribute = CellAttribute.Walk };
        return map;
    }
    private static PlayerObject Player(int id, Map map) => new() { Info = new CharacterInfo { Index = id, Name = "Siege" + id }, CurrentMap = map };
    private static GuildObject Guild(PlayerObject player, int id)
    {
        var guild = new GuildObject(new GuildInfo(player,"Siege" + id) { GuildIndex = id });
        player.MyGuild = guild; player.MyGuildRank = guild.Ranks[0]; return guild;
    }
}
