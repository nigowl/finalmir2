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
    }
}
