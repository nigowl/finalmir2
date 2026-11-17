using System.Drawing;
using System.Text.Json;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionMovementScenario
{
    public static void Prepare(ProjectPaths paths,int characterId,bool heavy,bool classic=false)
    {
        if(!paths.Data.Contains("mir2-companions-",StringComparison.Ordinal))throw new InvalidOperationException("只允许修改本测试创建的隔离世界。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("测试世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load());world.LoadAccounts();
        var character=world.CharacterList.Single(role=>role.Index==characterId);
        var origin=new Point(5,5);var end=new Point(10,13);
        WriteMap(Path.Combine(paths.Assets,"maps","NativeCompanionRun.map"),classic);
        foreach(var map in world.MapInfoList)
        {
            map.FileName="NativeCompanionRun";map.Respawns.Clear();map.Movements.Clear();map.SafeZones.Clear();
            map.NoFight=true;map.NoDropPlayer=true;
        }
        var first=world.MapInfoList[0];var second=world.MapInfoList[1];
        first.SafeZones.Add(new SafeZoneInfo{Info=first,Location=origin,Size=0,StartPoint=true});
        first.Movements.Add(new MovementInfo{MapIndex=second.Index,Source=end,Destination=origin,ShowOnBigMap=false});
        character.CurrentMapIndex=character.BindMapIndex=first.Index;character.CurrentLocation=character.BindLocation=origin;
        character.Level=30;character.HP=character.MP=1000;character.Experience=0;character.AccountInfo.Gold=0;
        character.Inventory=new UserItem[46];character.Equipment=new UserItem[14];character.QuestInventory=new UserItem[40];character.Magics.Clear();
        character.CurrentQuests.Clear();character.CompletedQuests.Clear();
        if(heavy)
        {
            var ore=new ItemInfo{Index=++world.ItemIndex,Name="NativeRunHeavyOre",Type=ItemType.Ore,Weight=255,StackSize=20};
            world.ItemInfoList.Add(ore);character.Inventory[0]=world.CreateFreshItem(ore);character.Inventory[0].Count=10;
        }
        world.QuestInfoList=[new QuestInfo{Index=1,Name="步跑跨图拜访",FileName="NativeRunVisit",RequiredMinLevel=1,RequiredMaxLevel=255,RequiredClass=RequiredClass.None}];
        world.QuestIndex=1;
        File.WriteAllText(Path.Combine(Settings.QuestPath,"NativeRunVisit.txt"),"[@DESCRIPTION]\n通过弯路和两道门前往另一地图拜访。\n[@GOLDREWARD]\n17\n");
        world.NPCInfoList=[new NPCInfo{Index=1,Name="Native Run Clerk",FileName="NativeRunClerk",MapIndex=second.Index,Location=end}];world.NPCIndex=1;
        File.WriteAllText(Path.Combine(Settings.NPCPath,"NativeRunClerk.txt"),"[@MAIN]\n#SAY\n步跑跨图回归。\n[Quests]\n1\n");
        File.WriteAllText(Path.Combine(paths.Runtime,"companion-running-fixture.json"),JsonSerializer.Serialize(new{origin=new{origin.X,origin.Y},end=new{end.X,end.Y},first=first.Index,second=second.Index,heavy,classic}));
        world.SaveDB();world.SaveAccounts();
        Console.WriteLine($"已设置两图折弯窄路/相邻两道原门/远处拜访，超重={heavy}，格式={(classic?0:1)}。");
    }

    public static void WriteMap(string path,bool classic=false)
    {
        // 对比原v0与2010格式；设置不同门号和独立动画字节，防止把动画误当作门。
        var header=new byte[classic?52:54];
        if(!classic){header[0]=0x10;System.Text.Encoding.ASCII.GetBytes("Map 2010 Ver 1.0").CopyTo(header,1);}
        BitConverter.GetBytes((short)48).CopyTo(header,classic?0:21);BitConverter.GetBytes((short)32).CopyTo(header,classic?2:25);
        using var writer=new BinaryWriter(File.Create(path));writer.Write(header);
        for(var x=0;x<48;x++)for(var y=0;y<32;y++)
        {
            var clear=y==5&&x>=5&&x<=35||x==35&&y>=5&&y<=13||y==13&&x>=10&&x<=35;
            if(classic)writer.Write((ushort)(clear?0:0x8000));
            else writer.Write(unchecked((int)((clear?0u:0x20000000u)^0xAA38AA38u)));
            writer.Write((ushort)0);writer.Write((ushort)0);
            writer.Write((byte)(y==5&&x is 8 or 9?0x80+x-7:0));writer.Write((byte)0);
            writer.Write((byte)(x==15&&y==5?7:0));writer.Write((byte)0);writer.Write((byte)0);
            writer.Write((byte)(x==20&&y==5?103:0));if(!classic)writer.Write((byte)0);
        }
    }
}
