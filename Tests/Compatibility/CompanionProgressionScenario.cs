using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using System.Text.Json;

internal static class CompanionProgressionScenario
{
    public static void Prepare(ProjectPaths paths,int characterId)
    {
        if(!paths.Data.Contains("mir2-companions-progression-",StringComparison.Ordinal))throw new InvalidOperationException("仅允许修改本主线测试的临时版本。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("测试世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load());world.LoadAccounts();
        var character=world.CharacterList.Single(c=>c.Index==characterId);var start=character.BindLocation;
        character.Level=10;character.Experience=0;character.HP=character.MP=1000;character.AccountInfo.Gold=0;
        character.CurrentMapIndex=character.BindMapIndex;character.CurrentLocation=start;character.CompletedQuests.Clear();
        character.Inventory=new UserItem[46];character.Equipment=new UserItem[14];character.QuestInventory=new UserItem[40];
        foreach(var map in world.MapInfoList){map.Respawns.Clear();map.NoFight=false;}
        var local=world.GetMapInfo(character.BindMapIndex);var remote=world.MapInfoList.Single(m=>m.Index!=local.Index);
        var blade=new ItemInfo{Index=++world.ItemIndex,Name="NativeProgressionBlade",Type=ItemType.Weapon,Durability=10000,
            Stats=new Stats{[Stat.MinDC]=100,[Stat.MaxDC]=100,[Stat.Accuracy]=100}};
        var token=new ItemInfo{Index=++world.ItemIndex,Name="NativeProgressionLetter",Type=ItemType.Quest,StackSize=1};
        var protectedItem=new ItemInfo{Index=++world.ItemIndex,Name="NativeProgressionProtected",Type=ItemType.Ore,StackSize=1};
        world.ItemInfoList.AddRange([blade,token,protectedItem]);character.Equipment[0]=world.CreateFreshItem(blade);character.Inventory[0]=world.CreateFreshItem(protectedItem);
        character.Magics=[new UserMagic(Spell.Fencing){Level=1,Experience=23}];
        world.MonsterInfoList=[new MonsterInfo{Index=1,Name="NativeProgressionOldTarget",AI=0,Image=Monster.Hen,Level=1,ViewRange=0,
            MoveSpeed=ushort.MaxValue,AttackSpeed=2500,Experience=0,Stats=new Stats{[Stat.HP]=1}}];world.MonsterIndex=1;
        local.Respawns.Add(new RespawnInfo{MonsterIndex=1,RespawnIndex=1,Count=1,Delay=600,Location=Functions.PointMove(start,MirDirection.Down,2)});
        world.QuestInfoList.Clear();
        foreach(var id in new[]{1,2,3})
        {
            var q=new QuestInfo{Index=id,Name="Native Progression "+id,FileName="NativeProgression"+id,RequiredMinLevel=1,RequiredMaxLevel=255,RequiredQuest=id==3?2:0};
            world.QuestInfoList.Add(q);
            File.WriteAllText(Path.Combine(Settings.QuestPath,q.FileName+".txt"),"[@DESCRIPTION]\n推进后续剧情，不丢弃原任务。\n"+
                (id==1?"[@KILLTASKS]\nNativeProgressionOldTarget 100\n":(id==2?"[@CARRYITEMS]\nNativeProgressionLetter 1\n":"[@FLAGTASKS]\n521 \"石柱1\"\n522 \"石柱2\"\n523 \"石柱3\"\n")+$"[@EXPREWARD]\n50000\n[@GOLDREWARD]\n{(id==2?111:222)}\n"));
            q.LoadInfo();
        }
        world.QuestIndex=3;character.CurrentQuests=[new(1)];character.CurrentQuests[0].StartDateTime=DateTime.UtcNow;
        world.NPCInfoList=[new NPCInfo{Index=1,Name="Native Progression Start",FileName="NativeProgressionStart",MapIndex=local.Index,Location=Functions.PointMove(start,MirDirection.Left,3)},
            new NPCInfo{Index=2,Name="Native Progression Return",FileName="NativeProgressionReturn",MapIndex=remote.Index,Location=Functions.PointMove(start,MirDirection.Left,3)}];world.NPCIndex=2;
        File.WriteAllText(Path.Combine(Settings.NPCPath,"NativeProgressionStart.txt"),"[@MAIN]\n#SAY\n主线接取与回访。\n[Quests]\n1\n2\n-3\n");
        File.WriteAllText(Path.Combine(Settings.NPCPath,"NativeProgressionReturn.txt"),"[@MAIN]\n#SAY\n送信后解锁回访。\n[Quests]\n-2\n3\n");
        for(var flag=521;flag<=523;flag++)
        {
            var marker=new NPCInfo{Index=++world.NPCIndex,Name="主线石柱"+flag,FileName="NativeProgressionFlag"+flag,
                MapIndex=flag==522?local.Index:remote.Index,Location=Functions.PointMove(start,MirDirection.Right,5+(flag-521)*5)};
            world.NPCInfoList.Add(marker);
            File.WriteAllText(Path.Combine(Settings.NPCPath,marker.FileName+".txt"),$"[@MAIN]\n#IF\nCHECKQUEST 3 ACTIVE\n#SAY\n发现石柱。\n#ACT\nSET [{flag}] 1\nLOCALMESSAGE \"发现石柱{flag}\" Hint\nBREAK\n#ELSESAY\n石柱静静地矗立。\n");
        }
        world.SaveDB();world.SaveAccounts();
        File.WriteAllText(Path.Combine(paths.Runtime,"companion-progression-fixture.json"),JsonSerializer.Serialize(new{origin=local.Index,remote=remote.Index,token=token.Index,
            weaponId=character.Equipment[0].UniqueID,protectedId=character.Inventory[0].UniqueID}));
        Console.WriteLine("已设置原100杀任务未完成、两图送信/前置三石柱链：必须真实触发521/522/523后交付2→3并升级，原任务与资产保留。");
    }
}
