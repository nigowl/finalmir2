using Crystal.Native;
using Client.MirObjects;
using Godot;
using C = ClientPackets;
using Point = System.Drawing.Point;

public partial class Main
{
    private MirDirection? pendingAttack;
    private Spell? pendingAttackSpell;
    private long routeBlockedSince, retryRouteAt;
    private bool moveRejected;
    private Point requestedMove;
    private Point? previousMoveGoal;
    public static MirDirection Direction(Point from, Point to)
    {
        var x = Math.Sign(to.X - from.X); var y = Math.Sign(to.Y - from.Y);
        return (x, y) switch { (0, -1) => MirDirection.Up, (1, -1) => MirDirection.UpRight, (1, 0) => MirDirection.Right,
            (1, 1) => MirDirection.DownRight, (0, 1) => MirDirection.Down, (-1, 1) => MirDirection.DownLeft, (-1, 0) => MirDirection.Left, _ => MirDirection.UpLeft };
    }
    private static int Distance(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    public void SetDestination(Point point, bool run = false)
    {
        StopKeyboardMovement();
        if (!applyingQuestNavigation) CancelQuestNavigation();
        preparedSkill = null;
        ResetRouteRetry();
        destination = point; actionTarget = 0; World.Selected = 0; pendingAttack = null; running = run;
        MiniMap.Route = State.User?.Location == point ? [] : World.PathTo(point);
    }
    public void SelectTarget(uint id) { StopKeyboardMovement(); CancelQuestNavigation(); ResetRouteRetry(); actionTarget = id; running = preferences.DefaultRun; autoPickupTarget = 0; World.Selected = id; destination = null; pendingAttack = null; MiniMap.Route = []; }

    private void ResetRouteRetry()
    {
        doorRequests.Clear();
        routeBlockedSince = retryRouteAt = 0;
        moveRejected = false;
    }

    private void WaitForRoute(long now, bool mayClear)
    {
        if (routeBlockedSince == 0) routeBlockedSince = now;
        if (mayClear && now - routeBlockedSince < 1600) { retryRouteAt = now + 200; return; }
        destination = null; actionTarget = 0; MiniMap.Route = [];
        ResetRouteRetry();
        Log("寻路已停止：目标不可到达或道路被阻挡。");
    }

    private void ConfirmLocation(Point location)
    {
        var now = Environment.TickCount64;
        if (moving && now - moveTime >= 750)
            GD.Print($"移动回执延迟：{State.Map?.FileName} {confirmedLocation} → {requestedMove}，回到 {location}，{now - moveTime}ms，网络 {State.Ping}ms。");
        if (location != confirmedLocation)
        {
            if (moving)
            {
                if (destination != null && destination == previousMoveGoal && lastMoved != 0 &&
                    now - lastMoved > (State.Me?.LastMoveDelay ?? State.PlayerTiming.MoveMilliseconds) + 250)
                    GD.Print($"连续移动间隔异常：地图 {State.Map?.FileName} #{State.Map?.MapIndex}，{confirmedLocation} → {location}，发包前 {moveTime - lastMoved}ms，回执 {now - moveTime}ms，网络 {State.Ping}ms。");
                // 以服务端确认时刻作为下一步基准，网络抖动不能让下一步提前撞上服务端 ActionTime。
                lastAcceptedMoveTime = now;
            }
            previousMoveGoal = moving ? destination : null;
            confirmedLocation = location;
            lastMoved = now;
            ResetRouteRetry();
        }
        else if (moving)
        {
            // 同坐标只有在等移动回执时才是拒绝；下一次先尝试单步。
            moveRejected = true;
            lastMoved = 0;
            WaitForRoute(now, true);
        }
        moving = false;
    }

    private void StopTimedOutMove(long now, string message)
    {
        GD.PrintErr($"移动回执超时：地图 {State.Map?.FileName} #{State.Map?.MapIndex}，{confirmedLocation} → {requestedMove}，{now - moveTime}ms，网络 {State.Ping}ms。");
        moving = false; destination = null; actionTarget = 0; MiniMap.Route = [];
        ResetRouteRetry();
        Log(message, ChatType.Hint);
    }

    private void ProcessActions()
    {
        ProcessKeyboardMovement();
        var me = State.Me; var user = State.User;
        if (me == null || user == null || me.Dead || World.Loading || World.Map == null || !HandshakeReady || npcInput.Visible) return;
        if (SkillPreparationBusy && !skillPreparationPaused || classPendingSpell != Spell.None || castPendingUntil > Environment.TickCount64) return;
        if (autoBattleEnabled && (State.Trading || State.StorageActive || npc.Visible || GuildStorageOpen))
        { destination = null; actionTarget = 0; pendingAttack = null; MiniMap.Route = []; return; }
        var now = Environment.TickCount64;
        if (moving)
        {
            if (now - moveTime >= 3000) StopTimedOutMove(now, "服务端未确认移动，请重新选择目标。");
            return;
        }
        if (State.Actors.GetValueOrDefault(actionTarget) is { Kind: ObjectType.Item } loot && loot.Location == user.Location)
        { PickUpTarget(loot); return; }
        if (ActionInProgress(me, now, true) || (me.Poison & (PoisonType.Paralysis | PoisonType.LRParalysis | PoisonType.Frozen)) != 0) return;
        if (pendingAttack is { } direction) { Attack(direction, pendingAttackSpell); return; }
        if (now < retryRouteAt) return;
        var canRun = !moveRejected && ClientMovement.CanRun(State, running, lastMoved, now);
        if (actionTarget != 0)
        {
            if (!State.Actors.TryGetValue(actionTarget, out var target) || target.Hidden) { actionTarget = 0; return; }
            var distance = Distance(user.Location, target.Location);
            if (target.Kind == ObjectType.Merchant && distance <= 5)
            { destination = null; MiniMap.Route = []; State.NpcId = target.Id; Send(new C.CallNPC { ObjectID = target.Id, Key = "[@MAIN]" }); actionTarget = 0; return; }
            if (target.Kind == ObjectType.Monster && !target.Dead && BattleSkill(target, now)) return;
            if (ClassSkillRules.Thrusting(State, preferences, target, SmartCombatActive, now))
            { Attack(Direction(user.Location, target.Location), Spell.Thrusting); return; }
            if (target.Kind == ObjectType.Monster && !target.Dead && !ClientAutomation.RequiresPhysicalAttack(target) && OriginalPlayerArt.HasBow(user.Class, me.Weapon) && distance <= Globals.MaxAttackRange && World.ClearShot(target.Location))
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
                if (destination == null) { WaitForRoute(now, true); return; }
            }
        }
        if (destination == null || State.InTrapRock) return;
        if (user.Location == destination) { destination = null; MiniMap.Route = []; return; }
        var step = World.NextStep(destination.Value, canRun, out var route);
        MiniMap.Route = route;
        if (step == null) { WaitForRoute(now, routeBlockedSince != 0 || World.PathTo(destination.Value, ignoreActors: true).Length > 1); return; }
        var dir = Direction(user.Location, step.Value);
        var run = Distance(user.Location, step.Value) > 1;
        // 素材预取只影响画面，网络或微端缓存不能阻止合法移动。
        World.WarmMovement(route, running);
        if (!EnsureDoorsOpen(dir, Distance(user.Location, step.Value), now)) return;
        requestedMove = step.Value;
        moveTime = Environment.TickCount64;
        moving = true;
        Send(run ? new C.Run { Direction = dir } : new C.Walk { Direction = dir });
    }
    private void RangeAttack(Actor target)
    {
        var user = State.User!; var me = State.Me!;
        var now = Environment.TickCount64;
        var speed = State.PlayerTiming.AttackDelay(State.CalculateStats()[Stat.AttackSpeed], user.Level);
        var direction = Direction(user.Location, target.Location);
        if (now - attackTime < speed || !World.PlayerAnimationReady(direction, MirAction.AttackRange1)) return;
        me.Direction = direction;
        Send(new C.RangeAttack { Direction = direction, Location = user.Location, TargetID = target.Id, TargetLocation = target.Location });
        attackTime = now;
    }
    public void Attack(MirDirection direction, Spell? automaticSpell = null)
    {
        if (State.Me == null || State.User == null || State.Me.Dead) return;
        var speed = State.PlayerTiming.AttackDelay(State.CalculateStats()[Stat.AttackSpeed], State.User.Level);
        var now = Environment.TickCount64;
        if (moving || ActionInProgress(State.Me, now) || now < castPendingUntil || now - attackTime < speed) return;
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
        if (State.User == null || index < 0 || index >= State.User.Magics.Count || State.Me?.Dead != false || SkillPreparationBusy || classPendingSpell != Spell.None ||
            State.Trading || State.StorageActive || recyclePending != null || ClientSkillRules.CastBlocked(State.Me.Poison) || World.Loading) return;
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
        if (now < spellTime || now < castPendingUntil || now < magic.CastTime + Math.Max(0, magic.Delay)) return;
        if (ClientSkillRules.IsTrigger(magic.Spell))
        { Send(new C.SpellToggle { Spell = magic.Spell, CanUse = true }); if (profession) AwaitClassSkill(magic.Spell, 0, 0, now); return; }
        var pointer = World.GetGlobalMousePosition();
        var mouse = World.CellAt(pointer);
        var hover = !automatic && GetViewport().GuiGetHoveredControl() == null ? World.ActorAt(pointer, true) : null;
        var aim = ClientSkillRules.Aim(magic.Spell, State.Me, State.Actors.GetValueOrDefault(World.Selected), mouse, hover);
        if (aim == null) { if (!automatic) Log("请将鼠标移到有效目标上，或先选中技能目标。", ChatType.Hint); return; }
        RequestSkillCast(magic, aim.Value.Target, aim.Value.Location, automatic, profession);
    }

    private bool ActionInProgress(Actor actor, long now, bool continuingMovement = false) =>
        // 只有走成的那一步占用动作间隔，短暂拒绝可以较快改走。
        now - lastAcceptedMoveTime < actor.LastMoveDelay ||
        actor.Action is not (MirAction.Standing or MirAction.Stance or MirAction.Struck) &&
        !(continuingMovement && actor.Action is MirAction.Walking or MirAction.Running) && now - actor.ActionTime < World.ActionDuration(actor);
}
