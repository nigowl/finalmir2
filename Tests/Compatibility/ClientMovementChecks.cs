using Crystal.Native;
using S = ServerPackets;

internal static class ClientMovementChecks
{
    public static void Run(Action<bool, string> check)
    {
        var state = new GameState();
        state.Apply(new S.UserInformation { ObjectID = 1, Location = new(10, 10), HP = 100,
            Inventory = new UserItem[4], Equipment = new UserItem[14], QuestInventory = [] });
        state.BaseStats = new BaseStats(MirClass.Warrior) { Stats = [new BaseStat(Stat.BagWeight) { Base = 20 }] };
        check(!ClientMovement.CanRun(state, true, 0, 1000), "普通角色没有移动回执仍直接起跑");
        check(ClientMovement.CanRun(state, true, 1000, 1600), "确认走一步后仍不能跑");
        check(!ClientMovement.CanRun(state, false, 1000, 1600), "选择走路后仍跑步");
        check(ClientMovement.CanRun(state, true, 1000, 2050) && !ClientMovement.CanRun(state, true, 1000, 2200), "起跑资格不符合原服有效期");
        var info = new ItemInfo { Index = 1, Type = ItemType.Boots, Durability = 100, CanFastRun = true };
        state.User!.Equipment[11] = new UserItem(info) { CurrentDura = 100 };
        check(ClientMovement.CanRun(state, true, 0, 1000), "快速起跑装备仍被强制先走一步");
        state.User.Equipment[11]!.CurrentDura = 0;
        check(!ClientMovement.CanRun(state, true, 0, 1000), "损坏装备仍提供快速起跑");
        state.UserBuffs[BuffType.Transform] = new() { Type = BuffType.Transform, Values = [1] };
        check(ClientMovement.CanRun(state, true, 0, 1000), "原变身快速起跑仍强制走路");
        state.UserBuffs[BuffType.Transform].Paused = true;
        check(!ClientMovement.CanRun(state, true, 0, 1000), "暂停变身仍提供快速起跑");
        state.UserBuffs.Remove(BuffType.Transform);
        state.User.Equipment[11]!.CurrentDura = 100;
        foreach (var poison in new[] { PoisonType.Paralysis, PoisonType.LRParalysis, PoisonType.Frozen })
        { state.Me!.Poison = poison; check(!ClientMovement.CanRun(state, true, 1000, 1600), "不可移动状态仍允许跑步：" + poison); }
        state.Me!.Poison = PoisonType.Slow;
        check(ClientMovement.CanRun(state, true, 0, 1600) && state.Me.MoveDelay == PlayerActionTiming.DefaultMoveMilliseconds * 2, "减速被错误当作禁跑或丢失双倍间隔");
        state.Me.Poison = PoisonType.None;
        state.UserBuffs[BuffType.MoonLight] = new() { Type = BuffType.MoonLight };
        check(!ClientMovement.CanRun(state, true, 1000, 1600), "潜行绕过原跑步限制");
        state.UserBuffs[BuffType.SwiftFeet] = new() { Type = BuffType.SwiftFeet };
        check(ClientMovement.CanRun(state, true, 1000, 1600) && ClientMovement.RunDistance(state) == 2, "潜行疾风步步数不符原服");
        state.UserBuffs.Remove(BuffType.MoonLight);
        check(ClientMovement.RunDistance(state) == 3, "疾风步没有检查完整三格");
        state.UserBuffs[BuffType.SwiftFeet].Paused = true;
        check(ClientMovement.RunDistance(state) == 2, "暂停疾风步仍走三格");
        state.Apply(new S.MountUpdate { ObjectID = 1, RidingMount = true });
        check(ClientMovement.RunDistance(state) == 3, "坐骑原回执未同步三格跑步");
        state.Apply(new S.MountUpdate { ObjectID = 1, RidingMount = false });
        check(ClientMovement.RunDistance(state) == 2, "下马仍按三格规划");
        state.User.Inventory[0] = new UserItem(new ItemInfo { Type = ItemType.Ore, Weight = 100 }) { Count = 21 };
        check(!ClientMovement.CanRun(state, true, 1000, 1600), "超重仍跑步");
        check(ClientMovement.StepIndex([], 2) == 0 && ClientMovement.StepIndex([new(0, 0)], 2) == 0, "空路线访问越界");
        check(ClientMovement.StepIndex([new(0, 0), new(1, 0)], 2) == 1, "终点前一步发生越过");
        check(ClientMovement.StepIndex([new(0, 0), new(1, 0), new(2, 0)], 2) == 2, "正常两格退回走路");
        check(ClientMovement.StepIndex([new(0, 0), new(1, 0), new(2, 1)], 2) == 1, "跑步跨过弯角");
        check(ClientMovement.StepIndex([new(0, 0), new(1, 0), new(2, 0)], 3) == 1, "三格状态只检查两格就跑");
        check(ClientMovement.StepIndex([new(0, 0), new(1, 0), new(2, 0), new(3, 0)], 3) == 3, "完整三格不能跑");
        check(ClientMovement.StepIndex([new(0, 0), new(1, 0), new(2, 0), new(3, 1)], 3) == 1, "三格跑步越过第三格弯角");
    }
}
