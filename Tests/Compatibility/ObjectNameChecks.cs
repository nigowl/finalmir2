using System.Drawing;
using Crystal.Native;
using S = ServerPackets;

internal static class ObjectNameChecks
{
    public static void Run(Action<bool, string> check)
    {
        CheckMonsterRefresh(check);
        var state = new GameState(); state.Apply(new S.ObjectName { ObjectID = 99, Name = "未知名称" });
        check(state.User == null && state.Actors.Count == 0, "未登录改名回执创建了对象");
        state.Apply(new S.UserInformation { ObjectID = 1, Name = "本人", Inventory = [], Equipment = new UserItem[14], QuestInventory = [] });
        state.Apply(new S.ObjectName { ObjectID = 1, Name = "错误本人名称" });
        check(state.User!.Name == "本人" && state.Me!.Name == "本人", "场景改名包覆盖本人身份");
        state.Apply(new S.ObjectPlayer { ObjectID = 2, Name = "旧玩家", NameColour = Color.Blue });
        state.Apply(new S.ObjectMonster { ObjectID = 3, Name = "旧怪物", NameColour = Color.Purple, MasterObjectId = 1 });
        state.Apply(new S.ObjectNPC { ObjectID = 4, Name = "旧NPC" }); state.Apply(new S.ObjectItem { ObjectID = 5, Name = "旧物品" });
        foreach (var id in new uint[] { 2, 3, 4, 5 })
        {
            var actor = state.Actors[id]; var location = new Point((int)id, 8); actor.Location = new(location.X - 1, location.Y);
            actor.Move(location, MirDirection.Right, MirAction.Walking, 1234);
            state.Apply(new S.ObjectName { ObjectID = id, Name = "新名称（" + id + "）" });
            check(ReferenceEquals(actor, state.Actors[id]) && actor.Name == "新名称（" + id + "）" && actor.Location == location &&
                actor.Action == MirAction.Walking && actor.MotionTime == 1234, "名称增量替换对象或重置动作/坐标");
        }
        check(state.Actors[2].Colour == Color.Blue && state.Actors[3].Colour == Color.Purple && state.Actors[3].MasterObjectId == 1,
            "名称包改变颜色或猜测未下发的归属");
        state.Apply(new S.ObjectName { ObjectID = 3, Name = "" }); check(state.Actors[3].Name == "", "原包合法空名未生效");
        state.Apply(new S.ObjectName { ObjectID = 99, Name = "未知对象" }); check(state.Actors.Count == 5, "未知改名包创建了幽灵对象");
        state.Apply(new S.MapChanged { MapIndex = 2 }); state.Apply(new S.ObjectName { ObjectID = 3, Name = "旧图迟到" });
        check(state.Actors.Count == 1 && !state.Actors.ContainsKey(3) && state.Me!.Name == "本人", "迟到改名包恢复旧图对象");
        state.Apply(new S.LogOutSuccess()); state.Apply(new S.ObjectName { ObjectID = 2, Name = "旧角色迟到" });
        state.Apply(new S.UserInformation { ObjectID = 8, Name = "新角色", Inventory = [], Equipment = new UserItem[14], QuestInventory = [] });
        check(state.Actors.Count == 1 && state.Me!.Name == "新角色", "换角保留旧名称或旧对象");
    }

    private static void CheckMonsterRefresh(Action<bool, string> check)
    {
        var state = new GameState();
        S.ObjectMonster Info(uint owner) => new() { ObjectID = 30, Name = "归属检查", Image = Monster.Shinsu,
            Location = new(7, 8), Direction = MirDirection.Right, MasterObjectId = owner, NameColour = Color.Purple, AI = 13,
            Poison = PoisonType.Green, Buffs = [BuffType.Hiding] };
        state.Apply(Info(0)); var actor = state.Actors[30];
        check(actor.Action == MirAction.Appear, "新神兽未保留出现动作");
        foreach (var action in new[] { MirAction.Walking, MirAction.Attack1, MirAction.Spell })
        {
            actor.Move(new(6, 8), MirDirection.Right, MirAction.Standing, 1000);
            actor.Move(new(7, 8), MirDirection.Right, action, 1234); actor.Health = 37;
            actor.ActionSpell = Spell.FireBall; actor.Casting = true;
            foreach (var owner in new uint[] { 1, 2, 0 })
            {
                var packet = Info(owner); packet.Name = owner == 0 ? "归属检查" : $"归属检查({owner})";
                state.Apply(packet);
                check(ReferenceEquals(actor, state.Actors[30]) && actor.MasterObjectId == owner && actor.Name == packet.Name,
                    "完整怪物资料没有原位同步野生/主人/转移/到期归属");
                check(actor.Action == action && actor.MotionTime == 1234 && actor.ActionTime == 1234 && actor.Previous == new Point(6, 8) &&
                    actor.Health == 37 && actor.Casting && actor.ActionSpell == Spell.FireBall && actor.Buffs.Contains(BuffType.Hiding),
                    "仅改归属的全量包重置动作/插值/血量/施法或重播出现动画");
            }
        }
        var refresh = Info(2); refresh.Image = Monster.Hen; refresh.Location = new(10, 9); refresh.Direction = MirDirection.Left;
        refresh.NameColour = Color.Orange; refresh.Dead = refresh.Hidden = refresh.Skeleton = true; refresh.AI = 2;
        refresh.Poison = PoisonType.Red; refresh.Buffs.Clear(); state.Apply(refresh);
        check(ReferenceEquals(actor, state.Actors[30]) && actor.Location == refresh.Location && actor.Previous == refresh.Location &&
            actor.Direction == refresh.Direction && actor.Image == (int)Monster.Hen && actor.Dead && actor.Hidden && actor.Harvested &&
            actor.Colour == Color.Orange && actor.AI == 2 && actor.Poison == PoisonType.Red && actor.Buffs.Count == 0 &&
            actor.Action == MirAction.Standing && !actor.Casting && actor.Health == 37,
            "完整怪物资料未同步权威外观/位置/尸体/Buff或仍插值旧位置");
        state.Apply(Info(0)); check(!actor.Dead && !actor.Hidden && !actor.Harvested && actor.Action == MirAction.Standing,
            "已有怪物恢复活体时重复播放出生或保留旧尸体状态");
        state.Apply(new S.ObjectNPC { ObjectID = 30, Name = "旧类型" }); var npc = state.Actors[30]; state.Apply(Info(1));
        check(!ReferenceEquals(npc, state.Actors[30]) && state.Actors[30].Kind == ObjectType.Monster, "异类对象编号未正确重建怪物");
        state.Apply(new S.ObjectRemove { ObjectID = 30 }); state.Apply(Info(0));
        check(!ReferenceEquals(actor, state.Actors[30]) && state.Actors[30].Health == 100 && state.Actors[30].Action == MirAction.Appear,
            "移除后重新出现的怪物沿用旧对象/血量或漏播出现动作");
    }
}
