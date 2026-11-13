using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionCombatScenario
{
    public static void Prepare(ProjectPaths paths, int characterId, bool tree, bool emptyMana = false)
    {
        if(!paths.Data.Contains("mir2-companions-",StringComparison.Ordinal))throw new InvalidOperationException("只允许修改本测试创建的隔离世界。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("测试世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load());world.LoadAccounts();
        var character=world.CharacterList.Single(role=>role.Index==characterId);
        if(character.Class!=MirClass.Wizard)throw new InvalidDataException("战斗场景必须使用法师。");
        var map=new MapInfo{Index=++world.MapIndex,FileName="0",Title=tree?"Native Tree Field":"Native Ranged Field",Light=LightSetting.Day,NoDropPlayer=true,NoDrug=true,NoTownTeleport=true};
        world.MapInfoList.Add(map);
        character.CurrentMapIndex=character.BindMapIndex=map.Index;
        character.CurrentLocation=character.BindLocation;
        character.Level=14;character.Experience=0;character.HP=52;character.MP=emptyMana?0:160;character.AccountInfo.Gold=0;
        character.Inventory=new UserItem[46];character.Equipment=new UserItem[14];
        var weapon=new ItemInfo{Index=++world.ItemIndex,Name="NativeCasterBlade",Type=ItemType.Weapon,Shape=1,Durability=5000,
            RequiredClass=RequiredClass.Wizard,RequiredGender=RequiredGender.Male,Stats=new Stats{[Stat.MinDC]=tree?5:10,[Stat.MaxDC]=tree?5:10,[Stat.Accuracy]=100,[Stat.MinMC]=100,[Stat.MaxMC]=100}};
        world.ItemInfoList.Add(weapon);character.Equipment[0]=world.CreateFreshItem(weapon);
        var spell=world.MagicInfoList.Single(magic=>magic.Spell==Spell.FireBall);
        spell.Level1=1;spell.BaseCost=10;spell.LevelCost=0;spell.DelayBase=tree?1800u:5000u;character.Magics=[new UserMagic(Spell.FireBall)];
        var monster=new MonsterInfo{Index=++world.MonsterIndex,Name=tree?"NativeSpellImmuneTree":"NativeRangedTarget",Image=tree?Monster.ChestnutTree:Monster.Hen,AI=(byte)(tree?3:0),Level=(ushort)(tree?60:1),Experience=100,
            Stats=new Stats{[Stat.HP]=tree?10:240},ViewRange=(byte)(tree?7:0),MoveSpeed=tree?(ushort)1000:ushort.MaxValue,AttackSpeed=1000};
        world.MonsterInfoList.Add(monster);
        map.Respawns.Add(new RespawnInfo{RespawnIndex=++world.RespawnIndex,MonsterIndex=monster.Index,Location=Functions.PointMove(character.BindLocation,MirDirection.Down,tree?2:3),Count=1,Delay=600});
        world.SaveDB();world.SaveAccounts();
        Console.WriteLine(tree?"已设置隔离法师：满160MP、已学火球术、可用武器；独立无NPC地图只有动态编号的10HP原栗树。":$"已设置隔离法师：魔力{character.MP}、火球冷却5秒、目标240HP，独立无NPC/药品地图。");
    }
}
