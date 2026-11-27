using System.Drawing;
using System.Reflection;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirNetwork;
using Server.MirObjects;
using S = ServerPackets;

internal static class HeroCombatChecks
{
    private sealed class Player : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
    }
    private static void Load(HeroObject actor, CharacterInfo info)
    { actor.Info = info; actor.HInfo = (HeroInfo)info; info.Mount = new MountInfo(actor); actor.Stats = new Stats(); }
    private sealed class Warrior(HeroInfo info, Player owner) : WarriorHero(info, owner)
    { protected override void Load(CharacterInfo info, MirConnection connection) => HeroCombatChecks.Load(this, info); }
    private sealed class Wizard(HeroInfo info, Player owner) : WizardHero(info, owner)
    {
        public Point? Destination;
        protected override void Load(CharacterInfo info, MirConnection connection) => HeroCombatChecks.Load(this, info);
        protected override void MoveTo(Point location) => Destination = location;
    }
    private sealed class Taoist(HeroInfo info, Player owner) : TaoistHero(info, owner)
    {
        protected override void Load(CharacterInfo info, MirConnection connection) => HeroCombatChecks.Load(this, info);
        public UserItem? Material(int count, short shape) => shape is 1 or 2 ? GetPoison(count, (byte)shape) : GetAmulet(count, shape);
    }
    private sealed class Assassin(HeroInfo info, Player owner) : AssassinHero(info, owner)
    { protected override void Load(CharacterInfo info, MirConnection connection) => HeroCombatChecks.Load(this, info); }
    private sealed class Archer(HeroInfo info, Player owner) : ArcherHero(info, owner)
    { protected override void Load(CharacterInfo info, MirConnection connection) => HeroCombatChecks.Load(this, info); }
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Act(HeroObject hero, string method) => hero.GetType().GetMethod(method, Hidden)!.Invoke(hero, null);
    private static Spell Pending(HeroObject hero) => (Spell)typeof(HeroObject).GetField("NextMagicSpell", Hidden)!.GetValue(hero)!;
    private static Spell Plan(HeroObject hero)
    {
        typeof(HeroObject).GetField("NextMagicSpell", Hidden)!.SetValue(hero, Spell.None);
        Act(hero, "ProcessAI"); return Pending(hero);
    }
    private static UserMagic Learn(HeroObject hero, Spell spell, int cost = 5)
    {
        var magic = new UserMagic(spell) { CastTime = -100000, Key = 0,
            Info = new MagicInfo { Name = spell.ToString(), Spell = spell, BaseCost = (byte)cost, DelayBase = 1800, Range = 9, PowerBase = 10 } };
        hero.Info.Magics.Add(magic); return magic;
    }
    public static void Run(Action<bool, string> check)
    {
        var map = new Map(new MapInfo { Index = -30 }) { Width = 25, Height = 25, Cells = new Cell[25, 25] };
        for (var y = 0; y < map.Height; y++) for (var x = 0; x < map.Width; x++) map.Cells[x, y] = new Cell { Attribute = CellAttribute.Walk };
        void Place(MapObject actor, Point point)
        { actor.CurrentMap = map; actor.CurrentLocation = point; actor.Node = new LinkedListNode<MapObject>(actor); map.GetCell(point).Add(actor); }
        var owner = new Player { Info = new CharacterInfo { HP = 1000, MP = 1000, Level = 50, PMode = PetMode.Both, HeroBehaviour = HeroBehaviour.Attack }, Stats = new Stats { [Stat.HP] = 1000, [Stat.MP] = 1000 } };
        owner.Info.Mount = new MountInfo(owner); Place(owner, new(9, 10)); map.Players.Add(owner);
        HeroObject Hero(MirClass job)
        {
            if (owner.Hero != null) { map.GetCell(owner.Hero.CurrentLocation).Remove(owner.Hero); owner.Hero.Dead = true; }
            var info = new HeroInfo(new ClientPackets.NewHero { Class = job, Name = "组合检查" }) { HP = 1000, MP = 100, Level = 50, Inventory = [] };
            HeroObject hero = job switch { MirClass.Warrior => new Warrior(info, owner), MirClass.Wizard => new Wizard(info, owner),
                MirClass.Taoist => new Taoist(info, owner), MirClass.Assassin => new Assassin(info, owner), _ => new Archer(info, owner) };
            hero.Stats = new Stats { [Stat.HP] = 1000, [Stat.MP] = 1000, [Stat.MinDC] = 100, [Stat.MaxDC] = 100,
                [Stat.MinMC] = 100, [Stat.MaxMC] = 100, [Stat.MinSC] = 100, [Stat.MaxSC] = 100, [Stat.Accuracy] = 100, [Stat.Luck] = 100 };
            owner.Hero = hero; Place(hero, new(10, 10)); return hero;
        }
        var target = new MonsterObject(new MonsterInfo { AI = 0, Level = 1 }) { HP = 10000, Stats = new Stats { [Stat.HP] = 10000 }, DamageRate = 1, ArmourRate = 1 };
        Place(target, new(14, 10));
        var wizard = Hero(MirClass.Wizard);
        foreach(var direction in Enum.GetValues<MirDirection>().Where(direction=>(int)direction<8))
        {
            owner.Direction=direction;((Wizard)wizard).Destination=null;Act(wizard,"ProcessStacking");Act(wizard,"ProcessRoam");
            check(((Wizard)wizard).Destination==null&&wizard.CurrentLocation==new Point(10,10),"范围内英雄仍追逐主人身后固定格，主人转向就反复调整站位");
        }
        wizard.CurrentLocation=new(12,13);Act(wizard,"ProcessRoam");
        check(((Wizard)wizard).Destination==null,"英雄没有保留三格范围的对角边界");
        owner.Direction=MirDirection.Right;wizard.CurrentLocation=new(13,10);((Wizard)wizard).Destination=null;Act(wizard,"ProcessRoam");
        check(((Wizard)wizard).Destination==owner.CurrentLocation,"超出跟随范围没有追向主人的当前格位");
        wizard.CurrentLocation=new(10,10);((Wizard)wizard).Destination=null;
        wizard.Target = target;
        var shield = Learn(wizard, Spell.MagicShield); var fire = Learn(wizard, Spell.FireBall);
        check(Plan(wizard) == Spell.MagicShield, "英雄未绑快捷键的已学技能没有自动施放");
        shield.CastTime = Envir.Main.Time;
        check(Plan(wizard) == Spell.FireBall, "英雄高优先技能冷却挡住就绪攻击技能");
        Act(wizard, "ProcessTarget");
        foreach (var action in wizard.ActionList.ToArray()) { wizard.ActionList.Remove(action); wizard.Process(action); }
        check(wizard.MP == 95 && target.HP < 10000 && fire.CastTime == Envir.Main.Time, "英雄组合没有经原Magic实际扣蓝、扣血与记冷却");
        wizard.ActionTime = wizard.AttackTime = wizard.SpellTime = 0;
        check(Plan(wizard) == Spell.None, "冷却期间重复选择同一远程技能"); Act(wizard, "ProcessTarget");
        check(((Wizard)wizard).Destination == null && wizard.CurrentLocation == new Point(10, 10), "远程冷却期间英雄错误贴近敌人");
        wizard.MP = 0; Plan(wizard); Act(wizard, "ProcessTarget");
        check(((Wizard)wizard).Destination == target.CurrentLocation, "英雄缺蓝不能回退追近普攻");
        wizard.MP = 100; shield.CastTime = fire.CastTime = -100000; wizard.Info.Magics.Remove(shield);
        target.Info.AI = 3;
        check(Plan(wizard) == Spell.None, "英雄对仅接受普攻的树目标仍消耗法术"); target.Info.AI = 0;
        fire.Info.Range = 2;
        check(Plan(wizard) == Spell.None, "英雄技能选择忽略当前版本实际射程"); fire.Info.Range = 9;
        map.Cells[12, 10].Attribute = CellAttribute.HighWall;
        check(Plan(wizard) == Spell.None, "英雄隔墙反复发射无效法术"); map.Cells[12, 10].Attribute = CellAttribute.Walk;
        owner.PMode = PetMode.MoveOnly;
        check(Plan(wizard) == Spell.None, "跟随模式英雄仍自动攻击"); owner.PMode = PetMode.Both;

        void Move(MapObject actor, Point location) { map.GetCell(actor.CurrentLocation).Remove(actor); actor.CurrentLocation = location; map.GetCell(location).Add(actor); }
        MonsterObject Enemy(Point location)
        {
            var enemy = new MonsterObject(new MonsterInfo { AI = 0, Level = 1 }) { HP = 10000, Stats = new Stats { [Stat.HP] = 10000 }, DamageRate = 1, ArmourRate = 1 };
            Place(enemy, location); return enemy;
        }
        var extra = Enemy(new(14, 11)); var storm = Learn(wizard, Spell.IceStorm);
        check(Plan(wizard) == Spell.IceStorm, "法师英雄多怪未选择群攻"); extra.Dead = true;
        check(Plan(wizard) == Spell.FireBall, "法师英雄单怪浪费群攻");
        Learn(wizard, Spell.Vampirism); wizard.HP = wizard.MaxHealth * 2 / 5;
        var lowHealthSpell = Plan(wizard);
        check(lowHealthSpell == Spell.Vampirism, $"法师英雄低血未优先吸血：{lowHealthSpell}, HP={wizard.HP}/{wizard.MaxHealth}, MP={wizard.MP}");

        var tao = Hero(MirClass.Taoist); tao.Target = target;
        var soul = Learn(tao, Spell.SoulFireBall); Learn(tao, Spell.Poisoning);
        check(Plan(tao) == Spell.None, "道士英雄缺耗材仍安排火符/施毒");
        var amulet = new UserItem(new ItemInfo { Type = ItemType.Amulet, Shape = 0 }) { Count = 50, UniqueID = 91001 };
        var poison = new UserItem(new ItemInfo { Type = ItemType.Amulet, Shape = 2 }) { Count = 50, UniqueID = 91002 };
        tao.Info.Equipment[5] = amulet; tao.Info.Equipment[6] = poison;
        check(Plan(tao) == Spell.Poisoning, "道士英雄没有先补目标缺失毒");
        target.PoisonList.Add(new Poison { PType = PoisonType.Red, Owner = tao, Duration = 50, TickSpeed = 1000 });
        check(Plan(tao) == Spell.SoulFireBall, "道士英雄对已有同色毒目标重复消耗");
        SharedMaterials(check, (Taoist)tao, owner, target);
        tao.Target = null; owner.HP = 400; var heal = Learn(tao, Spell.Healing); tao.AttackTime = long.MaxValue;
        check(Plan(tao) == Spell.Healing, "道士英雄无敌人时无法治疗主人");
        Act(tao, "ProcessTarget"); foreach (var action in tao.ActionList.ToArray()) { tao.ActionList.Remove(action); tao.Process(action); }
        check(owner.HealAmount > 0 && tao.MP == 95 && Pending(tao) == Spell.None, $"英雄治疗被普攻计时拦住或未走原治疗：Heal={owner.HealAmount}, MP={tao.MP}, pending={Pending(tao)}");
        owner.HP = 1000; owner.HealAmount = 0; tao.HP = 400; heal.CastTime = Envir.Main.Time;
        check(Plan(tao) == Spell.None, "英雄持续治疗没有至少5秒重施间隔"); tao.HP = 1000;

        Move(target, new(11, 10)); target.PoisonList.Clear();
        var warrior = Hero(MirClass.Warrior); warrior.Target = target; warrior.Slaying = true;
        Learn(warrior, Spell.FlamingSword); Learn(warrior, Spell.TwinDrakeBlade); Learn(warrior, Spell.Slaying);
        Plan(warrior); owner.Packets.Clear(); Act(warrior, "ProcessTarget");
        check(owner.Packets.OfType<S.ObjectAttack>().Last().Spell == Spell.FlamingSword && warrior.MP == 95, "战士英雄已有攻杀抢占烈火或烈火未实际扣蓝");
        warrior.ActionTime = warrior.AttackTime = 0; warrior.MP = 9; warrior.FlamingSword = false; warrior.Slaying = false; Plan(warrior);
        check(!warrior.TwinDrakeBlade && warrior.MP == 9, "双龙斩准备未预留出刀蓝量");
        warrior.MP = 10; Plan(warrior); owner.Packets.Clear(); Act(warrior, "ProcessTarget");
        check(owner.Packets.OfType<S.ObjectAttack>().Last().Spell == Spell.TwinDrakeBlade && warrior.MP == 0, "战士英雄双龙斩两段没有实际扣蓝");

        var assassin = Hero(MirClass.Assassin); assassin.Target = target; Learn(assassin, Spell.DoubleSlash); Learn(assassin, Spell.CrescentSlash); Learn(assassin, Spell.HeavenlySword);
        check(Plan(assassin) == Spell.None, "刺客英雄单怪空放范围斩击"); owner.Packets.Clear(); Act(assassin, "ProcessTarget");
        check(owner.Packets.OfType<S.ObjectAttack>().Last().Spell == Spell.DoubleSlash && assassin.MP == 95, "刺客英雄未自动双连斩");
        assassin.ActionTime = assassin.AttackTime = assassin.SpellTime = 0; Move(extra, new(11, 11)); extra.Dead = false;
        check(Plan(assassin) == Spell.CrescentSlash, "刺客英雄前方多怪未选择扇区斩击"); extra.Dead = true;
        Move(target, new(12, 10)); check(Plan(assassin) == Spell.HeavenlySword, "刺客英雄隔位直线未使用神圣剑术");
        Move(target, new(12, 11)); check(Plan(assassin) == Spell.None, "刺客英雄对斜偏目标空放直线技能");
        assassin.Info.Magics.Clear(); Learn(assassin, Spell.PoisonSword); Move(target, new(11, 10)); target.PoisonList.Clear();
        assassin.ActionTime = assassin.AttackTime = assassin.SpellTime = 0; assassin.MP = 100;
        var sharedPoison = owner.Info.Inventory[6] = new UserItem(new ItemInfo { Type = ItemType.Amulet, Shape = 1,
            RequiredClass = (RequiredClass)31, RequiredGender = RequiredGender.Male | RequiredGender.Female }) { UniqueID = 93001, Count = 1 };
        check(Plan(assassin) == Spell.PoisonSword, "刺客英雄没有使用主人背包毒粉安排毒剑");
        Act(assassin, "ProcessTarget"); foreach (var action in assassin.ActionList.ToArray()) { assassin.ActionList.Remove(action); assassin.Process(action); }
        check(owner.Info.Inventory[6] == null && sharedPoison.Count == 0 && assassin.MP == 95 && target.PoisonList.Any(p => p.PType.HasFlag(PoisonType.Green)), "刺客毒剑未通过原技能精确消耗主人毒粉并中毒");
        Move(target, new(12, 11));

        var archer = Hero(MirClass.Archer); archer.Target = target;
        archer.Info.Equipment[0] = new UserItem(new ItemInfo { Type = ItemType.Weapon, Shape = 200 }) { CurrentDura = 1000 };
        Learn(archer, Spell.DoubleShot); Learn(archer, Spell.NapalmShot); Learn(archer, Spell.VampireShot);
        check(Plan(archer) == Spell.DoubleShot, "弓箭英雄单怪未优先双连射");
        extra.Dead = false; Move(extra, new(12, 12)); check(Plan(archer) == Spell.NapalmShot, "弓箭英雄多怪未选择燃烧箭");
        archer.HP = 400; check(Plan(archer) == Spell.VampireShot, "弓箭英雄低血未优先吸血箭");
        archer.HP = 1000; extra.Dead = true; Learn(archer, Spell.ElementalBarrier);
        check(Plan(archer) == Spell.DoubleShot, "弓箭英雄没有冥想仍反复尝试聚集元素"); Learn(archer, Spell.Meditation);
        check(Plan(archer) == Spell.ElementalBarrier, "弓箭英雄没有自动维护元素屏障");
        archer.Info.Equipment[0].CurrentDura = 0; check(Plan(archer) == Spell.None, "弓箭英雄损坏武器仍使用弓系技能");
    }

    private static void SharedMaterials(Action<bool, string> check, Taoist hero, Player owner, MonsterObject target)
    {
        hero.Info.Equipment[5] = hero.Info.Equipment[6] = null;
        UserItem Material(short shape, ulong id, ushort count) => new(new ItemInfo { Type = ItemType.Amulet, Shape = shape,
            RequiredClass = (RequiredClass)31, RequiredGender = RequiredGender.Male | RequiredGender.Female }) { UniqueID = id, Count = count };
        var amulet = owner.Info.Inventory[6] = Material(0, 92001, 2);
        var red = owner.Info.Inventory[7] = Material(2, 92002, 1);
        var green = owner.Info.Inventory[8] = Material(1, 92003, 1);
        target.PoisonList.Clear(); target.HP = 10000;
        void Ready() { hero.ActionTime = hero.AttackTime = hero.SpellTime = 0; foreach (var magic in hero.Info.Magics) magic.CastTime = -100000; }
        void Cast()
        {
            Act(hero, "ProcessTarget");
            foreach (var action in hero.ActionList.ToArray()) { hero.ActionList.Remove(action); hero.Process(action); }
        }
        Ready(); check(Plan(hero) == Spell.Poisoning, "英雄只有共用包红毒时没有安排施毒"); Cast();
        check(owner.Info.Inventory[7] == null && red.Count == 0 && target.PoisonList.Any(p => p.PType.HasFlag(PoisonType.Red)) &&
            owner.Packets.OfType<S.DeleteItem>().Any(p => p.UniqueID == red.UniqueID && p.Count == 1), "英雄未从共用包精确消耗最后红毒/原中毒回执");
        Ready(); check(Plan(hero) == Spell.Poisoning, "已有红毒时英雄未从共用包选择绿毒"); Cast();
        check(owner.Info.Inventory[8] == null && green.Count == 0 && target.PoisonList.Any(p => p.PType.HasFlag(PoisonType.Green)), "共用包绿毒未消耗/生效或没有清空末格");
        Ready(); var mana = hero.MP;
        check(Plan(hero) == Spell.SoulFireBall, "双毒后英雄未使用共用包普通护符"); Cast();
        check(amulet.Count == 1 && hero.MP == mana - 5 && target.HP < 10000 && hero.Info.Inventory.Length == 0, "共用护符未扣次数/蓝量/真实伤害或重建了英雄背包");
        Ready(); Plan(hero); Cast(); check(owner.Info.Inventory[6] == null && amulet.Count == 0, "最后一符消耗后主人背包留零数量幽灵物品");
        var reserve = owner.Info.Inventory[6] = Material(0, 92004, 4); owner.Info.Inventory[7] = Material(0, 92005, 4);
        check(hero.Material(5, 0) == null, "需要5符时错误跨包合计了两个4符包");
        reserve.Count = 5; check(ReferenceEquals(hero.Material(5, 0), reserve), "共用包单包5符无法满足召唤");
        var guards = new Action<UserItem>[] { item => item.Info.Bind = BindMode.NoHero, item => item.SoulBoundId = owner.Info.Index + 1,
            item => item.Info.RequiredClass = RequiredClass.Warrior, item => item.Info.RequiredAmount = 100,
            item => item.RentalInformation = new RentalInformation { BindingFlags = BindMode.DontTrade }, item => item.SealedInfo = new SealedInfo(),
            item => item.ExpireInfo = new ExpireInfo { ExpiryDate = DateTime.MinValue } };
        owner.Info.Inventory[7] = null;
        foreach (var restrict in guards)
        { var item = Material(0, 92006, 5); restrict(item); owner.Info.Inventory[6] = item; check(hero.Material(5, 0) == null && item.Count == 5, "英雄共享毒符绕过职业/等级/绑定/租赁/封印/到期校验"); }
        owner.Info.Inventory[6] = reserve; owner.TradePartner = new Player(); check(hero.Material(5, 0) == null, "主人交易中仍共享消耗毒符"); owner.TradePartner = null;
        reserve.SoulBoundId = owner.Info.Index; check(ReferenceEquals(hero.Material(5, 0), reserve), "主人本人绑定的共用毒符被误判成英雄编号绑定");
        Array.Clear(owner.Info.Inventory); Ready(); hero.MP = 100; target.PoisonList.Clear();
    }
}
