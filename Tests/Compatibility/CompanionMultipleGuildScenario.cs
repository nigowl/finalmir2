using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionMultipleGuildScenario
{
    public static void Prepare(ProjectPaths paths, int[] roles)
    {
        if(roles.Length!=4)throw new InvalidOperationException("多行会专项需要四个隔离角色。");
        CompanionGuildScenario.Run(paths,roles[0],roles[1],"prepare");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("隔离世界读取失败。");world.LoadAccounts();world.LoadGuilds();
        var original=world.CharacterList.Single(c=>c.Index==roles[0]);var token=world.ItemInfoList.Single(i=>i.Name=="NativeGuildToken");
        foreach(var role in world.CharacterList.Where(c=>roles.Contains(c.Index)))
        {
            role.Level=22;role.HP=1000;role.MP=1000;role.AccountInfo.Gold=5000;role.CurrentMapIndex=original.CurrentMapIndex;role.CurrentLocation=original.CurrentLocation;
            role.Inventory=new UserItem[80];role.Equipment=new UserItem[14];role.CurrentQuests.Clear();role.CompletedQuests.Clear();role.Magics.Clear();
            role.Equipment[0]=world.CreateFreshItem(world.ItemInfoList.Single(i=>i.Name=="NativeGuildBlade"+roles[0]));
            var red=world.ItemInfoList.First(i=>i.Type==ItemType.Potion&&i.Shape is 0 or 1&&i.Stats[Stat.HP]>0);role.Inventory[1]=world.CreateFreshItem(red);role.Inventory[1].Count=20;
            var town=world.ItemInfoList.First(i=>i.Type==ItemType.Scroll&&i.Shape==1);role.Inventory[2]=world.CreateFreshItem(town);
            if(role.Index==roles[0]||role.Index==roles[1])role.Inventory[0]=world.CreateFreshItem(token);
        }
        world.SaveDB();world.SaveAccounts();
    }
}
