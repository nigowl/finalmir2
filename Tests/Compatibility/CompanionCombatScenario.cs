using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionCombatScenario
{
    public static void Prepare(ProjectPaths paths, int characterId, bool tree, bool emptyMana = false, bool retireThreat = false, bool persistentThreat = false, bool penaltyMana = false, string amuletCase="")
    {
        if(!paths.Data.Contains("mir2-companions-",StringComparison.Ordinal))throw new InvalidOperationException("只允许修改本测试创建的隔离世界。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("测试世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load());world.LoadAccounts();
        var character=world.CharacterList.Single(role=>role.Index==characterId);
        var taoist=amuletCase.Length>0;
        if(taoist && amuletCase is not ("missing" or "bag" or "equipped" or "last"))throw new InvalidDataException("未知护身符测试场景。");
        if(character.Class!=(taoist?MirClass.Taoist:MirClass.Wizard))throw new InvalidDataException("战斗场景职业不匹配。");
        var map=new MapInfo{Index=++world.MapIndex,FileName="0",Title=tree?"Native Tree Field":"Native Ranged Field",Light=LightSetting.Day,NoDropPlayer=true,NoDrug=!retireThreat,NoTownTeleport=true};
        world.MapInfoList.Add(map);
        character.CurrentMapIndex=character.BindMapIndex=map.Index;
        character.CurrentLocation=character.BindLocation;
        character.Level=14;character.Experience=0;character.HP=52;character.MP=taoist?60:emptyMana?0:penaltyMana?15:160;character.AccountInfo.Gold=0;
        character.Inventory=new UserItem[46];character.Equipment=new UserItem[14];
        if(retireThreat)
        {
            var potion=new ItemInfo{Index=++world.ItemIndex,Name="NativeRetirementMedicine",Type=ItemType.Potion,Shape=1,StackSize=20,Stats=new Stats{[Stat.HP]=30}};
            world.ItemInfoList.Add(potion);character.Inventory[0]=world.CreateFreshItem(potion);character.Inventory[0].Count=10;
        }
        var weapon=new ItemInfo{Index=++world.ItemIndex,Name="NativeCasterBlade",Type=ItemType.Weapon,Shape=1,Durability=5000,
            RequiredClass=taoist?RequiredClass.Taoist:RequiredClass.Wizard,RequiredGender=RequiredGender.Male,Stats=new Stats{[Stat.MinDC]=tree?5:10,[Stat.MaxDC]=tree?5:10,[Stat.Accuracy]=100,[Stat.MinMC]=100,[Stat.MaxMC]=100,[Stat.MinSC]=taoist?100:0,[Stat.MaxSC]=taoist?100:0,[Stat.ManaPenaltyPercent]=penaltyMana?1000:0}};
        world.ItemInfoList.Add(weapon);character.Equipment[0]=world.CreateFreshItem(weapon);
        var spell=world.MagicInfoList.Single(magic=>magic.Spell==(taoist?Spell.SoulFireBall:Spell.FireBall));
        spell.Level1=1;spell.BaseCost=10;spell.LevelCost=0;spell.DelayBase=tree?1800u:5000u;character.Magics=[new UserMagic(spell.Spell)];
        if(taoist && amuletCase!="missing")
        {
            var paper=new ItemInfo{Index=++world.ItemIndex,Name="NativeCastingAmulet",Type=ItemType.Amulet,Shape=0,StackSize=20,RequiredClass=RequiredClass.Taoist,RequiredGender=RequiredGender.Male};
            world.ItemInfoList.Add(paper);var item=world.CreateFreshItem(paper);item.Count=(ushort)(amuletCase=="last"?1:20);
            if(amuletCase=="bag")
            {
                character.Inventory[0]=item;
                var poison=new ItemInfo{Index=++world.ItemIndex,Name="NativeWornPoison",Type=ItemType.Amulet,Shape=1,StackSize=20,RequiredClass=RequiredClass.Taoist,RequiredGender=RequiredGender.Male};
                world.ItemInfoList.Add(poison);character.Equipment[9]=world.CreateFreshItem(poison);character.Equipment[9].Count=20;
            }
            else character.Equipment[9]=item;
        }
        if(persistentThreat)character.Magics.Clear();
        var monster=new MonsterInfo{Index=++world.MonsterIndex,Name=tree?"NativeSpellImmuneTree":"NativeRangedTarget",Image=tree?Monster.ChestnutTree:Monster.Hen,AI=(byte)(tree?3:0),Level=(ushort)(tree?60:1),Experience=100,
            Stats=new Stats{[Stat.HP]=persistentThreat?400:tree?10:240,[Stat.MinDC]=persistentThreat?1:retireThreat?6:0,[Stat.MaxDC]=persistentThreat?1:retireThreat?6:0,[Stat.Accuracy]=retireThreat?100:0},ViewRange=(byte)(retireThreat?16:tree?7:0),MoveSpeed=tree||retireThreat?(ushort)1000:ushort.MaxValue,AttackSpeed=1000};
        world.MonsterInfoList.Add(monster);
        if(persistentThreat)
        {
            for(var x=-1;x<=1;x++)map.Respawns.Add(new RespawnInfo{RespawnIndex=++world.RespawnIndex,MonsterIndex=monster.Index,
                Location=new(character.BindLocation.X+x,character.BindLocation.Y+3),Count=1,Delay=600});
        }
        // 起点四格内已由fixture验证连通，不能让退场检查先卡在十二格外的随机追击。
        else map.Respawns.Add(new RespawnInfo{RespawnIndex=++world.RespawnIndex,MonsterIndex=monster.Index,Location=Functions.PointMove(character.BindLocation,MirDirection.Down,tree?2:3),Count=1,Delay=600});
        world.SaveDB();world.SaveAccounts();
        Console.WriteLine(taoist?$"已设置隔离道士：60MP、灵魂火符冷却5秒、护身符{amuletCase}、目标240HP，不能无耗材空放。":persistentThreat?"已设置三只400HP/1DC持续追击怪，角色无攻击魔法；单只可猎杀，不应先打完才退场。":
            retireThreat?"已设置退场受击场景：暂停后由原怪物主动攻击，必须自卫收尾并等待原退出确认。":tree?"已设置隔离法师：满160MP、已学火球术、可用武器；独立无NPC地图只有动态编号的10HP原栗树。":$"已设置隔离法师：魔力{character.MP}、火球冷却5秒{(penaltyMana?"、额外耗蓝1000%（基础10/实际110）":"")}、目标240HP，独立无NPC/药品地图。");
    }
}
