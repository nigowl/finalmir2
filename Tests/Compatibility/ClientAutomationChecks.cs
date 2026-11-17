using System.Text.Json;
using Crystal.Native;
using Point = System.Drawing.Point;
using S = ServerPackets;

internal static class ClientAutomationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var preferences = new ClientPreferences();
        check(!preferences.AutoHp && !preferences.AutoMp && !preferences.DefaultRun, "Client automation was enabled by default");
        preferences.Volume = 500; preferences.HpPercent = -1; preferences.MpPercent = 200; preferences.Normalize();
        check(preferences.Volume == 100 && preferences.HpPercent == 1 && preferences.MpPercent == 95, "Invalid client preferences escaped normalization");
        check(preferences.CrtStrength == 3 && JsonSerializer.Deserialize<ClientPreferences>("{\"CrtFilter\":true}")!.CrtStrength == 3, "Existing CRT settings lost the compatible default strength");
        preferences.CrtStrength = -1; preferences.Normalize();
        check(preferences.CrtStrength == 1, "CRT intensity escaped its lower bound");
        preferences.CrtStrength = 99; preferences.Normalize();
        check(preferences.CrtStrength == 5, "CRT intensity escaped its upper bound");
        preferences.CrtFilter = true;
        preferences.ShowItemNames = true;
        var restored = JsonSerializer.Deserialize<ClientPreferences>(JsonSerializer.Serialize(preferences))!;
        check(restored.Volume == 100 && restored.HpPercent == 1 && !JsonSerializer.Serialize(preferences).Contains("autoBattleEnabled"), "Client settings persisted session-only auto-battle state");
        check(restored.CrtFilter && restored.CrtStrength == 5 && restored.ShowNpcNames && !restored.ShowMonsterNames && restored.ShowItemNames, "Visual settings lost the CRT/name display preferences on persistence");
        check(!JsonSerializer.Deserialize<ClientPreferences>("{}")!.ShowItemNames, "Existing settings enabled ground names without selecting the option");
        var red = new UserItem(new ItemInfo { Index = 1, Type = ItemType.Potion, Shape = 0, Stats = new Stats { [Stat.HP] = 20 } }) { UniqueID = 1, Count = 2 };
        var blue = new UserItem(new ItemInfo { Index = 2, Type = ItemType.Potion, Shape = 1, Stats = new Stats { [Stat.MP] = 20 } }) { UniqueID = 2, Count = 2 };
        var buff = new UserItem(new ItemInfo { Index = 3, Type = ItemType.Potion, Shape = 3, Stats = new Stats { [Stat.HP] = 500 } }) { UniqueID = 3, Count = 2 };
        var state = new GameState { BaseStats = new BaseStats(MirClass.Warrior) { Stats = [new BaseStat(Stat.HP) { Base = 100 }, new BaseStat(Stat.MP) { Base = 100 }] } };
        state.Apply(new S.NewItemInfo { Info = red.Info }); state.Apply(new S.NewItemInfo { Info = blue.Info }); state.Apply(new S.NewItemInfo { Info = buff.Info });
        state.Apply(new S.UserInformation { ObjectID = 1, Location = new Point(10, 10), HP = 20, MP = 10, Inventory = [buff, red, blue], Equipment = new UserItem[14], QuestInventory = [] });
        red.Info.Weight = 2; blue.Info.Weight = 3;
        check(state.InventoryWeight == .12m, "客户端未按不同规格药品的小数重量累计或遗漏数量");
        var automation = new ClientAutomation(); preferences = new ClientPreferences { AutoHp = true, AutoMp = true };
        check(automation.NextItem(state, preferences, 1000) == red && red.Count == 2, "Auto-potion selected a buff or deducted the item before its acknowledgement");
        check(automation.NextItem(state, preferences, 1200) == null, "Auto-potion sent a duplicate while waiting for the server");
        automation.Apply(new S.UseItem { UniqueID = 2, Grid = MirGridType.Inventory, Success = true }, 1300);
        check(automation.PendingItem == 1, "An unrelated item acknowledgement unlocked auto-potion");
        automation.Apply(new S.UseItem { UniqueID = 1, Grid = MirGridType.Inventory, Success = true }, 1400);
        state.Apply(new S.UseItem { UniqueID = 1, Grid = MirGridType.Inventory, Success = true });
        check(red.Count == 1 && automation.NextItem(state, preferences, 1500) == null, "Auto-potion bypassed its acknowledged cooldown or consumed the wrong count");
        state.User!.HP = 100;
        check(automation.NextItem(state, preferences, 2400) == blue, "Low MP did not select blue potion after HP recovered");
        automation.Apply(new S.UseItem { UniqueID = 2, Grid = MirGridType.Inventory, Success = false }, 2450);
        check(automation.Paused && automation.NextItem(state, preferences, 5000) == null, "Auto-potion retried a rejected asset operation");
        automation.Resume();
        check(automation.NextItem(state, preferences, 6000) == blue, "Reapplying settings did not resume acknowledged potion use");
        check(automation.NextItem(state, preferences, 9000) == null && automation.Paused && automation.PendingItem == 2, "A timeout silently cleared an unresolved potion operation");
        automation.Resume();
        check(automation.NextItem(state, preferences, 10000) == null, "Reapplying settings resent an unresolved timed-out potion");
        automation.Apply(new S.UseItem { UniqueID = 2, Grid = MirGridType.Inventory, Success = true }, 10100);
        check(automation.PendingItem == 0 && automation.Paused, "A late acknowledgement silently resumed paused automation");
        automation.Reset(); state.Me!.Dead = true;
        check(automation.NextItem(state, preferences, 12000) == null, "Dead players automatically consumed potions");
        state.Me.Dead = false; state.TradePartner = "Peer";
        check(automation.NextItem(state, preferences, 12000) == null, "Trading players automatically consumed potions");
        state.TradePartner = ""; state.StorageActive = true;
        check(automation.NextItem(state, preferences, 12000) == null, "A storage operation raced auto-potion");
        state.StorageActive = false; preferences.AutoMp = false;
        check(automation.NextItem(state, preferences, 13000) == null, "Disabled auto-MP consumed an item");
        foreach (var (id, ai, master, dead, hidden) in new[] { (2u, 0, 0u, false, false), (3u, 6, 0u, false, false), (4u, 0, 1u, false, false), (5u, 0, 0u, true, false), (6u, 0, 0u, false, true), (7u, 113, 0u, false, false) })
            state.Apply(new S.ObjectMonster { ObjectID = id, AI = (byte)ai, MasterObjectId = master, Location = new Point(11, 10), Dead = dead, Hidden = hidden });
        check(state.Actors[4].MasterObjectId == 1 && state.Actors[3].AI == 6, "Monster AI/ownership metadata was lost in the reducer");
        check(ClientAutomation.Targets(state).Select(actor => actor.Id).SequenceEqual(new[] { 2u }), "Auto-battle selected a guard, pet, corpse or hidden monster");
        ProtectionAndSkills(state, check);
        EmergencyCooldown(check);
    }

    private static void EmergencyCooldown(Action<bool,string> check)
    {
        var state=new GameState{BaseStats=new BaseStats(MirClass.Wizard){Stats=[new(Stat.HP){Base=100}]}};
        var red=new UserItem(new ItemInfo{Index=1,Type=ItemType.Potion,Stats=new Stats{[Stat.HP]=30}}){UniqueID=1,Count=5};
        var town=new UserItem(new ItemInfo{Index=2,Type=ItemType.Scroll,Shape=1}){UniqueID=2,Count=2};
        state.Apply(new S.NewItemInfo{Info=red.Info});state.Apply(new S.NewItemInfo{Info=town.Info});
        state.Apply(new S.UserInformation{ObjectID=1,HP=60,Inventory=[red,town],Equipment=new UserItem[14],QuestInventory=[]});
        var preferences=new ClientPreferences{AutoHp=true,HpPercent=65,AutoTown=true,TownPercent=35};var automation=new ClientAutomation();
        check(automation.NextItem(state,preferences,1000)==red,"紧急回城场景没有先正常喝药");
        state.User!.HP=20;
        check(automation.NextItem(state,preferences,1100)==null,"未确认药品仍并发消耗回城卷");
        automation.Apply(new S.UseItem{Grid=MirGridType.Inventory,UniqueID=1,Success=true},1200);
        check(automation.NextItem(state,preferences,1250)==town,"喝药成功后的普通间隔阻止危急回城");
        automation.Apply(new S.UseItem{Grid=MirGridType.Inventory,UniqueID=2,Success=true},1300);
        check(automation.NextItem(state,preferences,1400)==null,"同一次低血保护重复耗卷或取消普通药品间隔");
        automation.Reset();state.User.HP=60;preferences.AutoTown=false;preferences.AutoRandom=true;preferences.RandomPercent=35;
        check(automation.NextItem(state,preferences,2000)==red,"随机回城场景没有先喝药");
        town.Info.Shape=2;state.User.HP=20;automation.Apply(new S.UseItem{Grid=MirGridType.Inventory,UniqueID=1,Success=true},2100);
        check(automation.NextItem(state,preferences,2150)==town,"喝药间隔阻止危急随机保护");
        automation.Apply(new S.UseItem{Grid=MirGridType.Inventory,UniqueID=2,Success=false},2200);
        check(automation.Paused&&automation.NextItem(state,preferences,6000)==null,"紧急逃生放宽了原拒绝停用保护");
    }

    private static void ProtectionAndSkills(GameState state, Action<bool, string> check)
    {
        var random = new UserItem(new ItemInfo { Index = 4, Type = ItemType.Scroll, Shape = 2 }) { UniqueID = 4, Count = 2 };
        var town = new UserItem(new ItemInfo { Index = 5, Type = ItemType.Scroll, Shape = 1 }) { UniqueID = 5, Count = 2 };
        state.User!.Inventory = [random, town]; state.User.HP = 10;
        var preferences = new ClientPreferences { AutoTown = true, AutoRandom = true };
        var automation = new ClientAutomation();
        check(automation.NextItem(state, preferences, 1000) == town && automation.PendingScrollShape == 1 && town.Count == 2, "Emergency protection failed to prioritize acknowledged town scroll over random scroll");
        check(automation.NextItem(state, preferences, 1100, true) == null, "Idle teleport raced an outstanding protection request");
        automation.Apply(new S.UseItem { UniqueID = 5, Grid = MirGridType.Inventory, Success = true }, 1200);
        check(automation.NextItem(state, preferences, 2300) == null, "The same low-health episode consumed another escape scroll");
        preferences.AutoTown=false; preferences.AutoRandom=false;
        check(automation.NextItem(state,preferences,2301)==null,"地图屏蔽回城期间使用了卷轴");
        preferences.AutoTown=true;
        check(automation.NextItem(state,preferences,2302)==null,"地图保护开关重置了未恢复生命的回城触发，重复耗卷");
        preferences.AutoRandom=true;
        state.User.HP = 100; automation.NextItem(state, preferences, 2400); state.User.HP = 10;
        check(automation.NextItem(state, preferences, 2500) == town, "Recovering HP did not rearm emergency protection");
        automation.Reset(); preferences.AutoTown = false;
        check(automation.NextItem(state, preferences, 3000) == random && automation.PendingScrollShape == 2, "Random protection selected a different scroll shape");
        automation.Apply(new S.UseItem { UniqueID = 4, Grid = MirGridType.Inventory, Success = false }, 3100);
        check(automation.Paused && automation.NextItem(state, preferences, 7000, true) == null, "Rejected random teleport automatically retried");
        automation.Reset(); preferences.AutoRandom = false;
        check(automation.NextItem(state, preferences, 8000, true) == random, "No-monster random did not select its inventory scroll");
        automation.Apply(new S.UseItem { UniqueID = 4, Grid = MirGridType.Inventory, Success = true }, 8100);
        check(automation.NextItem(state, preferences, 9500, true) == null && automation.NextItem(state, preferences, 13000, true) == random, "No-monster random ignored its five-second consumption interval");
        state.User.MP = 100;
        var single = new ClientMagic { Spell = Spell.FireBall, BaseCost = 5 };
        var area = new ClientMagic { Spell = Spell.FireBang, BaseCost = 25 };
        state.User.Class = MirClass.Wizard; state.User.Magics = [single, area];
        var target = state.Actors[2];
        check(ClientAutomation.SkillFor(state, preferences, target, 20000) == single, "A single monster did not use the configured learned single-target skill");
        state.Apply(new S.ObjectMonster { ObjectID = 8, Location = new Point(12, 10), AI = 0 });
        check(ClientAutomation.SkillFor(state, preferences, target, 20000) == area, "Grouped monsters did not prefer the configured area skill");
        state.Apply(new S.ObjectMonster { ObjectID=9182, AI=3, Image=Monster.Hen, Location=new Point(11,11) });
        var tree=state.Actors[9182];
        check(ClientAutomation.RequiresPhysicalAttack(tree),"原TCP栗树AI没有触发物理攻击，不应依赖素材或世界编号");
        check(ClientAutomation.SkillFor(state,preferences,tree,20000)==null,"栗树仍选择无效群攻或单体法术而非普攻");
        check(ClientAutomation.Targets(state).Any(actor=>actor.Id==tree.Id),"栗树被直接跳过而非允许正常普攻");
        check(!ClientAutomation.RequiresPhysicalAttack(target),"普通怪物被错误禁止施法");
        check(!ClientAutomation.RequiresPhysicalAttack(new Actor{Kind=ObjectType.Player,AI=3}),"玩家对战错误沿用栗树限制");
        state.DisabledSkills.Add(area.Spell);
        check(ClientAutomation.SkillFor(state, preferences, target, 20000) == single, "关闭群攻后挂机仍使用该技能");
        state.DisabledSkills.Add(single.Spell);
        check(ClientAutomation.SkillFor(state, preferences, target, 20000) == null, "关闭全部技能后挂机未回退普攻");
        state.DisabledSkills.Clear();
        state.User.MP = 10;
        check(ClientAutomation.SkillFor(state, preferences, target, 20000) == single, "Insufficient area-skill mana did not fall back to single-target casting");
        single.CastTime = 19500; single.Delay = 1000;
        check(ClientAutomation.SkillFor(state, preferences, target, 20000) == null, "Automatic casting bypassed the learned skill cooldown");
        preferences.RandomPercent = -1; preferences.TownPercent = 999; preferences.Normalize();
        check(preferences.RandomPercent == 1 && preferences.TownPercent == 95 && !JsonSerializer.Serialize(preferences).Contains("SingleSkill"), "无效保护阈值或已移除的挂机技能设置仍被保存");
    }
}
