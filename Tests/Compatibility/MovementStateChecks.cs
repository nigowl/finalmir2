using Client.MirObjects;
using Crystal.Native;
using System.Drawing;
using System.Numerics;
using S = ServerPackets;

internal static class MovementStateChecks
{
    public static void Run(Action<bool, string> check)
    {
        var state = new GameState();
        state.Apply(new S.UserInformation { ObjectID = 10, HP = 100, Location = new(10, 10), Inventory = [], Equipment = new UserItem[14] });
        state.Me!.Casting = true; state.Me.ActionSpell = Spell.FireBall;
        state.Apply(new S.Pushed { Location = new(12, 10), Direction = MirDirection.Left });
        check(state.User!.Location == new Point(12, 10) && state.User.Direction == MirDirection.Left && state.Me.Location == state.User.Location &&
            state.Me.Action == MirAction.Pushed && !state.Me.Casting && state.Me.ActionSpell == Spell.None, "受推没有同步本人坐标/朝向，或仍沿用旧施法动作");
        var time = state.Me.MotionTime;
        state.Apply(new S.ObjectPushed { ObjectID = 10, Location = new(11, 10), Direction = MirDirection.Right });
        check(state.Me.Location == new Point(12, 10) && state.Me.MotionTime == time, "本人广播重复开始受推或覆盖已确认坐标");
        state.Apply(new S.ObjectMonster { ObjectID = 20, Location = new(8, 8) });
        state.Apply(new S.ObjectPushed { ObjectID = 20, Location = new(8, 9), Direction = MirDirection.Up });
        check(state.Actors[20].Action == MirAction.Pushed && state.Actors[20].Location == new Point(8, 9) && state.User.Location == new Point(12, 10),
            "远端受推丢失动作或改变本人位置");

        foreach (var job in Enum.GetValues<MirClass>())
        foreach (var gender in Enum.GetValues<MirGender>())
        {
            var weapon = job == MirClass.Archer ? 200 : job == MirClass.Assassin ? 100 : 1;
            check(OriginalPlayerArt.Resolve(job, gender, 0, 0, weapon, MirAction.Pushed) == OriginalPlayerArt.Resolve(job, gender, 0, 0, weapon, MirAction.Walking) &&
                OriginalPlayerArt.Animation(job, weapon, MirAction.Pushed) == OriginalPlayerArt.Animation(job, weapon, MirAction.Walking), $"{job}/{gender}受推未使用正确职业/性别的行走造型");
        }
        var actor = new Actor { Id = 1, Kind = ObjectType.Player, Location = new(10, 10), PlayerTiming = new(500, 90) };
        actor.Move(new(12, 10), MirDirection.Right, MirAction.Running, 1000);
        var origin = actor.PositionAt(1250, 600);
        actor.Move(new(13, 10), MirDirection.Left, MirAction.Pushed, 1250);
        check(Vector2.Distance(actor.PositionAt(1250, 600), origin) < .001f && actor.MovementDuration(600) == actor.PlayerTiming.Scale(500),
            "跑步中受推瞬移，或未沿用服务端受推间隔");
        actor.Move(new(14, 10), MirDirection.Left, MirAction.Pushed, 1250);
        var halfway = actor.PositionAt(1250 + actor.MovementDuration(600) / 2, 600);
        check(Vector2.Distance(actor.PositionAt(1250, 600), origin) < .001f && halfway.X > origin.X && halfway.X < 14 &&
            actor.PositionAt(5000, 600) == new Vector2(14, 10), "连续受推没有从当前画面接续，或越过最终坐标");
        actor.Move(new(14, 10), MirDirection.Left, MirAction.Pushed, 1400);
        check(actor.MotionTime == 1250, "重复受推回执重置了动作计时");

        var messages = new List<string>(); state.ChatMessage += (text, _) => messages.Add(text);
        state.Apply(new S.InTrapRock { Trapped = true }); state.Apply(new S.InTrapRock { Trapped = true });
        check(state.InTrapRock && !ClientMovement.CanRun(state, true, 1000, 1500) && messages.Count == 1, "困魔仍可跑步或重复刷提示");
        state.Apply(new S.InTrapRock { Trapped = false });
        check(!state.InTrapRock && messages.Count == 2, "困魔解除没有同步或缺少提示");
        state.Apply(new S.Opendoor { DoorIndex = 3 }); state.Apply(new S.Opendoor { DoorIndex = 3 });
        state.Apply(new S.Opendoor { DoorIndex = 0 });
        check(state.OpenDoors.SetEquals([3]), "开门状态不幂等或记录了无效门编号");
        state.Apply(new S.Opendoor { DoorIndex = 3, Close = true });
        check(state.OpenDoors.Count == 0, "关门回执没有清理打开状态");
        state.Apply(new S.Opendoor { DoorIndex = 5 }); state.Apply(new S.InTrapRock { Trapped = true });
        state.Apply(new S.HeroInformation { ObjectID = 30 });
        check(state.InTrapRock && state.OpenDoors.Contains(5), "英雄资料误清本人困魔/门状态");
        state.Apply(new S.MapChanged { FileName = "new", Location = new(1, 1) });
        check(!state.InTrapRock && state.OpenDoors.Count == 0 && state.User.Location == new Point(1, 1), "切图保留旧门/困魔/受推坐标");
        foreach (Packet reset in new Packet[] { new S.Death(), new S.Revived(), new S.UserInformation { ObjectID = 10, HP = 100, Equipment = new UserItem[14] }, new S.LogOutSuccess(), new S.Disconnect() })
        {
            if (state.User == null) state.Apply(new S.UserInformation { ObjectID = 10, HP = 100, Equipment = new UserItem[14] });
            state.Apply(new S.InTrapRock { Trapped = true }); state.Apply(reset);
            check(!state.InTrapRock, "角色生命周期遗留困魔状态：" + reset.GetType().Name);
        }
        var server = Packet.IsServer;
        try
        {
            Packet.IsServer = false;
            check(Packet.ReceivePacket(new S.Pushed { Location = new(3, 4), Direction = MirDirection.Up }.GetPacketBytes().ToArray(), out var extra) is S.Pushed { Location.X: 3, Location.Y: 4, Direction: MirDirection.Up } && extra.Length == 0,
                "受推原TCP字段往返错误");
            check(Packet.ReceivePacket(new S.InTrapRock { Trapped = true }.GetPacketBytes().ToArray(), out extra) is S.InTrapRock { Trapped: true } && extra.Length == 0, "困魔原TCP字段往返错误");
            check(Packet.ReceivePacket(new S.Opendoor { DoorIndex = 5, Close = true }.GetPacketBytes().ToArray(), out extra) is S.Opendoor { DoorIndex: 5, Close: true } && extra.Length == 0, "门原TCP字段往返错误");
        }
        finally { Packet.IsServer = server; }
    }
}
