using Crystal.Native;
using Point = System.Drawing.Point;
using S = ServerPackets;

internal static class ClientActionItemsChecks
{
    public static void Run(Action<bool, string> check)
    {
        var state = new GameState();
        state.Apply(new S.UserInformation { ObjectID = 7, HP = 100, Location = new Point(20, 20), Inventory = new UserItem[46], Equipment = new UserItem[14], QuestInventory = [] });
        var me = state.Me!;
        var now = Environment.TickCount64;
        me.Move(new Point(22, 20), MirDirection.Right, MirAction.Running, now - 100);
        state.User!.Location = me.Location;
        var action = me.ActionTime;
        var beforeSlow = me.PositionAt(now, Actor.MoveDuration);
        state.Apply(new S.Poisoned { Poison = PoisonType.Slow });
        check(me.PositionAt(now, Actor.MoveDuration) == beforeSlow, "移动途中中减速导致插值后退");
        state.Apply(new S.Struck { AttackerID = 30 });
        state.Apply(new S.Poisoned { Poison = PoisonType.Green });
        check(me.Action == MirAction.Running && me.ActionTime == action && me.Poison == PoisonType.Green, "受击/中毒打断仍未结束的跑步");
        me.Move(me.Location, me.Direction, MirAction.Spell);
        state.Apply(new S.Struck());
        check(me.Action == MirAction.Spell, "受击截断施法动作");
        state.Apply(new S.ObjectHarvest { ObjectID = me.Id, Location = me.Location, Direction = MirDirection.Left });
        state.Apply(new S.Struck());
        check(me.Action == MirAction.Harvest && me.Direction == MirDirection.Left, "割肉回执未显示动作或受击中断割肉");
        state.Apply(new S.UserLocation { Location = me.Location, Direction = me.Direction });
        check(me.Action == MirAction.Harvest, "原地位置回执覆盖割肉");
        me.Move(me.Location, me.Direction, MirAction.Standing);
        state.Apply(new S.Struck());
        check(me.Action == MirAction.Struck, "静止时没有受击动作");
        me.Move(me.Location, me.Direction, MirAction.AttackRange1, Environment.TickCount64 - PlayerActionTiming.Default.Scale(700));
        state.Apply(new S.Struck());
        check(me.Action == MirAction.AttackRange1, "受击截断800ms弓箭动作的最后几帧");
        me.ActionTime = Environment.TickCount64 - 1100;
        state.Apply(new S.Struck());
        check(me.Action == MirAction.Struck, "已完成动作后仍不显示受击反馈");
        var target = new Actor { Id = 8, Kind = ObjectType.Monster, Location = new Point(25, 24) };
        var mouse = new Point(30, 30);
        check(ClientSkillRules.Aim(Spell.FireWall, me, null, mouse) == (0u, mouse), "地面技能没有选中对象时没有使用鼠标坐标");
        check(ClientSkillRules.Aim(Spell.FireBang, me, target, mouse) == (target.Id, target.Location), "选中目标的范围技能没有使用对象坐标");
        target.Dead = true;
        check(ClientSkillRules.Aim(Spell.FireWall, me, target, mouse) == (0u, mouse), "已死亡的旧目标覆盖鼠标坐标");
        check(ClientSkillRules.Aim(Spell.Poisoning, me, null, mouse) == null, "目标专用技能无目标时仍被发出");
        check(ClientSkillRules.Aim(Spell.Healing, me, null, mouse) == (me.Id, me.Location), "无目标治愈未保留本人恢复");
        check(ClientSkillRules.Aim(Spell.MagicShield, me, target, mouse) == (0u, me.Location), "自身增益错误使用远处目标坐标");
        target.Dead = false;
        foreach (var spell in new[] { Spell.FireBounce, Spell.StraightShot, Spell.DoubleShot, Spell.DelayedExplosion, Spell.ElementalShot, Spell.DarkBody })
            check(ClientSkillRules.Aim(spell, me, target, mouse) == (target.Id, target.Location) && ClientSkillRules.Aim(spell, me, null, mouse) == null,
                "对象技能丢失已选择目标或无目标仍扣蓝：" + spell);
        me.Hidden = true;
        check(ClientSkillRules.Aim(Spell.UltimateEnhancer, me, me, mouse) == (me.Id, me.Location), "隐身本人的增益目标被清除");
        me.Hidden = false;
        check(PlayerActionTiming.DefaultMoveMilliseconds == 500 && Server.MirObjects.HumanObject.MoveDelay == me.MoveDelay &&
            Server.MirObjects.PlayerObject.HarvestDelay == PlayerActionTiming.Default.Scale(350), "两端走跑/割肉未同步1.1倍速");
        check(PlayerActionTiming.Default.AttackDelay(0, 1) == PlayerActionTiming.Default.Scale(1386) &&
            PlayerActionTiming.Default.AttackDelay(100, 100) == 500, "普攻基础/最快间隔没有同步提速10%");
        check(PlayerActionTiming.Default.CastDelay(Spell.FireBall) == 1637 && PlayerActionTiming.Default.CastDelay(Spell.DoubleShot) == 1637 &&
            PlayerActionTiming.Default.CastDelay(Spell.FlameField) == 2273, "技能全局间隔与原服不一致");
        check(!ClientSkillRules.CastBlocked(PoisonType.Green | PoisonType.Red | PoisonType.Slow) &&
            ClientSkillRules.CastBlocked(PoisonType.Stun | PoisonType.Green), "普通毒/减速被当成禁止施法，或眩晕漏拦");
        foreach (var poison in new[] { PoisonType.Stun, PoisonType.Dazed, PoisonType.Paralysis, PoisonType.Frozen })
            check(ClientSkillRules.CastBlocked(poison), "禁施法异常漏拦：" + poison);
        var hovered = new Actor { Id = 21, Kind = ObjectType.Monster, Location = new Point(22, 22) };
        foreach (var spell in new[] { Spell.Poisoning, Spell.SoulFireBall })
            check(ClientSkillRules.Aim(spell, me, null, mouse, hovered) == (hovered.Id, hovered.Location) &&
                ClientSkillRules.Aim(spell, me, target, mouse, hovered) == (hovered.Id, hovered.Location), "鼠标悬停未作为技能目标：" + spell);
        var gait = new Actor { Kind = ObjectType.Player, Location = new Point(10, 10) };
        gait.Move(new Point(11, 10), MirDirection.Right, MirAction.Walking, 1000);
        var phase = gait.MovementAnimationElapsed(1517, 600);
        var displayed = gait.PositionAt(1517, 600);
        gait.Move(new Point(12, 10), MirDirection.Right, MirAction.Walking, 1517);
        check(gait.MovementAnimationElapsed(1517, 600) >= phase - 1 && gait.PositionAt(1517, 600) == displayed, "连续走路重置步态或位置跳动");
        check(gait.PositionAt(9999, 600) == new System.Numerics.Vector2(12, 10), "移动插值越过权威终点");
        var preferences = new ClientPreferences { AutoSell = true, SellNames = ["普通肉"], SellWhitelist = true };
        check(!Functions.CanFly(new Point(0, 0), new Point(4, 2), point => point != new Point(2, 2)), "斜向射线跳过原服逐格阻挡");
        check(Functions.CanFly(new Point(0, 0), new Point(4, 2), point => point != new Point(0, 1)), "旁边障碍错误挡住射线");
        check(!Functions.CanFly(new Point(4, 2), new Point(0, 0), point => point != new Point(0, 0)), "射线终点阻挡未检查");
        check(ClientSkillRules.NeedsClearShot(Spell.SoulFireBall) && ClientSkillRules.NeedsClearShot(Spell.FireBall) &&
            !ClientSkillRules.NeedsClearShot(Spell.Poisoning) && !ClientSkillRules.NeedsClearShot(Spell.ThunderBolt), "投射技能与非射线技能规则混用");
        var meat = new UserItem(new ItemInfo { Name = "普通肉", Type = ItemType.Meat, Price = 100, StackSize = 100 }) { Count = 10, UniqueID = 1 };
        state.User!.Inventory[0] = meat;
        check(ClientAutoSell.Next(state, preferences, new HashSet<ulong>()) == meat, "白名单没有选择指定普通物品");
        check(ClientAutoSell.Next(state, preferences, new HashSet<ulong> { 1 }) == null, "已提交物品重复出售");
        meat.Info.Type = ItemType.Quest;
        check(!ClientAutoSell.Allowed(state, preferences, meat), "任务专用品进入自动出售");
        meat.Info.Type = ItemType.Meat; meat.SoulBoundId = 2;
        check(!ClientAutoSell.Allowed(state, preferences, meat), "绑定物品进入自动出售");
        meat.SoulBoundId = -1; meat.AddedStats[Stat.MaxDC] = 1;
        check(!ClientAutoSell.Allowed(state, preferences, meat), "强化物品进入自动出售");
        meat.AddedStats.Clear(); preferences.SellWhitelist = false; preferences.SellNames = ["保留物品"];
        check(ClientAutoSell.Allowed(state, preferences, meat), "黑名单未允许普通肉");
        meat.Info.Type = ItemType.Potion;
        check(!ClientAutoSell.Allowed(state, preferences, meat), "黑名单自动出售药品");
        meat.Info.Type = ItemType.Meat; state.Quests[1] = new ClientQuestProgress { TaskList = ["普通肉 0/5"] };
        check(!ClientAutoSell.Allowed(state, preferences, meat), "当前任务所需普通材料被卖出");
        preferences.SellNames = ["  普通肉 ", "普通肉", ""]; preferences.Normalize();
        check(preferences.SellNames.SequenceEqual(new[] { "普通肉" }), "出售名单未去重/修剪空白");
    }
}
