using System.Text.Json;
using Crystal.Native;
using Point = System.Drawing.Point;
using S = ServerPackets;

internal static class ClassCombatChecks
{
    public static void Run(Action<bool, string> check)
    {
        var preferences = new ClientPreferences();
        check(preferences.SmartBattle && !preferences.AutoHp && !preferences.AutoRandom && !preferences.AutoTown, "默认组合错误开启资产保护");
        check(!JsonSerializer.Serialize(preferences).Contains("SingleSkill") && !JsonSerializer.Serialize(preferences).Contains("AreaSkill"), "挂机仍保存独立技能选择");
        var state = new GameState { BaseStats = new BaseStats(MirClass.Warrior) { Stats = [new BaseStat(Stat.HP) { Base = 100 }, new BaseStat(Stat.MP) { Base = 100 }] } };
        state.Apply(new S.UserInformation { ObjectID = 1, Class = MirClass.Warrior, HP = 100, MP = 100, Location = new Point(10, 10),
            Inventory = new UserItem[12], Equipment = new UserItem[14], QuestInventory = [] });
        var target = new Actor { Id = 2, Kind = ObjectType.Monster, Location = new Point(11, 10) }; state.Actors[2] = target;
        const long now = 50000;
        ClientMagic Learn(Spell spell) { var magic = new ClientMagic { Spell = spell, BaseCost = 10, CastTime = 0 }; state.User!.Magics.Add(magic); return magic; }
        foreach (var spell in new[] { Spell.Slaying, Spell.FlamingSword, Spell.TwinDrakeBlade, Spell.Thrusting }) Learn(spell);
        state.Apply(new S.SpellToggle { ObjectID = 1, Spell = Spell.Slaying, CanUse = true });
        check(ClassSkillRules.Melee(state, MirDirection.Right, Spell.None, false) == Spell.Slaying, "挂机普通攻击跳过服务端准备好的攻杀");
        state.User!.MP = 0;
        check(ClassSkillRules.Melee(state, MirDirection.Right, Spell.None, false) == Spell.Slaying, "无蓝时被动攻杀被错误禁用");
        state.User.MP = 100; state.ActiveSkills.Add(Spell.FlamingSword);
        check(ClassSkillRules.Melee(state, MirDirection.Right, Spell.None, true) == Spell.FlamingSword, "挂机没有优先消费已准备烈火");
        state.ActiveSkills.Remove(Spell.FlamingSword); state.ActiveSkills.Add(Spell.TwinDrakeBlade);
        check(ClassSkillRules.Melee(state, MirDirection.Right, Spell.None, true) == Spell.TwinDrakeBlade, "挂机遗漏已准备双龙斩");
        target.AI = 3;
        check(ClassSkillRules.Melee(state, MirDirection.Right, Spell.None, true) == Spell.None && ClientAutomation.SkillFor(state, preferences, target, now) == null, "栗树没有回退原普攻");
        target.AI = 0; target.Location = new Point(12, 10);
        preferences.SmartBattle = false; state.ActiveSkills.Add(Spell.Thrusting); state.User.MP = 0;
        check(ClassSkillRules.Thrusting(state, preferences, target, false, now) && ClassSkillRules.Melee(state, MirDirection.Right, Spell.Thrusting, false) == Spell.Thrusting,
            "手动开启刺杀后仍要求额外智能开关或魔力");
        state.DisabledSkills.Add(Spell.Thrusting);
        check(!ClassSkillRules.Thrusting(state, preferences, target, false, now), "关闭刺杀后仍自动隔位攻击"); state.DisabledSkills.Clear();
        state.User.MP = 100; preferences.SmartBattle = true;
        check(ClassSkillRules.Melee(state, MirDirection.Right, Spell.Thrusting, true) == Spell.Thrusting, "隔位刺杀被近战蓄力覆盖造成空砍");
        state.ActiveSkills.Clear(); target.Location = new Point(11, 10);
        check(ClassSkillRules.Next(state, preferences, target, false, now) == Spell.FlamingSword && ClassSkillRules.Next(state, preferences, target, true, now) == Spell.FlamingSword, "手动与挂机战斗未共用烈火组合");
        state.DisabledSkills.Add(Spell.FlamingSword); state.User.MP = 11;
        check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.None, "双龙斩未预留出刀耗蓝，准备后无法使用");
        target.Location = new Point(12, 10);
        check(ClassSkillRules.Thrusting(state, preferences, target, true, now), "低蓝双龙斩仍阻止隔位刺杀");
        state.User.MP = 20; target.Location = new Point(11, 10);
        check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.TwinDrakeBlade, "双龙斩两段耗蓝恰好足够却未准备");
        state.DisabledSkills.Clear(); state.User.MP = 100;
        state.User.Magics.Clear(); state.User.Class = MirClass.Wizard;
        state.Apply(new S.ObjectPlayer { ObjectID = 99, Name = state.User.Name, Class = MirClass.Wizard, Gender = state.User.Gender });
        check(ClassSkillRules.HasMirror(state) && ClassSkillRules.Confirmed(state, Spell.Mirroring, 0, 0, 100), "原同名ObjectPlayer镜像没有被识别");
        state.Actors[99].Name = "其他法师";
        check(!ClassSkillRules.HasMirror(state), "其他玩家被误认为自己的镜像"); state.Actors.Remove(99);
        var bolt = Learn(Spell.ThunderBolt); Learn(Spell.FireBall); var storm = Learn(Spell.IceStorm); Learn(Spell.MagicShield);
        check(ClientAutomation.SkillFor(state, preferences, target, now) == bolt, "法师没有自动选择已学单体主攻");
        state.Actors[3] = new Actor { Id = 3, Kind = ObjectType.Monster, Location = new Point(12, 10) };
        check(ClientAutomation.SkillFor(state, preferences, target, now) == storm, "法师多怪没有自动切群攻");
        state.DisabledSkills.Add(Spell.IceStorm); state.DisabledSkills.Add(Spell.FireBall); bolt.CastTime = now - 100; bolt.Delay = 1000;
        target.Location = new Point(15, 10);
        check(ClientAutomation.SkillFor(state, preferences, target, now) == null && ClassSkillRules.WaitForRanged(state, preferences, target, now), "远程技能短冷却期间继续贴近怪物");
        state.User.MP = 0;
        check(!ClassSkillRules.WaitForRanged(state, preferences, target, now), "缺蓝无法回退普攻"); state.User.MP = 100;
        check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.MagicShield, "法师挂机未维护魔法盾");
        preferences.SmartBattle = false;
        check(ClientAutomation.SkillFor(state, preferences, target, now) == null && ClassSkillRules.Next(state, preferences, target, true, now) == Spell.None, "关闭职业组合仍自动放法术");
        preferences.SmartBattle = true; state.User.Magics.Clear(); state.DisabledSkills.Clear(); state.Actors.Remove(3); state.User.Class = MirClass.Taoist;
        var soul = Learn(Spell.SoulFireBall); Learn(Spell.Poisoning); Learn(Spell.Healing); Learn(Spell.SummonSkeleton);
        check(ClientAutomation.SkillFor(state, preferences, target, now) == null, "缺符时挂机仍发送无效火符");
        var amulet = new UserItem(new ItemInfo { Type = ItemType.Amulet, Shape = 0, StackSize = 100 }) { Count = 50 }; state.User.Inventory[0] = amulet;
        check(ClientAutomation.SkillFor(state, preferences, target, now) == soul, "道士无法从背包自动准备护符主攻");
        state.User.HP = 60;
        check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.Healing, "道士挂机低血未优先治愈");
        state.User.HP = 100;
        check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.SummonSkeleton, "道士挂机没有补召唤兽");
        var pet = new Actor { Id = 3, Kind = ObjectType.Monster, MasterObjectId = 1, Location = new Point(10, 11), Image = (int)Monster.BoneFamiliar }; state.Actors[3] = pet;
        Learn(Spell.PetEnhancer);
        check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.PetEnhancer && ClassSkillRules.PetToEnhance(state) == pet, "宠物强化没有选择本人的未强化宠物");
        pet.Buffs.Add(BuffType.PetEnhancer);
        check(ClassSkillRules.Confirmed(state, Spell.PetEnhancer, 3, 0, 100) && ClassSkillRules.PetToEnhance(state) == null, "宠物强化忽略真实Buff回执");
        foreach (short shape in new short[] { 1, 2 }) state.User.Inventory[shape] = new UserItem(new ItemInfo { Type = ItemType.Amulet, Shape = shape, StackSize = 100 }) { Count = 20 };
        Learn(Spell.SoulShield);
        check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.Poisoning, "道士召唤后未补双毒");
        check(ClassSkillRules.Next(state, preferences, target, false, now) == Spell.Poisoning, "手动打怪没有先毒后增益");
        target.Poison = PoisonType.Red | PoisonType.Green;
        check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.SoulShield, "双毒齐全后未补缺失增益");
        state.UserBuffs[BuffType.SoulShield] = new ClientBuff { Type = BuffType.SoulShield };
        check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.None && ClientAutomation.SkillFor(state, preferences, target, now) == soul, "毒/增益齐全后没有回到火符攻击");
        state.StorageActive = true;
        check(ClassSkillRules.Next(state, preferences, target, true, now) == Spell.None, "仓库操作期间职业组合仍施法");
        Roaming(check);
        FiveClasses(check);
    }

    private static void FiveClasses(Action<bool,string> check)
    {
        var state=new GameState{BaseStats=new BaseStats(MirClass.Assassin){Stats=[new BaseStat(Stat.HP){Base=100},new BaseStat(Stat.MP){Base=100}]}};
        state.Apply(new S.UserInformation{ObjectID=1,Class=MirClass.Assassin,HP=100,MP=100,Location=new(10,10),Inventory=new UserItem[12],Equipment=new UserItem[14],QuestInventory=[]});
        var preferences=new ClientPreferences();const long now=100000;
        var target=new Actor{Id=2,Kind=ObjectType.Monster,Location=new(11,10)};state.Actors[2]=target;
        ClientMagic Learn(Spell spell){var magic=new ClientMagic{Spell=spell,BaseCost=10};state.User!.Magics.Add(magic);return magic;}
        foreach(var job in Enum.GetValues<MirClass>())check(ClassSkillRules.Maintained(job).Length>0,$"{job}缺少职业辅助选项");
        foreach(var spell in new[]{Spell.Haste,Spell.LightBody,Spell.DoubleSlash,Spell.CrescentSlash,Spell.HeavenlySword})Learn(spell);
        check(ClassSkillRules.Next(state,preferences,target,true,now)==Spell.Haste,"刺客没有使用本职业增益");
        foreach(var spell in ClassSkillRules.Maintained(MirClass.Assassin))state.UserBuffs[ClassSkillRules.Buff(spell)]=new ClientBuff{Type=ClassSkillRules.Buff(spell)};
        check(ClassSkillRules.Next(state,preferences,target,true,now)==Spell.None && ClassSkillRules.Confirmed(state,Spell.Haste,1,0,100),"刺客重复补已有增益或不识别真实Buff");
        check(ClassSkillRules.Melee(state,MirDirection.Right,Spell.None,true)==Spell.DoubleSlash,"刺客智能组合没有使用双重斩");
        preferences.SmartBattle=false;
        check(ClassSkillRules.Next(state,preferences,target,true,now)==Spell.None && ClassSkillRules.AttackMagic(state,preferences,target,now)==null,"关闭刺客组合仍自动施法");
        preferences.SmartBattle=true;
        state.Actors[3]=new Actor{Id=3,Kind=ObjectType.Monster,Location=new(11,11)};
        check(ClassSkillRules.AttackMagic(state,preferences,target,now)?.Spell==Spell.CrescentSlash,"刺客近身群怪没有选择前方扇区斩击");
        target.Location=new(12,10);state.Actors[3].Location=new(13,10);
        check(ClassSkillRules.AttackMagic(state,preferences,target,now)?.Spell==Spell.HeavenlySword,"刺客直线三格没有选择神圣剑术");
        state.Actors[3].Location=new(12,11);
        check(ClassSkillRules.AttackMagic(state,preferences,target,now)==null,"刺客将非直线范围内的怪误算为神圣剑术命中");
        state.Actors.Remove(3);state.User!.Magics.Clear();state.UserBuffs.Clear();state.User.Class=MirClass.Archer;
        foreach(var spell in new[]{Spell.ElementalBarrier,Spell.Concentration,Spell.Meditation,Spell.StraightShot,Spell.DoubleShot,Spell.NapalmShot,Spell.VampireShot})Learn(spell);
        check(ClassSkillRules.Next(state,preferences,target,true,now)==Spell.ElementalBarrier,"弓箭手没有优先准备护盾");
        check(!ClassSkillRules.Confirmed(state,Spell.ElementalBarrier,1,0,100,0),"没有元素或盾回执即确认成功");
        state.Apply(new S.SetElemental{ObjectID=1,Enabled=true,ElementType=1,ExpLast=100});
        check(ClassSkillRules.Confirmed(state,Spell.ElementalBarrier,1,0,100,0) && !ClassSkillRules.Confirmed(state,Spell.ElementalBarrier,1,0,100,1),"元素屏障两阶段确认接受了旧元素");
        state.UserBuffs[BuffType.ElementalBarrier]=new ClientBuff{Type=BuffType.ElementalBarrier};
        check(ClassSkillRules.Next(state,preferences,target,true,now)==Spell.Concentration,"弓箭手护盾后没有补凝神");
        state.UserBuffs[BuffType.Concentration]=new ClientBuff{Type=BuffType.Concentration};
        check(ClassSkillRules.Next(state,preferences,target,true,now)==Spell.None && ClassSkillRules.AttackMagic(state,preferences,target,now)?.Spell==Spell.DoubleShot,"弓箭手已齐增益后未开始远程攻击");
        state.Actors[3]=new Actor{Id=3,Kind=ObjectType.Monster,Location=new(13,10)};
        check(ClassSkillRules.AttackMagic(state,preferences,target,now)?.Spell==Spell.NapalmShot,"弓箭手群怪没有选择燃烧箭");
        state.User.HP=50;
        check(ClassSkillRules.AttackMagic(state,preferences,target,now)?.Spell==Spell.VampireShot,"弓箭手低血没有优先吸血箭");
        state.User.MP=0;
        check(ClassSkillRules.AttackMagic(state,preferences,target,now)==null && !ClassSkillRules.WaitForRanged(state,preferences,target,now),"弓箭手缺蓝不能回退普通攻击");
        state.User.MP=100;target.AI=3;
        check(ClassSkillRules.AttackMagic(state,preferences,target,now)==null && ClassSkillRules.Melee(state,MirDirection.Right,Spell.None,true)==Spell.None,"五职业组合绕过原Tree仅普攻规则");
    }

    private static void Roaming(Action<bool, string> check)
    {
        var roam = new ClientRoaming(); var point = new Point(400, 400); var destinations = new List<Point>();
        for (var step = 0; step < 16; step++)
        {
            var goal = roam.Next(1, point, 800, 800, step * 60000, _ => true, _ => true);
            check(goal.HasValue && Functions.MaxDistance(point, goal.Value) >= 32, "大地图挂机只在附近短距离折返");
            var previous = point; point = goal!.Value; destinations.Add(point);
            for (var i = 1; i <= 16; i++) roam.Observe(1, new Point(previous.X + (point.X - previous.X) * i / 16, previous.Y + (point.Y - previous.Y) * i / 16));
        }
        check(destinations.Distinct().Count() >= 14 && destinations.Max(p => p.X) - destinations.Min(p => p.X) > 200, "探索没有离开原区域或反复选同一目的地");
        var attempts = 0;
        check(roam.Next(2, new Point(4, 4), 12, 12, 1000000, _ => true, _ => { attempts++; return false; }) == null && attempts <= 8, "不可达小地图的寻路尝试无界");
        check(roam.Next(3, new Point(4, 4), 12, 12, 1000001, _ => true, p => p.X >= 0 && p.X < 12 && p.Y >= 0 && p.Y < 12) != null, "切图保留旧地图排除或目标越界");
    }
}
