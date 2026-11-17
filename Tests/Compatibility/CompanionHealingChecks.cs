using Crystal.Native;
using Mir2.Companion;
using S = ServerPackets;

internal static class CompanionHealingChecks
{
    public static void Run(Action<bool, string> check)
    {
        var state = new GameState { BaseStats = new BaseStats(MirClass.Taoist) { Stats = [new BaseStat(Stat.HP) { Base = 100 }] } };
        check(CompanionSafety.SelfHealing(state, 10000)==null, "未进入游戏仍尝试自疗");
        state.Apply(new S.UserInformation { ObjectID=1, Class=MirClass.Taoist, HP=70, MP=8, Inventory=[], Equipment=new UserItem[14], QuestInventory=[], Magics=[] });
        check(CompanionSafety.SelfHealing(state,10000)==null,"未学会治愈术仍尝试施放");
        var heal = new ClientMagic { Spell=Spell.Healing, BaseCost=5, LevelCost=2, Level=1 };
        state.User!.Magics.Add(heal);
        check(CompanionSafety.SelfHealing(state,10000)==heal,"低血量且已学技能/足够MP时未自疗");
        state.User.HP=81; check(CompanionSafety.SelfHealing(state,10000)==null,"生命充足仍消耗MP");
        state.User.HP=80; check(CompanionSafety.SelfHealing(state,10000)==heal,"80%自疗阈值错误");
        state.User.HP=0; check(CompanionSafety.SelfHealing(state,10000)==null,"零血量仍自疗");
        state.User.HP=70; state.Me!.Dead=true; check(CompanionSafety.SelfHealing(state,10000)==null,"死亡仍自疗");
        state.Me.Dead=false; state.User.MP=6; check(CompanionSafety.SelfHealing(state,10000)==null,"技能等级耗蓝不足仍施放");
        state.User.MP=7; check(CompanionSafety.SelfHealing(state,10000)==heal,"恰好够MP被拒绝");
        state.Apply(new S.TradeAccept { Name="队友" }); check(CompanionSafety.SelfHealing(state,10000)==null,"交易期间自疗");
        state.Apply(new S.TradeCancel()); state.StorageActive=true; check(CompanionSafety.SelfHealing(state,10000)==null,"仓库期间自疗");
        state.StorageActive=false; state.DisabledSkills.Add(heal.Spell); check(CompanionSafety.SelfHealing(state,10000)==null,"关闭技能仍施放");
        state.DisabledSkills.Clear(); heal.CastTime=6000; check(CompanionSafety.SelfHealing(state,10000)==null,"持续回血尚未等待5秒就重施");
        check(CompanionSafety.SelfHealing(state,11000)==heal,"5秒后未恢复自疗决策");
        heal.Delay=8000; check(CompanionSafety.SelfHealing(state,11000)==null,"忽略版本技能冷却");
        check(CompanionSafety.SelfHealing(state,14000)==heal,"版本技能冷却结束仍阻止自疗");
        state.User.Equipment[0]=new UserItem(new ItemInfo { Stats=new Stats { [Stat.ManaPenaltyPercent]=100 } });
        check(CompanionSafety.SelfHealing(state,14000)==null,"未计入原装备耗蓝惩罚");
        state.User.MP=14; check(CompanionSafety.SelfHealing(state,14000)==heal,"耗蓝惩罚后足够MP未自疗");
        check(!CompanionSafety.DeathBudgetReached(new(),3,3),"尚未进入游戏被死亡预算误停用");
        check(!CompanionSafety.DeathBudgetReached(state,3,3),"存活角色被死亡预算误停用");
        state.Me!.Dead=true;
        check(!CompanionSafety.DeathBudgetReached(state,2,3),"预算尚未达到就停用");
        check(CompanionSafety.DeathBudgetReached(state,3,3),"达到死亡预算可被正常退场绕过");
    }
}
