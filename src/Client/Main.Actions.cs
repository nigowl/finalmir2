using Crystal.Native;
using Client.MirObjects;
using Godot;
using C = ClientPackets;
using Point = System.Drawing.Point;

public partial class Main
{
    private MirDirection? pendingAttack;
    private Spell? pendingAttackSpell;
    public static MirDirection Direction(Point from, Point to)
    {
        var x = Math.Sign(to.X - from.X); var y = Math.Sign(to.Y - from.Y);
        return (x, y) switch { (0, -1) => MirDirection.Up, (1, -1) => MirDirection.UpRight, (1, 0) => MirDirection.Right,
            (1, 1) => MirDirection.DownRight, (0, 1) => MirDirection.Down, (-1, 1) => MirDirection.DownLeft, (-1, 0) => MirDirection.Left, _ => MirDirection.UpLeft };
    }
    private static int Distance(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    public void SetDestination(Point point, bool run = false)
    {
        destination = point; actionTarget = 0; pendingAttack = null; running = run;
        MiniMap.Route = State.User?.Location == point ? [] : World.PathTo(point);
    }
    public void SelectTarget(uint id) { actionTarget = id; World.Selected = id; destination = null; pendingAttack = null; MiniMap.Route = []; }
    private void ProcessActions()
    {
        var me = State.Me; var user = State.User;
        if (me == null || user == null || me.Dead || World.Loading || World.Map == null || !HandshakeReady) return;
        var now = Environment.TickCount64;
        if (moving)
        {
            if (now - moveTime > 3000) { moving = false; destination = null; actionTarget = 0; MiniMap.Route = []; Log("移动回执超时，请检查连接后重新选择目标。"); }
            return;
        }
        if (now - moveTime < Actor.MoveDuration || now - me.ActionTime < World.ActionDuration(me) && me.Action != MirAction.Standing) return;
        if (pendingAttack is { } direction) { Attack(direction, pendingAttackSpell); return; }
        if (actionTarget != 0)
        {
            if (!State.Actors.TryGetValue(actionTarget, out var target) || target.Hidden) { actionTarget = 0; return; }
            var distance = Distance(user.Location, target.Location);
            if (target.Kind == ObjectType.Merchant && distance <= 5)
            { destination = null; MiniMap.Route = []; State.NpcId = target.Id; Send(new C.CallNPC { ObjectID = target.Id, Key = "[@MAIN]" }); actionTarget = 0; return; }
            if (target.Kind == ObjectType.Item && distance == 0) { Send(new C.PickUp()); actionTarget = 0; return; }
            if (target.Kind == ObjectType.Monster && !target.Dead && BattleSkill(target, now)) return;
            if (target.Kind == ObjectType.Monster && distance <= 1)
            {
                if (target.Dead) { Send(new C.Harvest { Direction = Direction(user.Location, target.Location) }); actionTarget = 0; }
                else Attack(Direction(user.Location, target.Location), autoBattleEnabled ? Spell.None : null);
                return;
            }
            destination = target.Location;
            if (target.Kind != ObjectType.Item)
            {
                destination = World.Approach(target);
                if (destination == null) { actionTarget = 0; MiniMap.Route = []; return; }
            }
        }
        if (destination == null) return;
        if (user.Location == destination) { destination = null; MiniMap.Route = []; return; }
        // 原服务端要求先走一步；提前准备同向走跑素材，避免切换跑步时再次停顿。
        var step = World.NextStep(destination.Value, running && now - lastMoved < 700, out var route);
        MiniMap.Route = route;
        if (step == null) { destination = null; MiniMap.Route = []; Log("寻路已停止：目标不可到达或道路被阻挡。"); return; }
        var dir = Direction(user.Location, step.Value);
        var run = Distance(user.Location, step.Value) == 2;
        var ready = World.PlayerAnimationReady(dir, MirAction.Walking);
        if (running) ready &= World.PlayerAnimationReady(dir, MirAction.Running);
        if (!ready) return;
        Send(run ? new C.Run { Direction = dir } : new C.Walk { Direction = dir });
        moveTime = now; moving = true;
    }
    public void Attack(MirDirection direction, Spell? automaticSpell = null)
    {
        if (State.Me == null || State.User == null || State.Me.Dead) return;
        var speed = Math.Max(550, 1400 - State.CalculateStats()[Stat.AttackSpeed] * 60 - Math.Min(370, State.User.Level * 14));
        var now = Environment.TickCount64;
        if (moving || now - moveTime < Actor.MoveDuration || now - attackTime < speed) return;
        if (!World.PlayerAnimationReady(direction, MirAction.Attack1)) { pendingAttack = direction; pendingAttackSpell = automaticSpell; return; }
        pendingAttack = null;
        var spell = automaticSpell ?? Spell.None;
        foreach (var candidate in new[] { Spell.Slaying, Spell.Thrusting, Spell.HalfMoon, Spell.CrossHalfMoon, Spell.DoubleSlash, Spell.TwinDrakeBlade, Spell.FlamingSword })
        {
            if (automaticSpell.HasValue) break;
            if (!State.ActiveSkills.Contains(candidate)) continue;
            if (candidate == Spell.Thrusting && !State.Actors.Values.Any(a => !a.Dead && a.Kind == ObjectType.Monster && a.Location == Functions.PointMove(State.User.Location, direction, 2))) continue;
            var magic = State.User.Magics.FirstOrDefault(m => m.Spell == candidate);
            if (magic != null && magic.BaseCost + magic.LevelCost * magic.Level <= State.User.MP) spell = candidate;
        }
        Send(new C.Attack { Direction = direction, Spell = spell }); attackTime = Environment.TickCount64;
        _ = PlaySound(OriginalSoundRules.Swing(State.Me.Weapon, State.Me.Class == MirClass.Assassin));
        if (spell != Spell.None && State.User.Magics.FirstOrDefault(m => m.Spell == spell) is { } used) used.CastTime = attackTime;
        if (spell is Spell.Slaying or Spell.TwinDrakeBlade or Spell.FlamingSword) State.ActiveSkills.Remove(spell);
        State.Me.Direction = direction; State.Me.Action = MirAction.Attack1; State.Me.ActionTime = attackTime;
    }
    public void Cast(int index, bool automatic = false)
    {
        if (State.User == null || index >= State.User.Magics.Count || Environment.TickCount64 < spellTime) return;
        var magic = State.User.Magics[index];
        if (magic.Spell is Spell.Fencing or Spell.SpiritSword or Spell.Slaying or Spell.FatalSword or Spell.MPEater or Spell.Hemorrhage or Spell.Focus or Spell.Meditation) { Log("此技能为被动技能。"); return; }
        if (magic.Spell is Spell.Thrusting or Spell.HalfMoon or Spell.CrossHalfMoon or Spell.DoubleSlash or Spell.TwinDrakeBlade or Spell.FlamingSword or Spell.CounterAttack)
        {
            var enabled = !State.ActiveSkills.Contains(magic.Spell);
            Send(new C.SpellToggle { Spell = magic.Spell, CanUse = enabled });
            if (enabled) State.ActiveSkills.Add(magic.Spell); else State.ActiveSkills.Remove(magic.Spell);
            Log($"{magic.Name}: {(enabled ? "开启" : "关闭")}"); return;
        }
        destination = null; if (!automatic) actionTarget = 0; pendingAttack = null; MiniMap.Route = []; moveTime = Environment.TickCount64;
        var targetId = World.Selected;
        var point = State.Actors.TryGetValue(targetId, out var selected) ? selected.Location : World.CellAt(World.GetGlobalMousePosition());
        if (magic.Spell == Spell.Healing) { targetId = State.User.ObjectID; point = State.User.Location; }
        if (State.Me != null) State.Me.Direction = Direction(State.User.Location, point);
        Send(new C.Magic { ObjectID = State.User.ObjectID, Spell = magic.Spell, Direction = Direction(State.User.Location, point), TargetID = targetId, Location = point });
        magic.CastTime = Environment.TickCount64;
        spellTime = magic.CastTime + Math.Max(1800, magic.Delay);
    }
}
