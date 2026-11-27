using Mir2.Companions;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirObjects;

internal static class CompanionGuildPreparationChecks
{
    public static void Run(Action<bool,string> check)
    {
        var horn=new ItemInfo { Index=99971,Name="沃玛角",Type=ItemType.CraftingMaterial,StackSize=999 };
        var offer=new PublicShopEntry { ItemIndex=horn.Index,Price=1000000,MaxCount=999 };
        (PublicShopEntry Offer,ushort Count)? Buy(uint gold,int held=0) => CompanionPublicShopRules.GuildMaterial([offer],id=>id==horn.Index?horn:null,horn.Index,1,2000000,gold,100,held);
        check(Buy(2999999)==null,"建会材料采购动用了预留建会与补给金币");
        check(Buy(3000000)?.Count==1,"满足预留后不能正常购买一份建会材料");
        check(Buy(5000000,1)==null,"旧规划在材料回执后重复购买建会材料");
        check(CompanionPublicShopRules.GuildMaterial([offer],_=>horn,horn.Index,1,-1,4000000,100)==null,"负数保留预算被接受");
        var player=new PlayerObject { Info=new CharacterInfo { Index=9971,Name="会长权限检查" } };
        var guild=new GuildObject(new GuildInfo(player,"检查行会") { GuildIndex=9971 });player.MyGuild=guild;player.MyGuildRank=guild.Ranks[0];
        var war=new ConquestObject(new ConquestGuildInfo { Info=new ConquestInfo { Index=99,Type=ConquestType.Request,Monday=true },Owner=0 });
        check(ConquestRegistration.Available(player,war),"原申请制空位不能由会长报名");
        player.MyGuildRank.Index=1;check(!ConquestRegistration.Register(player,war)&&war.GuildInfo.AttackerID==-1,"普通成员可以抢报攻城");player.MyGuildRank.Index=0;
        player.Dead=true;check(!ConquestRegistration.Register(player,war),"死亡会长仍能报名");player.Dead=false;
        war.Info.Type=ConquestType.Auto;check(!ConquestRegistration.Register(player,war),"将自动城制强改成申请制");war.Info.Type=ConquestType.Request;
        war.GuildInfo.Owner=guild.Guildindex;check(!ConquestRegistration.Register(player,war),"本会对自己的城池报名");war.GuildInfo.Owner=0;
        war.Info.Monday=false;check(!ConquestRegistration.Register(player,war),"无任何战期仍安排报名");war.Info.Monday=true;
        check(ConquestRegistration.Register(player,war)&&war.GuildInfo.AttackerID==guild.Guildindex&&war.GuildInfo.NeedSave,"报名未写原攻方或未标记原城池存档");
        war.GuildInfo.NeedSave=false;check(!ConquestRegistration.Register(player,war)&&!war.GuildInfo.NeedSave,"重复报名被当作新操作");
        var stats=new Stats { [Stat.HP]=100,[Stat.MinDC]=100,[Stat.MaxDC]=100,[Stat.Accuracy]=10 };
        player.Stats=stats;player.Info.Level=30;
        var low=new MonsterInfo { AI=0,Stats=new Stats { [Stat.HP]=1 },Drops=[new DropInfo { Chance=1,Gold=10 }] };
        var rich=new MonsterInfo { AI=0,Stats=new Stats { [Stat.HP]=1 },Drops=[new DropInfo { Chance=1,Gold=10000 }] };
        check(CompanionPlanning.FundingValue(player,rich,10)>CompanionPlanning.FundingValue(player,low,1),"筹资只挑最近的低收入怪物");
    }
}
