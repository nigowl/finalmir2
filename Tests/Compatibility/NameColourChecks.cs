using System.Drawing;
using Crystal.Native;
using S = ServerPackets;

internal static class NameColourChecks
{
    public static void Run(Action<bool, string> check)
    {
        var state = new GameState();
        state.Apply(new S.ColourChanged { NameColour = Color.Red });
        state.Apply(new S.ObjectColourChanged { ObjectID = 99, NameColour = Color.Blue });
        check(state.User == null && state.Actors.Count == 0, "未登录颜色回执创建了角色或场景对象");
        state.Apply(new S.UserInformation { ObjectID = 1, NameColour = Color.Red, Inventory = [], QuestInventory = [], Equipment = new UserItem[14] });
        check(state.Me!.Colour.ToArgb() == Color.Red.ToArgb(), "登录本人姓名丢失服务端初始颜色");
        state.Apply(new S.ObjectPlayer { ObjectID = 2, NameColour = Color.Blue });
        state.Apply(new S.ObjectMonster { ObjectID = 3, NameColour = Color.Purple });
        state.Apply(new S.ObjectNPC { ObjectID = 4, NameColour = Color.Yellow });
        state.Apply(new S.ObjectItem { ObjectID = 5, NameColour = Color.Lime });
        check(state.Actors[2].Colour == Color.Blue && state.Actors[3].Colour == Color.Purple &&
            state.Actors[4].Colour == Color.Yellow && state.Actors[5].Colour == Color.Lime, "场景对象丢失原包名称颜色");
        state.Apply(new S.ColourChanged { NameColour = Color.Yellow });
        check(state.User!.NameColour == Color.Yellow && state.Me.Colour == Color.Yellow && state.Actors[2].Colour == Color.Blue,
            "本人名称颜色回执没有同步人物与场景，或覆盖其他对象");
        state.Apply(new S.ObjectColourChanged { ObjectID = 1, NameColour = Color.Black });
        check(state.User.NameColour == Color.Yellow && state.Me.Colour == Color.Yellow, "他人颜色广播覆盖本人专属回执");
        foreach (var id in new uint[] { 2, 3, 4, 5 })
        {
            var colour = Color.FromArgb(127, 11, 22, (int)id);
            state.Apply(new S.ObjectColourChanged { ObjectID = id, NameColour = colour });
            check(state.Actors[id].Colour == colour && state.Me.Colour == Color.Yellow, "对象颜色增量未按场景编号更新或丢失ARGB");
        }
        state.Apply(new S.ObjectColourChanged { ObjectID = 99, NameColour = Color.Red });
        check(state.Actors.Count == 5 && !state.Actors.ContainsKey(99), "未知对象颜色包创建了幽灵对象");
        state.Apply(new S.MapChanged { MapIndex = 2 });
        state.Apply(new S.ObjectColourChanged { ObjectID = 2, NameColour = Color.Red });
        check(state.Actors.Count == 1 && state.Me.Colour == Color.Yellow && state.User.NameColour == Color.Yellow,
            "切图丢失本人颜色或迟到颜色包恢复旧图对象");
        state.Apply(new S.LogOutSuccess());
        state.Apply(new S.ColourChanged { NameColour = Color.Black });
        state.Apply(new S.UserInformation { ObjectID = 6, NameColour = Color.White, Inventory = [], QuestInventory = [], Equipment = new UserItem[14] });
        check(state.Me!.Colour == Color.White && state.User!.NameColour == Color.White && state.Actors.Count == 1,
            "换角泄漏上个角色的名称颜色");
    }
}
