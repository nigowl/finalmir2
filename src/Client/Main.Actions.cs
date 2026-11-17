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
        preparedSkill = null;
        destination = point; actionTarget = 0; pendingAttack = null; running = run;
        MiniMap.Route = State.User?.Location == point ? [] : World.PathTo(point);
    }
    public void SelectTarget(uint id) { actionTarget = id; running = preferences.DefaultRun; autoPickupTarget = 0; World.Selected = id; destination = null; pendingAttack = null; MiniMap.Route = []; }
    private void ProcessActions()
    {
        var me = State.Me; var user = State.User;
        if (me == null || user == null || me.Dead || World.Loading || World.Map == null || !HandshakeReady) return;
        if (SkillPreparationBusy && !skillPreparationPaused || classPendingSpell != Spell.None) return;
        if (autoBattleEnabled && (State.Trading || State.StorageActive || npc.Visible || GuildStorageOpen))
        { destination = null; actionTarget = 0; pendingAttack = null; MiniMap.Route = []; return; }
        var now = Environment.TickCount64;
        if (destination.HasValue) World.WarmMovement(MiniMap.Route, running);
        if (moving)
        {
            if (now - moveTime > 3000) { moving = false; destination = null; actionTarget = 0; MiniMap.Route = []; Log("移动回执超时，请检查连接后重新选择目标。"); }
            return;
        }
        if (State.Actors.GetValueOrDefault(actionTarget) is { Kind: ObjectType.Item } loot && loot.Location == user.Location)
        { PickUpTarget(loot); return; }
        if (now - moveTime < me.MoveDelay || now - me.ActionTime < World.ActionDuration(me) && me.Action != MirAction.Standing) return;
        if (pendingAttack is { } direction) { Attack(direction, pendingAttackSpell); return; }
        var canRun = ClientMovement.CanRun(State, running, lastMoved, now);
        if (actionTarget != 0)
        {
            if (!State.Actors.TryGetValue(actionTarget, out var target) || target.Hidden) { actionTarget = 0; return; }
            var distance = Distance(user.Location, target.Location);
            if (target.Kind == ObjectType.Merchant && distance <= 5)
            { destination = null; MiniMap.Route = []; State.NpcId = target.Id; Send(new C.CallNPC { ObjectID = target.Id, Key = "[@MAIN]" }); actionTarget = 0; return; }
            if (target.Kind == ObjectType.Monster && !target.Dead && BattleSkill(target, now)) return;
            if (ClassSkillRules.Thrusting(State, preferences, target, SmartCombatActive, now))
            { Attack(Direction(user.Location, target.Location), Spell.Thrusting); return; }
            if (target.Kind == ObjectType.Monster && !target.Dead && !ClientAutomation.RequiresPhysicalAttack(target) && OriginalPlayerArt.HasBow(user.Class, me.Weapon) && distance <= Globals.MaxAttackRange)
            { RangeAttack(target); return; }
            if (target.Kind == ObjectType.Monster && distance <= 1)
            {
                if (target.Dead) { Send(new C.Harvest { Direction = Direction(user.Location, target.Location) }); actionTarget = 0; }
                else Attack(Direction(user.Location, target.Location), autoBattleEnabled ? Spell.None : null);
                return;
            }
            destination = target.Location;
            if (target.Kind != ObjectType.Item)
            {
                destination = World.Approach(target, canRun ? ClientMovement.RunDistance(State) : 1);
                if (destination == null) { actionTarget = 0; MiniMap.Route = []; return; }
            }
        }
        if (destination == null) return;
        if (user.Location == destination) { destination = null; MiniMap.Route = []; return; }
        var step = World.NextStep(destination.Value, canRun, out var route);
        MiniMap.Route = route;
        if (step == null) { destination = null; MiniMap.Route = []; Log("寻路已停止：目标不可到达或道路被阻挡。"); return; }
        var dir = Direction(user.Location, step.Value);
        var run = Distance(user.Location, step.Value) > 1;
        var ready = World.PlayerAnimationReady(dir, MirAction.Walking);
        if (running) ready &= World.PlayerAnimationReady(dir, MirAction.Running);
        if (!ready) return;
        for(var distance=1;distance<=Distance(user.Location,step.Value);distance++)
        {
            var cell=Functions.PointMove(user.Location,dir,distance);
            var door=World.Map.MapCells[cell.X,cell.Y].DoorIndex;
            if(door!=0)Send(new C.Opendoor{DoorIndex=door});
        }
        Send(run ? new C.Run { Direction = dir } : new C.Walk { Direction = dir });
        moveTime = now; moving = true;
    }
    private void RangeAttack(Actor target)
    {
        var user = State.User!; var me = State.Me!;
        var now = Environment.TickCount64;
        var speed = Math.Max(550, 1400 - State.CalculateStats()[Stat.AttackSpeed] * 60 - Math.Min(370, user.Level * 14));
        var direction = Direction(user.Location, target.Location);
        if (now - attackTime < speed + 200 || !World.PlayerAnimationReady(direction, MirAction.AttackRange1)) return;
        me.Direction = direction;
        Send(new C.RangeAttack { Direction = direction, Location = user.Location, TargetID = target.Id, TargetLocation = target.Location });
        attackTime = now;
    }
    public void Attack(MirDirection direction, Spell? automaticSpell = null)
    {
        if (State.Me == null || State.User == null || State.Me.Dead) return;
        var speed = Math.Max(550, 1400 - State.CalculateStats()[Stat.AttackSpeed] * 60 - Math.Min(370, State.User.Level * 14));
        var now = Environment.TickCount64;
        if (moving || now - moveTime < State.Me.MoveDelay || now - attackTime < speed) return;
        var spell = ClassSkillRules.Melee(State, direction, automaticSpell,
            State.User.Class == MirClass.Warrior && preferences.SmartHalfMoon || SmartCombatActive);
        var level = State.User.Magics.FirstOrDefault(magic => magic.Spell == spell)?.Level ?? 0;
        if (!World.MeleeReady(spell, level, direction)) { pendingAttack = direction; pendingAttackSpell = automaticSpell; return; }
        pendingAttack = null;
        Send(new C.Attack { Direction = direction, Spell = spell }); attackTime = Environment.TickCount64;
        _ = PlaySound(OriginalSoundRules.Swing(State.Me.Weapon, State.Me.Class == MirClass.Assassin));
        PlayMeleeSound(spell, State.Me.Gender);
        if (spell == Spell.FlamingSword && State.User.Magics.FirstOrDefault(m => m.Spell == spell) is { } used) used.CastTime = attackTime;
        if (spell is Spell.Slaying or Spell.TwinDrakeBlade or Spell.FlamingSword) State.ActiveSkills.Remove(spell);
        State.StartAttack(State.Me, direction, spell, level);
    }
    public void Cast(int index, bool automatic = false, bool profession = false)
    {
        if (State.User == null || index < 0 || index >= State.User.Magics.Count || State.Me?.Dead != false || SkillPreparationBusy || classPendingSpell != Spell.None || State.Trading || State.StorageActive) return;
        var magic = State.User.Magics[index];
        if (!ClientSkillRules.IsImplemented(magic.Spell)) { Log("当前原引擎尚未实现此技能，无法施放。", ChatType.Hint); return; }
        if (ClientSkillRules.IsPassive(magic.Spell)) { Log("此技能为被动技能。"); return; }
        if (ClientSkillRules.IsToggle(magic.Spell))
        {
            SetSkillToggle(index, !State.ActiveSkills.Contains(magic.Spell)); return;
        }
        if (State.DisabledSkills.Contains(magic.Spell)) { Log("该技能已关闭。"); return; }
        if (magic.Spell == Spell.MentalState) { Send(new C.SpellToggle { Spell = magic.Spell, CanUse = true }); return; }
        var now = Environment.TickCount64;
        if (now < spellTime || now < magic.CastTime + Math.Max(0, magic.Delay)) return;
        if (ClientSkillRules.IsTrigger(magic.Spell))
        { Send(new C.SpellToggle { Spell = magic.Spell, CanUse = true }); if (profession) AwaitClassSkill(magic.Spell, 0, 0, now); return; }
        var targetId = World.Selected;
        var point = State.Actors.TryGetValue(targetId, out var selected) ? selected.Location : World.CellAt(World.GetGlobalMousePosition());
        if (magic.Spell == Spell.Healing && (selected == null || selected.Kind != ObjectType.Player || selected.Dead)) { targetId = State.User.ObjectID; point = State.User.Location; }
        RequestSkillCast(magic, targetId, point, automatic, profession);
    }
}
