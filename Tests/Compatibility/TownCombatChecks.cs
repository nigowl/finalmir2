using System.Drawing;
using System.Reflection;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class TownCombatChecks
{
    private sealed class Player : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
    }

    public static void Run(Action<bool, string> check)
    {
        var map = new Map(new MapInfo { Index = -20 }) { Width = 9, Height = 9, Cells = new Cell[9, 9] };
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++) map.Cells[x, y] = new Cell();
        var player = new Player { Info = new CharacterInfo { Level = 10, Name = "TownCombat", BindMapIndex = map.Info.Index, BindLocation = new Point(1, 1) },
            Account = new AccountInfo(), CurrentMap = map, CurrentLocation = new Point(2, 2), Dead = true, Stats = new Stats(), DamageRate = 1, ArmourRate = 1 };
        player.Info.Mount = new MountInfo(player);
        player.Node = new LinkedListNode<MapObject>(player);
        map.AddObject(player); Envir.Main.MapList.Add(map);
        MonsterObject Spawn(byte ai, Point location, Player? master = null)
        {
            var monster = MonsterObject.GetMonster(new MonsterInfo { Name = "GuardFixture", AI = ai, Level = 1, ViewRange = 3,
                Stats = new Stats { [Stat.HP] = 50, [Stat.MinDC] = 10, [Stat.MaxDC] = 10, [Stat.Accuracy] = 100 } });
            monster.CurrentMap = map; monster.CurrentLocation = location; monster.Master = master;
            monster.DamageRate = monster.ArmourRate = 1;
            monster.Stats.Add(monster.Info.Stats); monster.SetHP(50); monster.ActionTime = monster.AttackTime = -1;
            monster.Node = new LinkedListNode<MapObject>(monster); map.AddObject(monster);
            return monster;
        }
        void Act(MonsterObject monster, string method) => monster.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(monster, null);
        try
        {
            var distant = Spawn(0, new Point(8, 8));
            var animal = Spawn(2, new Point(4, 5));
            foreach (var ai in new byte[] { 6, 57, 113 })
            {
                var guard = Spawn(ai, new Point(4, 4));
                var pet = Spawn(2, new Point(5, 4), player);
                Act(guard, "FindTarget");
                check(guard.Target == pet && guard.Target != distant && guard.Target != animal,
                    $"AI {ai} 没有选择近处召唤兽或没有保留野生动物免战。");
                Act(guard, "ProcessTarget");
                check(ai == 6 ? player.Packets.OfType<S.ObjectAttack>().Any(packet => packet.ObjectID == guard.ObjectID && packet.Location == guard.CurrentLocation) :
                    player.Packets.OfType<S.ObjectRangeAttack>().Any(packet => packet.ObjectID == guard.ObjectID && packet.Target == pet.CurrentLocation),
                    $"AI {ai} 没有以真实坐标广播攻击动作。");
                var hit = guard.ActionList.Single(action => action.Type is DelayedType.Damage or DelayedType.RangeDamage);
                guard.Process(hit);
                check(pet.HP < 50, $"AI {ai} 找到召唤兽但未命中。");
                map.RemoveObject(pet);
                var nearby = Spawn(0, new Point(5, 4));
                guard.Target = null;
                Act(guard, "FindTarget");
                check(guard.Target == nearby, $"AI {ai} 没有选择警戒范围内的野怪。");
                map.RemoveObject(nearby);
                guard.Target = null;
                Act(guard, "FindTarget");
                check(guard.Target == null, $"AI {ai} 选中了警戒范围外的野怪或普通野生动物。");
                map.RemoveObject(guard);
            }
            var attacker = Spawn(0, new Point(2, 1));
            player.Target = player.LastHitter = attacker; player.BrownTime = player.LastHitTime = Envir.Main.Time + 60000;
            var oldArrow = new DelayedAction(DelayedType.RangeDamage, Envir.Main.Time + 1000, player, 9999, DefenceType.AC);
            player.TownRevive();
            var health = Math.Max(1, player.Stats[Stat.HP] / 10);
            check(!player.Dead && player.HP == health && player.Target == null && player.LastHitter == null && player.BrownTime <= Envir.Main.Time &&
                player.CurrentLocation == player.Info.BindLocation && player.Packets.OfType<S.Revived>().Count() == 1 &&
                player.Packets.OfType<S.HealthChanged>().Last().HP == health, "回城复活未清理交战、未落在绑定点或未按10%回血。");
            attacker.Process(oldArrow);
            check(player.HP == health && player.LastHitter == null, "同图复活仍被上一次交战的箭命中。");
            check(player.IsAttackTarget(attacker), "回城复活意外开启了对野怪的持续免战。");
        }
        finally { Envir.Main.MapList.Remove(map); }
    }
}
