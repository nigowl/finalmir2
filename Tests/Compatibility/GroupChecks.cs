using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class GroupChecks
{
    public static void Run(Action<bool, string> check)
    {
        var map = new Map(new MapInfo { Index = 100, Title = "Group test" });
        var nodes = new LinkedList<MapObject>();
        GroupPlayer Player(string name)
        {
            var account = new AccountInfo();
            var player = new GroupPlayer { Info = new CharacterInfo { Name = name, AccountInfo = account, AllowGroup = true, HP = 100 }, Account = account, Stats = new Stats { [Stat.HP] = 100 }, CurrentMap = map };
            player.Node = nodes.AddLast(player); return player;
        }
        var leader = Player("Leader"); var member = Player("Member"); var third = Player("Third");
        member.GroupInvitation = leader; member.GroupInvite(true);
        check(leader.GroupMembers.Count == 2 && ReferenceEquals(member.GroupMembers, leader.GroupMembers) && leader.GroupMembers[0] == leader, "Original group consent did not create a shared ordered party");
        check(member.Packets.OfType<S.AddMember>().Select(p => p.Name).SequenceEqual(new[] { "Leader", "Member" }), "Group membership packets lost leader/member order");
        member.DelMember("Leader"); check(leader.GroupMembers.Count == 2, "Non-leader removed a party member");
        third.GroupInvitation = leader; third.GroupInvite(true);
        leader.LeaveGroup();
        check(member.GroupMembers.Count == 2 && member.GroupMembers[0] == member && ReferenceEquals(member.GroupMembers, third.GroupMembers) && leader.GroupMembers == null, "Leaving leader did not transfer party leadership consistently");
        member.DelMember("Third");
        check(member.GroupMembers == null && third.GroupMembers == null && third.Packets.OfType<S.DeleteGroup>().Any(), "Removing the last partner did not clear both parties");
        member.GroupInvitation = leader; member.SwitchGroup(false);
        check(member.GroupInvitation == null, "Disabling groups retained an outstanding invitation");
        member.GroupInvitation = leader; member.GroupInvite(true);
        check(member.GroupInvitation == null && member.GroupMembers == null && leader.GroupMembers == null, "Disabled recipient accepted an old group invitation");
        member.AllowGroup = true;
        member.CurrentMap = new Map(new MapInfo { Index = 101, Title = "Solo test", NoGroup = true });
        member.GroupInvitation = leader; member.GroupInvite(true);
        check(member.GroupInvitation == null && member.GroupMembers == null && leader.GroupMembers == null, "Recipient moved onto a solo map and accepted a stale invitation");
        member.CurrentMap = map; member.GroupInvitation = leader; member.GroupInvite(false);
        check(member.GroupInvitation == null && leader.GroupMembers == null, "Declining invitation created a party");
    }
    private sealed class GroupPlayer : PlayerObject
    {
        public readonly List<Packet> Packets = new();
        public override void Enqueue(Packet packet) => Packets.Add(packet);
        public override void ReceiveChat(string text, ChatType type) { }
    }
}
