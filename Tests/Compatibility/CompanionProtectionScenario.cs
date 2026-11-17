using System.Drawing;
using System.Text.Json;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionProtectionScenario
{
    public static void Prepare(ProjectPaths paths,int id,string mode)
    {
        if(!paths.Data.Contains("mir2-companions-",StringComparison.Ordinal)||mode is not ("town" or "denied" or "revive" or "crowd" or "budget"))
            throw new InvalidOperationException("只允许已知场景及隔离世界。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("隔离世界读取失败");
        world.BuffInfoList.AddRange(BuffInfo.Load());world.LoadAccounts();var c=world.CharacterList.Single(c=>c.Index==id);
        var town=world.MapInfoList[0];var field=world.MapInfoList[1];
        foreach(var map in world.MapInfoList)
        {
            map.Respawns.Clear();map.Movements.Clear();map.SafeZones.Clear();map.NPCs.Clear();
            map.FileName="NativeProtection";map.NoDrug=false;map.NoTownTeleport=mode=="denied";map.NoDropPlayer=true;map.NoFight=false;
        }
        world.NPCInfoList.Clear();world.QuestInfoList.Clear();
        town.SafeZones.Add(new SafeZoneInfo{Info=town,Location=new(10,10),Size=2,StartPoint=true});
        c.CurrentMapIndex=field.Index;c.CurrentLocation=new(20,20);c.BindMapIndex=town.Index;c.BindLocation=new(10,10);
        c.Level=18;c.HP=mode=="revive"?0:mode=="crowd"?68:20;c.MP=200;c.Experience=0;c.AccountInfo.Gold=1234;
        c.Inventory=new UserItem[46];c.Equipment=new UserItem[14];c.QuestInventory=new UserItem[40];c.Magics.Clear();c.CurrentQuests.Clear();c.CompletedQuests.Clear();
        var scroll=new ItemInfo{Index=++world.ItemIndex,Name="隔离回城卷",Type=ItemType.Scroll,Shape=1,StackSize=20};
        world.ItemInfoList.Add(scroll);c.Inventory[0]=world.CreateFreshItem(scroll);c.Inventory[0].Count=2;
        if(mode=="budget")c.Inventory[0]=null;
        if(mode is "crowd" or "budget")
        {
            var red=new ItemInfo{Index=++world.ItemIndex,Name="隔离普通红药",Type=ItemType.Potion,Shape=0,StackSize=100,Stats=new Stats{[Stat.HP]=30}};
            world.ItemInfoList.Add(red);
            if(mode=="crowd"){c.Inventory[1]=world.CreateFreshItem(red);c.Inventory[1].Count=20;}
            var mob=new MonsterInfo{Index=++world.MonsterIndex,Name="隔离围攻僵尸",Image=Monster.ClZombie,AI=0,Level=18,Experience=100,
                Stats=new Stats{[Stat.HP]=1000,[Stat.MinDC]=mode=="budget"?1000:8,[Stat.MaxDC]=mode=="budget"?1000:8,[Stat.Accuracy]=100},ViewRange=16,MoveSpeed=500,AttackSpeed=500};
            world.MonsterInfoList.Add(mob);
            foreach(var p in new[]{new Point(18,20),new Point(22,20),new Point(20,18),new Point(20,22)})
                field.Respawns.Add(new RespawnInfo{RespawnIndex=++world.RespawnIndex,MonsterIndex=mob.Index,Location=p,Count=1,Delay=600});
        }
        var header=new byte[52];BitConverter.GetBytes((short)32).CopyTo(header,0);BitConverter.GetBytes((short)32).CopyTo(header,2);
        using(var writer=new BinaryWriter(File.Create(Path.Combine(paths.Assets,"maps","NativeProtection.map"))))
        {
            writer.Write(header);
            for(var x=0;x<32;x++)for(var y=0;y<32;y++)
            {writer.Write((ushort)(x==0||y==0||x==31||y==31||x==10&&y==10?0x8000:0));writer.Write(new byte[10]);}
        }
        world.SaveDB();world.SaveAccounts();
        File.WriteAllText(Path.Combine(paths.Runtime,"protection-fixture.json"),JsonSerializer.Serialize(new{mode,id,town=town.Index,field=field.Index,scroll=scroll.Index,uniqueId=c.Inventory[0]?.UniqueID??0}));
        Console.WriteLine($"隔离保护场景 {mode}：阻挡安全区中心，原药卷/复活权限及4怪真实攻击。");
    }
}
