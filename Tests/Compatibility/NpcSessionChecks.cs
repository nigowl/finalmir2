using System.Runtime.CompilerServices;
using Server.MirDatabase;
using Server.MirObjects;
using S = ServerPackets;

internal static class NpcSessionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var script = (NPCScript)RuntimeHelpers.GetUninitializedObject(typeof(NPCScript));
        var main = new NPCPage(NPCScript.MainKey);
        main.SegmentList.Add(new NPCSegment(main, [], [NPCScript.BuyNewKey, NPCScript.RepairKey], [], [], []));
        var buy = new NPCPage(NPCScript.BuyNewKey);
        var repair = new NPCPage(NPCScript.RepairKey);
        script.NPCPages = [main, buy, repair]; script.Goods = [];
        var player = new NpcPlayer { Info = new CharacterInfo(), Account = new AccountInfo() };
        script.Call(player, 71, main.Key);
        check(player.NPCPage == main && player.NPCObjectID == 71, "NPC main did not establish its authorized dialogue");
        script.Call(player, 72, buy.Key);
        check(player.NPCPage == main, "A different NPC reused the current dialogue's shop link");
        script.Call(player, 71, buy.Key);
        check(player.NPCPage == buy && player.Packets.OfType<S.NPCGoods>().Any(), "A built-in shop page without dialogue sent goods but retained an invalid purchase session");
        player.Packets.Clear(); script.Call(player, 71, repair.Key);
        check(player.NPCPage == buy && !player.Packets.OfType<S.NPCRepair>().Any(), "An unoffered link bypassed NPC page validation");
        script.Call(player, 71, main.Key); script.Call(player, 71, repair.Key);
        check(player.NPCPage == repair && player.Packets.OfType<S.NPCRepair>().Any(), "An empty repair page retained the previous NPC session");
        var fresh = new NpcPlayer { Info = new CharacterInfo(), Account = new AccountInfo() };
        script.Call(fresh, 71, buy.Key);
        check(fresh.NPCPage == null && fresh.Packets.Count == 0, "An unsolicited shop page created a session or threw");
    }

    private sealed class NpcPlayer : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
    }
}
