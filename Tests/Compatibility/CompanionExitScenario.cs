using System.Text.Json;
using Client.MirObjects;
using Mir2.Companions;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionExitScenario
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
        var remote=world.MapInfoList.Single(map=>map.Index==2);var exit=remote.Movements[0];
        var terrain=new MapReader(Path.Combine(paths.Assets,"maps",remote.FileName+".map"));
        var neighbors=Enumerable.Range(0,8).Select(direction=>Functions.PointMove(exit.Source,(MirDirection)direction,1)).ToArray();
        if(neighbors.Any(point=>(terrain.MapCells[point.X,point.Y].BackImage&0x20000000)!=0||(terrain.MapCells[point.X,point.Y].FrontImage&0x8000)!=0))
            throw new InvalidDataException("隔离出口邻格必须原本可行走，不能用地形阻挡替代占格。");
        foreach(var point in neighbors)world.NPCInfoList.Add(new NPCInfo{Index=++world.NPCIndex,MapIndex=remote.Index,
            Location=point,Name="Native Exit Blocker",FileName="NativeExitBlocker"});
        File.WriteAllText(Path.Combine("Envir","NPCs","NativeExitBlocker.txt"),"[@MAIN]\n#SAY\n占格回归。\n");
        foreach(var map in world.MapInfoList)map.Respawns.Clear();
        for(var i=0;i<character.Inventory.Length;i++)
            if(CompanionSupplyRules.IsPotion(character.Inventory[i],Stat.HP))character.Inventory[i]=null;
        character.CurrentMapIndex=remote.Index;character.CurrentLocation=exit.Source;
        File.WriteAllText(Path.Combine(paths.Runtime,"companion-exit-fixture.json"),JsonSerializer.Serialize(new{
            origin=remote.Index,destination=exit.MapIndex,x=exit.Source.X,y=exit.Source.Y,
            neighbors=neighbors.Select(point=>new{x=point.X,y=point.Y})}));
        world.SaveDB();world.SaveAccounts();
        Console.WriteLine("已设置站在出口格、八个邻格全被可见NPC占用、缺红药须跨图补给的隔离场景。");
    }
}
