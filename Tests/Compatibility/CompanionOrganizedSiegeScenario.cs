using System.Text.Json;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionOrganizedSiegeScenario
{
    public static void Run(ProjectPaths paths,int[] roles,string mode)
    {
        if (roles.Length != 4 || !paths.Data.Contains("mir2-companions-guild-",StringComparison.Ordinal)) throw new InvalidOperationException("仅限四人行会隔离专项。");
        if (mode == "prepare") CompanionMultipleGuildScenario.Prepare(paths,roles);
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if (!world.LoadDB()) throw new InvalidDataException("测试世界读取失败。");world.LoadAccounts();world.LoadGuilds();
        var players=roles.Select(id=>world.CharacterList.Single(c=>c.Index==id)).ToArray();
        var war=world.ConquestInfoList.Single();
        if (mode is "scheduled-proof" or "battle-proof")
        {
            using var reader=new BinaryReader(DatabaseStorage.OpenRead(Path.Combine(Settings.ConquestsPath,war.Index+".mcd")));
            var saved=new ConquestGuildInfo(reader);
            if (mode=="scheduled-proof" && saved.AttackerID!=players[0].GuildIndex) throw new InvalidDataException("会长原NPC报名没有保存原攻方。");
            if (mode=="battle-proof" && saved.Owner!=players[0].GuildIndex) throw new InvalidDataException("攻方没有在消灭真实守方后取得原王宫归属。");
            if (world.GuildList.Count!=2 || players[0].GuildIndex!=players[2].GuildIndex || players[1].GuildIndex!=players[3].GuildIndex || players[0].GuildIndex==players[1].GuildIndex)
                throw new InvalidDataException("两方真实会籍没有保持。");
            if (mode=="scheduled-proof") Schedule(storage,roles);
            Console.WriteLine(JsonSerializer.Serialize(new { saved.Owner,saved.AttackerID,guilds=world.GuildList.Count,gold=players.Select(p=>p.AccountInfo.Gold) }));return;
        }
        if (mode=="prepare")
        {
            var horn=world.ItemInfoList.SingleOrDefault(PublicShopRules.IsGuildHorn);
            if (horn == null)
            { horn=new ItemInfo { Index=++world.ItemIndex,Name="沃玛角",Type=ItemType.CraftingMaterial,StackSize=999,Price=1000000 };world.ItemInfoList.Add(horn); }
            foreach (var player in players)
            {
                player.Inventory[0]=null;
                player.AccountInfo.Gold=player==players[0]||player==players[1]?1001000u:5000u;
            }
            File.WriteAllText(Path.Combine(Settings.ConfigPath,"GuildSettings.ini"),"[Guilds]\nMinimumLevel=22\n[Required-0]\nAmount=1000\n[Required-1]\nItemName="+horn.Name+"\nAmount=1\n[Exp]\nLevel-0=10000\n[Cap]\nLevel-0=4\n");
            var npc=world.NPCInfoList.Single();
            File.WriteAllText(Path.Combine(Settings.NPCPath,npc.FileName+".txt"),"[@MAIN]\n#SAY\n<创建行会/@CREATEGUILD/Gold>\n<攻城报名/@SIEGE>\n[@CREATEGUILD]\n[@SIEGE]\n");
            var monster=new MonsterInfo { Index=++world.MonsterIndex,Name="隔离建会金币来源",Image=Monster.Hen,AI=0,Level=1,ViewRange=0,
                MoveSpeed=ushort.MaxValue,AttackSpeed=2500,Stats=new Stats { [Stat.HP]=1 },DropPath="GuildFunding" };
            world.MonsterInfoList.Add(monster);
            var map=world.MapInfoList.Single(m=>m.Index==players[0].CurrentMapIndex);
            map.Respawns.Add(new RespawnInfo { MonsterIndex=monster.Index,Location=Functions.PointMove(players[0].CurrentLocation,MirDirection.Down,3),Count=10,Spread=2,Delay=1 });
            File.WriteAllText(Path.Combine(Settings.DropPath,"GuildFunding.txt"),"1/1 Gold 2000\n");
        }
        else if (mode=="battle")
        {
            if (world.GuildList.Count!=2) throw new InvalidDataException("先通过原TCP建立两个行会。");
            Schedule(storage,roles);
            foreach (var map in world.MapInfoList) map.Respawns.Clear();
            var start=players[0].BindLocation;
            foreach (var (player,index) in players.Select((p,i)=>(p,i)))
            {
                player.CurrentMapIndex=index%2==0?war.MapIndex:war.PalaceIndex;
                player.CurrentLocation=Functions.PointMove(start,index%2==0?MirDirection.Left:MirDirection.Right,index<2?2:3);
                var blade=new ItemInfo { Index=++world.ItemIndex,Name="隔离攻防兵器"+index,Type=ItemType.Weapon,Durability=5000,
                    Stats=new Stats { [Stat.MinDC]=index%2==0?150:5,[Stat.MaxDC]=index%2==0?150:5 } };
                world.ItemInfoList.Add(blade);player.Equipment[0]=world.CreateFreshItem(blade);
            }
            war.Game=ConquestGame.Classic;
            using var buffer=new MemoryStream();using (var writer=new BinaryWriter(buffer,System.Text.Encoding.UTF8,true))
                new ConquestGuildInfo { Owner=players[1].GuildIndex,AttackerID=-1 }.Save(writer);
            storage.Write(Path.Combine(Settings.ConquestsPath,war.Index+".mcd"),buffer.ToArray());
        }
        else throw new InvalidOperationException("未知攻城专项阶段。");
        war.Type=ConquestType.Request;war.StartHour=0;war.WarLength=1440;
        war.Monday=war.Tuesday=war.Wednesday=war.Thursday=war.Friday=war.Saturday=war.Sunday=true;
        world.SaveDB();world.SaveAccounts();
        Console.WriteLine("隔离攻城阶段："+mode+"；不使用STARTCONQUEST，开战只走原申请与原战期。");
    }
    private static void Schedule(SqliteDatabaseStorage storage,int[] roles)
    {
        foreach (var key in storage.List("companions/profiles","*.json"))
        {
            using var stream=storage.Read(key);
            var saved=JsonSerializer.Deserialize<CompanionEnvelope<CompanionRecord>>(stream)!;
            if (!roles.Contains(saved.Value.CharacterId)) continue;
            var record=saved.Value with { Profile=saved.Value.Profile with { Scheduled=true },Desired="off",SuppressedUntil=null,RestUntil=null,SessionUntil=null,SessionStarted=null };
            storage.Write(key,JsonSerializer.SerializeToUtf8Bytes(saved with { Value=record }));
        }
    }
}
