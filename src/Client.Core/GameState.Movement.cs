using S = ServerPackets;

namespace Crystal.Native;

public sealed partial class GameState
{
    public bool InTrapRock { get; private set; }
    public HashSet<byte> OpenDoors { get; } = [];

    private bool ApplyMovement(Packet packet)
    {
        switch (packet)
        {
            case S.UserInformation when packet is not S.HeroInformation:
            case S.MapInformation or S.MapChanged or S.LogOutSuccess or S.ReturnToLogin or S.Disconnect:
                OpenDoors.Clear(); InTrapRock = false; return false;
            case S.Death or S.Revived:
                InTrapRock = false; return false;
            case S.Pushed p when User != null:
                User.Location = p.Location; User.Direction = p.Direction;
                if (Me is { } me) Push(me, p.Location, p.Direction);
                return true;
            case S.ObjectPushed p:
                // 本人已经收到Pushed，附近广播不能重复开始同一次受推动作。
                if (p.ObjectID != User?.ObjectID && Actors.TryGetValue(p.ObjectID, out var actor)) Push(actor, p.Location, p.Direction);
                return true;
            case S.InTrapRock p:
                if (User != null && InTrapRock != p.Trapped)
                {
                    InTrapRock = p.Trapped;
                    NotifyMessage(p.Trapped ? "被困魔束缚，暂时无法移动。" : "困魔束缚已解除。", ChatType.System);
                }
                return true;
            case S.Opendoor p:
                if (User != null && p.DoorIndex != 0)
                { if (p.Close) OpenDoors.Remove(p.DoorIndex); else OpenDoors.Add(p.DoorIndex); }
                return true;
            default: return false;
        }
    }

    private static void Push(Actor actor, System.Drawing.Point location, MirDirection direction)
    {
        actor.Move(location, direction, MirAction.Pushed);
        actor.Casting = false; actor.ActionSpell = Spell.None;
    }
}
