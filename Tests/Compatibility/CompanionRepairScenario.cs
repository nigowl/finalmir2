using System.Text.Json;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionRepairScenario
{
    public static void Prepare(ProjectPaths paths,int characterId)
    {
        if(!paths.Data.Contains("mir2-companions-",StringComparison.Ordinal))throw new InvalidOperationException("只允许修改本测试创建的隔离世界。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("测试世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load());world.LoadAccounts();
        var character=world.CharacterList.Single(role=>role.Index==characterId);
        character.Inventory=new UserItem[46];character.Equipment=new UserItem[14];character.Level=10;character.HP=character.MP=10000;
        character.AccountInfo.Gold=20000;character.CurrentQuests.Clear();character.CompletedQuests=world.QuestInfoList.Select(quest=>quest.Index).ToList();
        foreach(var map in world.MapInfoList)map.Respawns.Clear();
        var remote=world.MapInfoList.Single(map=>map.Index==2);
        character.CurrentMapIndex=remote.Index;character.CurrentLocation=remote.Movements[0].Source;
        var damaged=new[]{ItemType.Weapon,ItemType.Armour}.Select(type=>new ItemInfo{Index=++world.ItemIndex,Name="NativeRepair"+type,
            Type=type,Price=10000,Durability=10000,Weight=0,Stats=new Stats{[type==ItemType.Weapon?Stat.MaxDC:Stat.MaxAC]=100}}).ToArray();
        world.ItemInfoList.AddRange(damaged);
        for(var i=0;i<damaged.Length;i++){character.Equipment[i]=world.CreateFreshItem(damaged[i]);character.Equipment[i].CurrentDura=(ushort)(i==0?1000:0);}
        var ring=new ItemInfo{Index=++world.ItemIndex,Name="NativeRepairRing",Type=ItemType.Ring,Price=1000,Durability=10000};world.ItemInfoList.Add(ring);
        foreach(var slot in new[]{7,8}){character.Equipment[slot]=world.CreateFreshItem(ring);character.Equipment[slot].CurrentDura=0;}
        var spareInfo=new ItemInfo{Index=++world.ItemIndex,Name="NativeRepairSpare",Type=ItemType.Weapon,Price=1000,Durability=10000,Stats=new Stats{[Stat.MaxDC]=100}};
        world.ItemInfoList.Add(spareInfo);var spare=world.CreateFreshItem(spareInfo);spare.CurrentDura=1000;character.Inventory[0]=spare;
        foreach(var info in new[]{world.ItemInfoList.First(i=>i.Type==ItemType.Potion&&i.Shape==0&&i.Stats[Stat.HP]>0),
            world.ItemInfoList.First(i=>i.Type==ItemType.Potion&&i.Shape==0&&i.Stats[Stat.MP]>0),world.ItemInfoList.First(i=>i.Type==ItemType.Scroll&&i.Shape==1)})
        {
            info.RequiredClass=RequiredClass.None;info.RequiredGender=RequiredGender.None;info.RequiredType=RequiredType.Level;info.RequiredAmount=0;info.Weight=0;info.StackSize=20;
            var supply=world.CreateFreshItem(info);supply.Count=(ushort)(info.Type==ItemType.Potion?20:1);character.Inventory[Array.IndexOf(character.Inventory,null)]=supply;
        }
        var tempting=new ItemInfo{Index=++world.ItemIndex,Name="NativeRepairTemptation",Type=ItemType.Weapon,Price=1,Durability=10000,Stats=new Stats{[Stat.MaxDC]=200}};
        world.ItemInfoList.Add(tempting);world.NPCInfoList.Clear();
        AddNpc(world,remote.Index,Functions.PointMove(character.CurrentLocation,MirDirection.Right,4),"Native Wrong Repair",100,[ItemType.Torch],tempting);
        if(character.Equipment.Take(2).Any(item=>item.RepairPrice()*6d<character.AccountInfo.Gold/2d))throw new InvalidDataException("超预算修理商人的测试费率不足。");
        AddNpc(world,remote.Index,Functions.PointMove(character.CurrentLocation,MirDirection.Left,4),"Native Costly Repair",600,[ItemType.Weapon,ItemType.Armour],tempting);
        var vendor=AddNpc(world,character.BindMapIndex,Functions.PointMove(character.BindLocation,MirDirection.Left,2),"Native Repair",125,[ItemType.Weapon,ItemType.Armour,ItemType.Ring],tempting);
        File.WriteAllText(Path.Combine(paths.Runtime,"companion-repair-fixture.json"),JsonSerializer.Serialize(new{
            origin=remote.Index,destination=character.BindMapIndex,gold=character.AccountInfo.Gold,vendorId=vendor.Index,spareId=spare.UniqueID,temptingId=tempting.Index,
            repairs=character.Equipment.Select((item,slot)=>(item,slot)).Where(pair=>pair.item!=null).Select(pair=>new{id=pair.item.UniqueID,itemId=pair.item.ItemIndex,slot=pair.slot,
                cost=(uint)(pair.item.RepairPrice()*1.25f),maxDura=pair.item.MaxDura-(pair.item.MaxDura-pair.item.CurrentDura)/30})}));
        world.SaveDB();world.SaveAccounts();
        Console.WriteLine("已设置药卷齐全、零耐久衣服及低耐久武器、远处125%修理商人，近处错误类型/超预算商人和破损备装。");
    }

    private static NPCInfo AddNpc(Envir world,int map,System.Drawing.Point location,string name,ushort rate,ItemType[] types,ItemInfo tempting)
    {
        var npc=new NPCInfo{Index=++world.NPCIndex,MapIndex=map,Location=location,Name=name,FileName=name.Replace(" ",""),Rate=rate};world.NPCInfoList.Add(npc);
        File.WriteAllText(Path.Combine(Settings.NPCPath,npc.FileName+".txt"),
            "[@MAIN]\n#SAY\n修理回归。\n<修理/@REPAIR> <购买/@BUY>\n\n[@REPAIR]\n#SAY\n原服修理。\n\n[@BUY]\n#SAY\n原服商店。\n\n[Types]\n"+
            string.Join('\n',types.Select(type=>(int)type))+"\n\n[Trade]\n"+tempting.Name+" 1\n");
        return npc;
    }
}
