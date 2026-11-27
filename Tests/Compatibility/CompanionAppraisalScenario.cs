using System.Text.Json;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionAppraisalScenario
{
    public static void Prepare(ProjectPaths paths,int characterId,bool scroll,bool audit=false,bool stone=false)
    {
        if(!paths.Data.Contains("mir2-companions-development-",StringComparison.Ordinal))throw new InvalidOperationException("只允许本鉴定隔离夹具。");
        using var gate=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("隔离世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load());world.LoadAccounts();
        var role=world.CharacterList.Single(role=>role.Index==characterId);
        if(audit)
        {
            if(stone)
            {
                var recovery=role.Equipment[(int)EquipmentSlot.Torch];
                File.WriteAllText(Path.Combine(paths.Runtime,"companion-stone-audit.json"),JsonSerializer.Serialize(new
                {role.AccountInfo.Gold,recovery.UniqueID,Remaining=MagicBloodStone.Remaining(recovery),Capacity=recovery.Info.Stats[Stat.MagicBlood]}));return;
            }
            var weapon=role.Equipment[0]??throw new InvalidDataException("鉴定后没有重穿武器。");
            File.WriteAllText(Path.Combine(paths.Runtime,"companion-appraisal-audit.json"),JsonSerializer.Serialize(new
            {role.AccountInfo.Gold,weapon.UniqueID,weapon.Identified,MinDC=weapon.AddedStats[Stat.MinDC],MaxDC=weapon.AddedStats[Stat.MaxDC]}));return;
        }
        role.Level=25;role.AccountInfo.Gold=10000;role.HP=role.MP=10000;role.Experience=0;
        role.CurrentMapIndex=role.BindMapIndex;role.CurrentLocation=role.BindLocation;
        role.Inventory=new UserItem[80];role.Equipment=new UserItem[14];role.Magics.Clear();role.CurrentQuests.Clear();role.CompletedQuests.Clear();
        foreach(var map in world.MapInfoList)map.Respawns.Clear();
        foreach(var info in world.ItemInfoList)info.Price=0;
        world.QuestInfoList=[];world.NPCInfoList=[];
        var definition=new ItemInfo{Index=++world.ItemIndex,Name="隔离成长鉴定剑",Type=ItemType.Weapon,Price=2000,Durability=10000,
            Stats=new Stats{[Stat.MinDC]=10,[Stat.MaxDC]=10}};
        world.ItemInfoList.Add(definition);var sword=world.CreateFreshItem(definition);
        sword.EquipmentQuality=EquipmentGrade.Supreme;sword.Identified=false;role.Equipment[0]=sword;
        foreach(var info in new[]{world.ItemInfoList.First(info=>info.Type==ItemType.Potion&&info.Shape==0&&info.Stats[Stat.HP]>0),
            world.ItemInfoList.First(info=>info.Type==ItemType.Potion&&info.Shape==0&&info.Stats[Stat.MP]>0),world.ItemInfoList.First(info=>info.Type==ItemType.Scroll&&info.Shape==1)})
        {
            info.RequiredClass=RequiredClass.None;info.RequiredGender=RequiredGender.None;info.RequiredAmount=0;info.StackSize=999;
            var item=world.CreateFreshItem(info);item.Count=(ushort)(info.Type==ItemType.Potion?20:1);role.Inventory[Array.IndexOf(role.Inventory,null)]=item;
        }
        if(scroll)
        {
            var info=new ItemInfo{Index=++world.ItemIndex,Name="隔离装备鉴定卷轴",Type=ItemType.Scroll,Shape=EquipmentAppraisal.ScrollShape,StackSize=999};
            world.ItemInfoList.Add(info);role.Inventory[Array.IndexOf(role.Inventory,null)]=world.CreateFreshItem(info);
        }
        else if(!stone)
        {
            var npc=new NPCInfo{Index=++world.NPCIndex,Name="隔离鉴定先生",FileName="CompanionAppraiser",MapIndex=role.CurrentMapIndex,
                Location=Functions.PointMove(role.CurrentLocation,MirDirection.Left,3)};world.NPCInfoList.Add(npc);
            File.WriteAllText(Path.Combine(Settings.NPCPath,npc.FileName+".txt"),"[@MAIN]\n#SAY\n<鉴定装备/@APPRAISE>\n[@APPRAISE]\n");
        }
        var stoneIndex=0;
        if(stone)
        {
            role.AccountInfo.Gold=1000000;role.MP=70;sword.Identified=true;
            var recovery=new ItemInfo{Index=++world.ItemIndex,Name="隔离恢复魔血石",Type=ItemType.Torch,Durability=1,Price=10000,
                Stats=new Stats{[Stat.MagicBlood]=100000}};
            world.ItemInfoList.Add(recovery);stoneIndex=recovery.Index;
        }
        Settings.AutoLearnSkills=false;Settings.AllowNewHero=false;Settings.SaveHeroSettings();
        var setup=new InIReader(Path.Combine(paths.World,"Configs/Setup.ini"));setup.Write("AutoLearnSkills","Enabled",false);setup.Save();
        world.SaveDB();world.SaveAccounts();
        File.WriteAllText(Path.Combine(paths.Runtime,"companion-development-fixture.json"),JsonSerializer.Serialize(new{weapon=sword.UniqueID,cost=scroll?0:200,stone=stoneIndex}));
    }
}
