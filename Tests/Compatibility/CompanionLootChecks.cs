using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirObjects;

internal static class CompanionLootChecks
{
    public static void Run(Action<bool,string> check)
    {
        var player=new PlayerObject{Info=new CharacterInfo(),Account=new AccountInfo()};
        var other=new PlayerObject{Info=new CharacterInfo(),Account=new AccountInfo()};
        var heavy=new ItemObject(player,new UserItem(new ItemInfo{Index=1,Type=ItemType.Weapon,Weight=200}));
        var gold=new ItemObject(player,50u);
        check(CompanionLoot.FirstPickup(player,[heavy,gold])==heavy,"原PickUp无目标编号，不能跳过同格先被捡到的重物去选金币");
        heavy.Owner=other;
        check(CompanionLoot.FirstPickup(player,[heavy,gold])==gold,"拾取引导未遵守其他玩家的归属保护");
        player.GroupMembers=[player,other];
        check(CompanionLoot.FirstPickup(player,[heavy,gold])==heavy,"拾取引导遗漏原规则允许的队友归属物品");
        player.GroupMembers=null;
        heavy.Owner=null;
        Array.Fill(player.Info.Inventory,new UserItem(new ItemInfo{Index=2,StackSize=1}));
        check(CompanionLoot.FirstPickup(player,[heavy,gold])==gold,"满包时未沿用引擎的金币/物品格位判定");
        player.Account.Gold=uint.MaxValue;
        check(CompanionLoot.FirstPickup(player,[heavy,gold])==null,"金币上限已满仍规划拾金");
        HarvestChecks(check);
    }

    private static void HarvestChecks(Action<bool,string> check)
    {
        var world=Server.MirEnvir.Envir.Main;var original=world.QuestInfoList;
        try
        {
            var first=new ItemInfo{Index=901,Type=ItemType.Quest};var second=new ItemInfo{Index=902,Type=ItemType.Quest};
            world.QuestInfoList=[new QuestInfo{Index=71,ItemTasks=[new(){Item=first,Count=1},new(){Item=second,Count=1}]}];
            var player=new PlayerObject{Info=new CharacterInfo(),Account=new AccountInfo(),Stats=new Stats{[Stat.BagWeight]=100},CurrentBagWeight=80};
            var progress=new QuestProgressInfo(71);player.CurrentQuests.Add(progress);progress.ItemTaskCount[0].Count=1;
            var info=new MonsterInfo{Index=81,AI=9,Drops=[new DropInfo{Chance=1,Item=first}]};
            var monster=MonsterObject.GetMonster(info);monster.Dead=true;
            check(!CompanionLoot.CanHarvest(player,monster),"负重已达储备线时，已收齐材料的尸体仍被当成必要任务采集");
            info.Drops=[new DropInfo{Chance=1,Item=second}];
            check(CompanionLoot.CanHarvest(player,monster),"负重储备线阻止了真实未完成任务物的采集");
            info.Drops=[new DropInfo{Chance=1,GroupedDrop=new GroupDropInfo{new DropInfo{Chance=1,Item=second}}}];
            check(CompanionLoot.CanHarvest(player,monster),"分组掉落的真实任务物被漏掉");
            info.Drops[0].Chance=0;
            check(!CompanionLoot.CanHarvest(player,monster),"零概率尸体来源绕过采集储备线");
            info.Drops=[new DropInfo{Chance=1,Item=second}];progress.EndDateTime=DateTime.UtcNow;
            check(!CompanionLoot.CanHarvest(player,monster),"待交付的任务仍授权超储备采集");
            progress.EndDateTime=DateTime.MaxValue;progress.ItemTaskCount[1].Count=1;
            check(!CompanionLoot.CanHarvest(player,monster),"已收齐但尚缺其他步骤的任务仍授权超储备采集");
            player.CurrentBagWeight=79;
            check(CompanionLoot.CanHarvest(player,monster),"未达负重线时普通采集被禁止");
            monster.EXPOwner=new PlayerObject{Info=new CharacterInfo(),Account=new AccountInfo()};
            check(!CompanionLoot.CanHarvest(player,monster),"采集未保留其他玩家的尸体归属");
            player.GroupMembers=[player,(PlayerObject)monster.EXPOwner];
            check(!CompanionLoot.CanHarvest(player,monster),"仅凭经验归属者在队伍中绕过原Harvest的尸体权限");
            monster.EXPOwner=player;
            check(CompanionLoot.CanHarvest(player,monster),"本人拥有的普通尸体被拒绝");
            monster.Harvested=true;
            check(!CompanionLoot.CanHarvest(player,monster),"已采完的尸体仍可采集");monster.Harvested=false;
            Array.Fill(player.Info.Inventory,new UserItem(new ItemInfo{Type=ItemType.Ore}));progress.ItemTaskCount[1].Count=0;
            check(!CompanionLoot.CanHarvest(player,monster),"满格时未保留原采集容量保护");
        }
        finally{world.QuestInfoList=original;}
    }
}
