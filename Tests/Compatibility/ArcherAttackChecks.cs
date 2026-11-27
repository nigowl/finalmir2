using System.Drawing;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirNetwork;
using Server.MirObjects;

internal static class ArcherAttackChecks
{
    private sealed class Player : PlayerObject
    {
        public override void Enqueue(Packet packet) { }
        public override void Broadcast(Packet packet) { }
    }
    private sealed class Hero(CharacterInfo info, PlayerObject owner) : HeroObject(info, owner)
    {
        protected override void Load(CharacterInfo info, MirConnection connection)
        { Info = info; HInfo = (HeroInfo)info; info.Mount = new MountInfo(this); }
        public override void Enqueue(Packet packet) { }
        public override void Broadcast(Packet packet) { }
    }

    internal static void Run(Action<bool, string> check)
    {
        var original = Settings.RangeAccuracyBonus; var rate = Settings.PlayerPhysicalDamageRate;
        try
        {
            Settings.RangeAccuracyBonus = 100; Settings.PlayerPhysicalDamageRate = 1;
            var map = new Map(new MapInfo()) { Width = 30, Height = 20, Cells = new Cell[30, 20] };
            for (var x = 0; x < 30; x++) for (var y = 0; y < 20; y++) map.Cells[x, y] = new Cell { Attribute = CellAttribute.Walk };
            void Place(MapObject actor, Point point)
            { actor.CurrentMap = map; actor.CurrentLocation = point; actor.Node = new LinkedListNode<MapObject>(actor); map.GetCell(point).Add(actor); }
            var owner = new Player { Info = new CharacterInfo { HP = 10000, MP = 1000, Level = 40, Class = MirClass.Archer, PMode = PetMode.Both }, Account = new AccountInfo() };
            owner.Info.Mount = new MountInfo(owner); Place(owner, new(10, 10));
            var hero = new Hero(new HeroInfo(new ClientPackets.NewHero { Class = MirClass.Archer }) { HP = 10000, MP = 1000, Level = 40 }, owner);
            Place(hero, new(10, 11));
            foreach (var actor in new HumanObject[] { owner, hero })
            {
                actor.Stats = new Stats { [Stat.HP] = 10000, [Stat.MP] = 1000, [Stat.MinDC] = 100, [Stat.MaxDC] = 100,
                    [Stat.MinMC] = 1, [Stat.MaxMC] = 1, [Stat.Accuracy] = 10000, [Stat.Luck] = 100 };
                actor.Info.Equipment[0] = new UserItem(new ItemInfo { Type = ItemType.Weapon, Shape = (short)(Globals.ClassWeaponCount * 2), Durability = 10000 })
                    { CurrentDura = 10000, MaxDura = 10000 };
                var target = new MonsterObject(new MonsterInfo { AI = 0 }) { HP = 10000, Stats = new Stats { [Stat.HP] = 10000 }, ArmourRate = 1, DamageRate = 1 };
                Place(target, new(19, actor.CurrentLocation.Y));
                void Shoot(Point? requested = null)
                {
                    actor.RangeAttack(MirDirection.Right, requested ?? target.CurrentLocation, target.ObjectID);
                    foreach (var action in actor.ActionList.ToArray()) { actor.ActionList.Remove(action); actor.Process(action); }
                }
                void Ready() { actor.ActionTime = actor.AttackTime = 0; }
                Ready(); Shoot(); check(target.HP == 9900 && actor.MP == 1000, "人物/英雄普箭未沿物攻原扣血或错误耗蓝");
                var hp = target.HP; Shoot(); check(target.HP == hp, "普箭绕过原攻击/动作间隔");
                Ready(); actor.Dead = true; Shoot(); actor.Dead = false; check(target.HP == hp, "死亡仍可普射");
                Ready(); actor.CurrentPoison = PoisonType.Frozen; Shoot(); actor.CurrentPoison = PoisonType.None;
                check(target.HP == hp, "冰冻仍可普射");
                map.GetCell(target.CurrentLocation).Remove(target); target.CurrentLocation = new(20, actor.CurrentLocation.Y); map.GetCell(target.CurrentLocation).Add(target);
                Ready(); Shoot(new(19, actor.CurrentLocation.Y)); check(target.HP == hp, "伪造射击落点绕过真实目标射程");
                map.GetCell(target.CurrentLocation).Remove(target); target.CurrentLocation = new(19, actor.CurrentLocation.Y); map.GetCell(target.CurrentLocation).Add(target);
                map.Cells[15, actor.CurrentLocation.Y].Attribute = CellAttribute.HighWall;
                Ready(); Shoot(); check(target.HP == hp, "普射穿过阻挡格"); map.Cells[15, actor.CurrentLocation.Y].Attribute = CellAttribute.Walk;
                Ready(); actor.RangeAttack(MirDirection.Right, target.CurrentLocation, target.ObjectID);
                target.CurrentMap = new Map(new MapInfo());
                foreach (var action in actor.ActionList.ToArray()) { actor.ActionList.Remove(action); actor.Process(action); }
                check(target.HP == hp, "弹道命中前切图仍造成伤害");
                map.GetCell(target.CurrentLocation).Remove(target);
            }
        }
        finally { Settings.RangeAccuracyBonus = original; Settings.PlayerPhysicalDamageRate = rate; }
    }
}
