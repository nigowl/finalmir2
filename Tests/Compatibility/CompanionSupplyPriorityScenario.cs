using System.Text.Json;
using Mir2.Companions;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionSupplyPriorityScenario
{
    public static void Prepare(ProjectPaths paths,int characterId,uint? emergencyGold=null,bool vendorRoutes=false,bool threatRoute=false,bool crowdedRoute=false,bool handoffThreat=false)
    {
        if(!paths.Data.Contains("mir2-companions-",StringComparison.Ordinal))throw new InvalidOperationException("只允许修改本测试创建的隔离世界。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("测试世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load());world.LoadAccounts();
        // 本专项验证NPC原价和跨图货架，不能被后来加入的公共商城补给抢占。
        foreach(var item in world.ItemInfoList.Where(item=>PublicShopRules.Category(item) is 0 or 1))item.Price=0;
        var character=world.CharacterList.Single(role=>role.Index==characterId);
        if(character.Class!=MirClass.Taoist)throw new InvalidDataException("补给顺序场景必须使用道士。");
        character.Level=10;character.HP=character.MP=1000;character.AccountInfo.Gold=emergencyGold??1000;
        character.CurrentMapIndex=character.BindMapIndex;character.CurrentLocation=character.BindLocation;
        character.Inventory=new UserItem[Globals.BaseInventorySize];character.Equipment=new UserItem[14];character.CurrentQuests.Clear();character.CompletedQuests.Clear();
        foreach(var map in world.MapInfoList)map.Respawns.Clear();
        world.QuestInfoList.Clear();world.NPCInfoList.Clear();
        ItemInfo Potion(string name,Stat stat,uint price)
        {
            var info=new ItemInfo{Index=++world.ItemIndex,Name=name,Type=ItemType.Potion,Shape=1,Price=price,StackSize=1,
                RequiredClass=RequiredClass.None,RequiredGender=RequiredGender.None,Stats=new Stats{[stat]=30}};
            world.ItemInfoList.Add(info);return info;
        }
        var red=Potion("NativePriorityRed",Stat.HP,emergencyGold.HasValue?40u:20u);var blue=Potion("NativePriorityBlue",Stat.MP,30);
        if(emergencyGold.HasValue)red.StackSize=20;
        var hp=emergencyGold.HasValue?0:CompanionSupplyRules.Target(Stat.HP,character.Class,50)-1;
        var mp=emergencyGold.HasValue?0:(CompanionSupplyRules.Target(Stat.MP,character.Class,50)+1)/2-1;
        foreach(var info in Enumerable.Repeat(red,hp).Concat(Enumerable.Repeat(blue,mp)))
            character.Inventory[Array.IndexOf(character.Inventory,null)]=world.CreateFreshItem(info);
        var filler=new ItemInfo{Index=++world.ItemIndex,Name="NativePriorityProtectedOre",Type=ItemType.Ore,StackSize=1};world.ItemInfoList.Add(filler);
        while(character.Inventory.Count(item=>item==null)>(emergencyGold.HasValue?3:1))character.Inventory[Array.IndexOf(character.Inventory,null)]=world.CreateFreshItem(filler);
        if(vendorRoutes)
        {
            var origin=world.GetMapInfo(character.BindMapIndex);var middle=new MapInfo{Index=903,FileName="0",Title="远店岔路",NoDropPlayer=true};
            var far=new MapInfo{Index=904,FileName="0",Title="远处药店",NoDropPlayer=true};
            origin.Movements.Add(new MovementInfo{MapIndex=middle.Index,Source=Functions.PointMove(character.BindLocation,MirDirection.Down,4),Destination=character.BindLocation});
            middle.Movements.Add(new MovementInfo{MapIndex=far.Index,Source=Functions.PointMove(character.BindLocation,MirDirection.Up,4),Destination=character.BindLocation});
            middle.Movements.Add(new MovementInfo{MapIndex=origin.Index,Source=Functions.PointMove(character.BindLocation,MirDirection.Left,4),Destination=character.BindLocation});
            far.Movements.Add(new MovementInfo{MapIndex=middle.Index,Source=Functions.PointMove(character.BindLocation,MirDirection.Right,4),Destination=character.BindLocation});
            // 原NPC按地图加载顺序进入世界列表，必须让远店实际先加载才能复现旧排序。
            world.MapInfoList.InsertRange(0,[middle,far]);world.MapIndex=Math.Max(world.MapIndex,far.Index);
            world.NPCInfoList.Add(new NPCInfo{Index=++world.NPCIndex,Name="Native Far Shop",FileName="NativePriorityShop",MapIndex=far.Index,
                Location=Functions.PointMove(character.BindLocation,MirDirection.Left,2)});
        }
        if(threatRoute)PrepareThreat(paths,character,crowdedRoute,handoffThreat);
        var npc=new NPCInfo{Index=++world.NPCIndex,Name="Native Priority Shop",FileName="NativePriorityShop",MapIndex=!threatRoute&&(emergencyGold.HasValue||vendorRoutes)?2:character.BindMapIndex,
            Location=Functions.PointMove(character.BindLocation,MirDirection.Left,2)};world.NPCInfoList.Add(npc);
        if(threatRoute)npc.Location=new(52,32);
        File.WriteAllText(Path.Combine(Settings.NPCPath,npc.FileName+".txt"),"[@MAIN]\n#SAY\n优先补足短缺药品。\n<购买/@BUY>\n[@BUY]\n#SAY\n原服采购。\n[Types]\n"+
            (int)ItemType.Potion+"\n[Trade]\nNativePriorityRed "+(emergencyGold.HasValue?20:1)+"\nNativePriorityBlue 1\n");
        if(handoffThreat)
        {
            // 真实超重使角色只能步行，避免跑步恰好穿过守怪攻击间隔而漏测近身路线。
            var heavy=new ItemInfo{Index=++world.ItemIndex,Name="NativeProtectedHeavyOre",Type=ItemType.Ore,Weight=100,StackSize=1000};world.ItemInfoList.Add(heavy);
            var load=world.CreateFreshItem(heavy);load.Count=1000;character.Inventory[0]=load;
            hp=CompanionSupplyRules.Target(Stat.HP,character.Class,50);mp=CompanionSupplyRules.Target(Stat.MP,character.Class,50);
            blue.StackSize=20;
            foreach(var (info,count) in new[]{(red,hp),(blue,mp)})
            {
                var item=world.CreateFreshItem(info);item.Count=(ushort)count;character.Inventory[Array.IndexOf(character.Inventory,null)]=item;
            }
            world.QuestInfoList.Add(new QuestInfo{Index=1,Name="避险拜访交付",FileName="NativeSupplyHandoff",RequiredMinLevel=1,RequiredMaxLevel=255,RequiredClass=RequiredClass.None});world.QuestIndex=1;
            File.WriteAllText(Path.Combine(Settings.QuestPath,"NativeSupplyHandoff.txt"),"[@DESCRIPTION]\n绕开不可应付的守怪，到另一端拜访并交付。\n[@GOLDREWARD]\n17\n");
            File.AppendAllText(Path.Combine(Settings.NPCPath,npc.FileName+".txt"),"\n[Quests]\n-1\n");
            world.NPCInfoList.Add(new NPCInfo{Index=++world.NPCIndex,Name="Native Handoff Start",FileName="NativeHandoffStart",MapIndex=character.BindMapIndex,Location=new(10,30)});
            File.WriteAllText(Path.Combine(Settings.NPCPath,"NativeHandoffStart.txt"),"[@MAIN]\n#SAY\n沿通道拜访另一端。\n[Quests]\n1\n");
        }
        File.WriteAllText(Path.Combine(paths.Runtime,"companion-supply-priority-fixture.json"),JsonSerializer.Serialize(new{
            red=red.Index,blue=blue.Index,hp,mp,price=emergencyGold.HasValue?40:30,gold=character.AccountInfo.Gold,vendorRoutes,nearMap=2,farMaps=new[]{903,904},items=character.Inventory.Where(item=>item!=null).Select(item=>item.UniqueID)}));
        world.SaveDB();world.SaveAccounts();
        Console.WriteLine(handoffThreat?"已设置满量药品/真实超重、5格宽通道和10000HP守怪，验证步行接交避险。":threatRoute?"已设置120金币/无红药、5格宽补给通道和10000HP守怪，验证原路线及真实采购。":emergencyGold.HasValue?$"已设置{emergencyGold}金币、无药、3空格；跨图商店红药40金币/堆叠20，验证最低保命采购。":
            $"已设置最后1空格、红药{hp}份高于储备线、蓝药{mp}份低于储备线；红20金币、蓝30金币。");
    }

    private static void PrepareThreat(ProjectPaths paths,CharacterInfo character,bool crowded,bool handoff)
    {
        var world=Envir.Main;var map=world.GetMapInfo(character.BindMapIndex);
        map.FileName="NativeSupplyThreat";map.Movements.Clear();map.SafeZones.Clear();map.NoTownTeleport=true;
        map.SafeZones.Add(new(){Info=map,Location=new(5,5),Size=0,StartPoint=true});
        character.CurrentLocation=character.BindLocation=new(10,32);character.Magics.Clear();
        var monster=new MonsterInfo{Index=++world.MonsterIndex,Name="隔离补给路守怪",AI=0,Image=Monster.ClZombie,Level=30,
            Stats=new Stats{[Stat.HP]=10000,[Stat.MinDC]=crowded?1:8,[Stat.MaxDC]=crowded?1:8,[Stat.Accuracy]=100},ViewRange=1,MoveSpeed=crowded?ushort.MaxValue:(ushort)1000,AttackSpeed=500};
        world.MonsterInfoList.Add(monster);
        foreach(var point in crowded?new System.Drawing.Point[]{new(10,31),new(11,32),new(10,33)}:[new(handoff?42:22,31)])
            map.Respawns.Add(new(){RespawnIndex=++world.RespawnIndex,MonsterIndex=monster.Index,Location=point,Count=1,Delay=600});
        var header=new byte[52];BitConverter.GetBytes((short)64).CopyTo(header,0);BitConverter.GetBytes((short)64).CopyTo(header,2);
        using var writer=new BinaryWriter(File.Create(Path.Combine(paths.Assets,"maps","NativeSupplyThreat.map")));writer.Write(header);
        for(var x=0;x<64;x++)for(var y=0;y<64;y++){writer.Write((ushort)(x==5&&y==5||x>=2&&x<=61&&y>=30&&y<=34?0:0x8000));writer.Write(new byte[10]);}
    }
}
